import test_inputs
"""Import-only workflow against a disposable SQL database; never changes TLS/TRS data."""
import copy,io,json,socket,subprocess,tempfile,time,urllib.request,urllib.error,zipfile
from concurrent.futures import ThreadPoolExecutor
from datetime import date,timedelta,datetime,timezone
from pathlib import Path
from sql_test_database import TestDatabase,root,project,dll
base='http://localhost:5193/api/';checks=0;process=None
def request(path,body=None,status=200,method=None):
    global checks
    data=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
    req=urllib.request.Request(base+path,data=data,method=method or ('POST' if data is not None else 'GET'),headers={'Content-Type':'application/octet-stream' if isinstance(body,bytes) else 'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=20) as r:code=r.status;raw=r.read()
    except urllib.error.HTTPError as e:code=e.code;raw=e.read()
    assert code==status,(path,code,status,raw[:2000]);checks+=1
    if 'template' in path:return raw
    return json.loads(raw) if raw else None
def state():return request('setup/')
def preview(package,replace=None,status=200,kind='trs'):
    q=f'setup/preview?kind={kind}&revision={state()["data"]["revision"]}'
    if replace:q+='&replaceEventId='+replace
    return request(q,json.dumps(package).encode() if not isinstance(package,bytes) else package,status)
def confirm(p,status=200):return request('setup/confirm',{'revision':p['revision'],'token':p['token']},status)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5193))!=0,'Test port already occupied'
database=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-import-tests-') as temp:
    log=open(Path(temp)/'server.log','w',encoding='utf-8')
    def start():
        global process
        process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**database.env,'ASPNETCORE_URLS':'http://localhost:5193'},stdout=log,stderr=log)
        for _ in range(100):
            if process.poll() is not None:raise RuntimeError('Test server exited')
            try:return state()
            except urllib.error.URLError:time.sleep(.1)
        raise RuntimeError('Test server did not start')
    def stop():
        global process
        if process:process.terminate();process.wait(timeout=10);process=None
    try:
        assert start()['data']['events']==[]
        p=test_inputs.package()
        p['exportedAt']=datetime.now(timezone.utc).isoformat()
        p['events'][0]['startDate']=(date.today()+timedelta(days=10)).isoformat();p['events'][0]['endDate']=(date.today()+timedelta(days=11)).isoformat()
        # Failures must never change persistent state.
        def bad(mutator,status=400):
            before=state();v=copy.deepcopy(p);mutator(v);preview(v,status=status);assert state()==before
        bad(lambda x:x.update(hasFixtures=True))
        bad(lambda x:x.pop('hasFixtures'))
        bad(lambda x:x.update(registrationClosed=False))
        bad(lambda x:x.pop('registrationClosed'))
        bad(lambda x:x.update(sourceSystemId=''))
        bad(lambda x:x.update(sourceTimeZone='invalid-zone'))
        bad(lambda x:x.update(schemaVersion=1))
        bad(lambda x:x.update(events=[]))
        bad(lambda x:x['events'].append(x['events'][0]))
        bad(lambda x:x.update(entries=[]))
        bad(lambda x:x['participants'].append(x['participants'][0]))
        bad(lambda x:x['participants'][0].update(email='invalid'))
        bad(lambda x:x['participants'][0].update(mobile='bad'))
        bad(lambda x:x['participants'][0].update(dob='2099-01-01'))
        bad(lambda x:x['participants'][0].update(dob='2020-01-01'))
        bad(lambda x:x['programs'][0].update(maxCapacity=1))
        bad(lambda x:x['programs'][0].update(minCapacity=3))
        bad(lambda x:x['entries'][0].update(status='X'))
        bad(lambda x:[program.update(status='X') for program in x['programs']])
        bad(lambda x:x['entries'][2].update(participantIds=['P001']))
        bad(lambda x:x['entries'][2].update(participantIds=['P001','P003']))
        bad(lambda x:x['entries'][0].update(participantIds=['missing']))
        bad(lambda x:x['entries'][0].update(eventId='other'))
        bad(lambda x:x['events'][0].update(maxProgramsPerParticipant=1))
        bad(lambda x:x['entries'].append({**x['entries'][0],'id':'new'}))
        bad(lambda x:x['entries'][1].update(seed=1))
        bad(lambda x:x['events'][0].update(startDate='2000-01-01',endDate='2000-01-02'))
        bad(lambda x:x.update(fixtures=[{'id':'existing'}]))
        bad(lambda x:x['entries'][0].update(participantIds=None))
        # One person can enter multiple programs, optional SBA ID stays blank.
        ready=preview(p);assert all(r['ready'] for r in ready['readiness']);assert state()['data']['events']==[]
        saved=confirm(ready);eventId=saved['data']['events'][0]['id'];assert len(saved['data']['participants'])==4
        assert all(x['sbaId']=='' for x in saved['data']['participants'])
        assert saved['data']['imports'][0]['sourceEventId']=='EV001'
        confirm(ready,409);preview(p,status=409)
        # Names catch cross-source and changed-ID duplicates; source IDs still protect linkage.
        for name in ['import test event','  IMPORT   TEST   EVENT  ','Import\tTest\tEvent']:
            duplicate=copy.deepcopy(p);duplicate['sourceSystemId']='another-source';duplicate['events'][0]['name']=name
            before=state();error=preview(duplicate,status=409);assert error['message']=='An event with this name already exists in TLS.';assert state()==before
        renamed=copy.deepcopy(p);renamed['events'][0]['name']='Renamed same identity';preview(renamed,status=409)
        # Server-side editing and retired routes are unavailable.
        for route in ['setup/records','setup/trs/login','setup/trs/events','setup/trs/download','import','courts','matches/M1']:
            request(route,{},404)
        request('state',status=404)
        # New file cannot replace a different source identity.
        wrong=copy.deepcopy(p);wrong['sourceSystemId']='different';preview(wrong,eventId,status=409)
        # Invalid replacement leaves the original intact.
        wrong=copy.deepcopy(p);wrong['entries'][0]['participantIds']=['missing'];before=state();preview(wrong,eventId,status=400);assert state()==before
        # Confirmed replacement changes source fields but not linkage or event identity.
        edited=copy.deepcopy(p);edited['events'][0]['name']='Corrected source name';edited['participants'][0]['email']='corrected@example.com'
        beforeIds=[x['id'] for x in saved['data']['participants']];changed=confirm(preview(edited,eventId))
        assert [x['id'] for x in changed['data']['participants']]==beforeIds
        assert changed['data']['events'][0]['name']=='Corrected source name'
        # Remove an entire entry and its now-unused people through full replacement.
        shorter=copy.deepcopy(edited);shorter['entries']=shorter['entries'][:2];shorter['participants']=shorter['participants'][:2]
        before=state();preview(shorter,eventId,status=400);assert state()==before
        shorter['programs']=shorter['programs'][:1]
        changed=confirm(preview(shorter,eventId));assert len(changed['data']['participants'])==2
        assert all(r['ready'] for r in changed['readiness'])
        # Competing previews: exactly one commits and one is stale.
        a=preview(edited,eventId);b=preview(shorter,eventId)
        def attempt(x):
            req=urllib.request.Request(base+'setup/confirm',data=json.dumps({'revision':x['revision'],'token':x['token']}).encode(),headers={'Content-Type':'application/json'})
            try:
                with urllib.request.urlopen(req,timeout=20) as r:return r.status
            except urllib.error.HTTPError as e:return e.code
        with ThreadPoolExecutor(max_workers=2) as pool:assert sorted(pool.map(attempt,[a,b]))==[200,409]
        # Excel: blank template and real formatted workbook.
        template=request('setup/template');assert zipfile.is_zipfile(io.BytesIO(template));preview(template,status=400,kind='excel')
        workbook=test_inputs.workbook(template)
        excel=preview(workbook,kind='excel');assert excel['source']=='EXCEL';assert len(excel['data']['participants'])==4
        assert excel['data']['participants'][0]['dob']=='1990-01-10'
        assert len(excel['data']['entries'])==4
        assert excel['data']['events'][0]['venue']==''
        assert all(e['status']=='A' for e in excel['data']['entries'])
        assert excel['sourceEventId']=='EV001'
        import xml.etree.ElementTree as ET
        ns='{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
        def change_cell(sheet,cell,value,source=workbook):
            target=io.BytesIO()
            with zipfile.ZipFile(io.BytesIO(source)) as src,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as dst:
                for name in src.namelist():
                    raw=src.read(name)
                    if name==f'xl/worksheets/sheet{sheet}.xml':
                        tree=ET.fromstring(raw);node=next(c for c in tree.iter(ns+'c') if c.get('r')==cell)
                        for child in list(node):node.remove(child)
                        node.set('t','inlineStr');ET.SubElement(ET.SubElement(node,ns+'is'),ns+'t').text=value
                        raw=ET.tostring(tree)
                    dst.writestr(name,raw)
            return target.getvalue()
        # A native Excel workbook with the same event name as a TRS import is blocked.
        before=state();error=preview(change_cell(1,'B2','  corrected   SOURCE name  '),status=409,kind='excel')
        assert error['message']=='An event with this name already exists in TLS.';assert state()==before
        # Replacement cannot rename an event to another existing event's name.
        second=copy.deepcopy(edited);second['sourceSystemId']='second-source';second['events'][0].update(name='Separate Event',startDate=(date.today()+timedelta(days=20)).isoformat(),endDate=(date.today()+timedelta(days=21)).isoformat())
        secondSaved=confirm(preview(second));secondId=next(i['eventId'] for i in secondSaved['data']['imports'] if i['sourceSystemId']=='second-source')
        collision=copy.deepcopy(second);collision['events'][0]['name']='Corrected source name'
        before=state();preview(collision,secondId,status=409);assert state()==before
        def invalid_excel(sheet,cell,value):
            before=state();preview(change_cell(sheet,cell,value),status=400,kind='excel');assert state()==before
        invalid_excel(3,'D4','Different person with same ID')
        before=state();preview(change_cell(3,'L5','2',change_cell(3,'L4','1')),status=400,kind='excel');assert state()==before
        invalid_excel(3,'C5','wrong-group') # Split doubles pair.
        invalid_excel(3,'B2','missing-program')
        invalid_excel(2,'C2','Unknown')
        invalid_excel(1,'C2','Unknown')
        invalid_excel(1,'A2','')
        invalid_excel(1,'A1','OldEventID')
        invalid_excel(3,'A1','OldParticipantID')
        confirm(excel,409);confirm(preview(workbook,kind='excel'));preview(workbook,status=409,kind='excel')
        # Formula injection is rejected, without partial import.
        target=io.BytesIO()
        with zipfile.ZipFile(io.BytesIO(workbook)) as src,zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED) as dst:
            for name in src.namelist():
                value=src.read(name)
                if name=='xl/worksheets/sheet2.xml':
                    tree=ET.fromstring(value);row=ET.SubElement(tree.find(ns+'sheetData'),ns+'row',{'r':'101'});cell=ET.SubElement(row,ns+'c',{'r':'A101'})
                    ET.SubElement(cell,ns+'f').text='1+1';ET.SubElement(cell,ns+'v').text='2';value=ET.tostring(tree)
                dst.writestr(name,value)
        preview(target.getvalue(),status=400,kind='excel')
        # Zone changes preserve dates and reject stale settings saves.
        current=state()['data'];request('setup/settings',{'revision':current['revision'],'timeZone':'+08:00'},method='PUT')
        request('setup/settings',{'revision':current['revision'],'timeZone':'+08:00'},409,method='PUT')
        current=state()['data']
        request('setup/settings',{'revision':current['revision'],'timeZone':current['timeZone'],'appName':'TLS UAT','logoDarkUrl':'/logo.svg'},method='PUT')
        assert state()['data']['appName']=='TLS UAT'
        assert state()['data']['logoDarkUrl']=='/logo.svg'
        current=state()['data'];request('setup/settings',{'revision':current['revision'],'timeZone':current['timeZone'],'logoLightUrl':'javascript:alert(1)'},400,method='PUT')
        before=state();request('setup/settings',{'revision':before['data']['revision'],'timeZone':'bad'},400,method='PUT');assert state()==before
        stop();start();assert state()==before
        assert int(database._sql('audit'))>=6
        print(f'PASS: {checks} HTTP assertions; imports, validation, read-only routes, source identity, replacement, concurrency, Excel parsing and restart persistence.')
    except Exception:
        log.flush();print((Path(temp)/'server.log').read_text(encoding='utf-8'));raise
    finally:stop();log.close()
finally:database.close()
