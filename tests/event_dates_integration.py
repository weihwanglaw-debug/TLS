"""Calendar rules and date editing against disposable SQL, never TLS/TRS."""
import copy,io,json,socket,subprocess,tempfile,time,urllib.request,urllib.error,zipfile
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor
from datetime import date,timedelta,datetime,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs

base='http://localhost:5192/api/';checks=0;process=None
def call(path,body=None,status=200,method=None):
 global checks
 raw=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
 req=urllib.request.Request(base+path,data=raw,method=method or ('POST' if raw is not None else 'GET'),headers={'Content-Type':'application/octet-stream' if isinstance(body,bytes) else 'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as response:code=response.status;content=response.read()
 except urllib.error.HTTPError as error:code=error.code;content=error.read()
 assert code==status,(path,code,status,content[:1200]);checks+=1
 return content if path=='setup/template' else json.loads(content)
def state():return call('setup/')['data']
def check(value):
 global checks
 assert value;checks+=1
def day(n):return (date.today()+timedelta(days=n)).isoformat()
def package(name,first,last):
 p=test_inputs.package();p['sourceSystemId']='date-tests-'+name;p['events'][0].update(name=name,startDate=day(first),endDate=day(last));return p
def preview(p,status=200,kind='trs',replace=None):return call('setup/preview?kind='+kind+'&revision='+str(state()['revision'])+('&replaceEventId='+replace if replace else ''),p,status)
def confirm(p,status=200):return call('setup/confirm',{'revision':p['revision'],'token':p['token']},status)
def dates(eid,first,last,status=200,revision=None):return call('tournament/event-dates',{'revision':state()['revision'] if revision is None else revision,'eventId':eid,'startDate':day(first),'endDate':day(last)},status,'PUT')
def mutate(path,body,status=200,method='PUT'):return call(path,{'revision':state()['revision'],**body},status,method)

with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5192))!=0
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-event-dates-') as folder:
  with open(Path(folder)/'server.log','w') as log:
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=log,stderr=log)
   try:
    for _ in range(150):
     try:state();break
     except urllib.error.URLError:time.sleep(.1)
    original=package('Calendar A',10,11);saved=confirm(preview(original))['data'];a=saved['events'][0]['id']
    second=package('Calendar B',20,21);saved=confirm(preview(second))['data'];b=next(e['id'] for e in saved['events'] if e['name']=='Calendar B')
    check(call('tournament/'+a)['datesEditable'])
    for first,last in [(9,10),(10,10),(11,12),(9,12),(10,11),(20,20),(21,22)]:
     before=state();error=preview(package('Conflicting event',first,last),409);check('overlap' in error['message']);check(state()==before)
    before=state();dates(a,19,20,409);check(state()==before)
    dates(a,15,14,400);check(state()==before)
    ids={key:copy.deepcopy(before[key]) for key in ['programs','participants','entries','imports']}
    updated=dates(a,8,9);check(updated['eventInfo']['startDate']==day(8));check(updated['eventInfo']['id']==a)
    after=state();check(all(after[key]==value for key,value in ids.items()))
    dates(a,10,11,409,revision=before['revision']);check(next(e for e in state()['events'] if e['id']==a)['startDate']==day(8))
    dates(a,10,11)
    # Adjacent days are allowed; source Excel uses the same shared calendar validation.
    template=call('setup/template');workbook=test_inputs.workbook(template);ns='{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
    def excel(first,last):
     out=io.BytesIO()
     with zipfile.ZipFile(io.BytesIO(workbook)) as source,zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as dest:
      for name in source.namelist():
       raw=source.read(name)
       if name=='xl/worksheets/sheet1.xml':
        root=ET.fromstring(raw)
        for cell,value in {'A2':'EXCEL-DATES','B2':'Excel calendar','D2':day(first),'E2':day(last)}.items():
         node=next(c for c in root.iter(ns+'c') if c.get('r')==cell)
         for child in list(node):node.remove(child)
         node.set('t','inlineStr');ET.SubElement(ET.SubElement(node,ns+'is'),ns+'t').text=value
        raw=ET.tostring(root)
       dest.writestr(name,raw)
     return out.getvalue()
    before=state();preview(excel(11,12),409,'excel');check(state()==before)
    saved=confirm(preview(excel(12,13),kind='excel'))['data'];x=next(e['id'] for e in saved['events'] if e['name']=='Excel calendar')
    dates(x,14,15);dates(x,11,12,409)
    # Replacement is checked too, even when the event's own identity is excluded.
    replacement=copy.deepcopy(original);replacement['events'][0].update(startDate=day(20),endDate=day(21))
    preview(replacement,409,replace=a)
    # Two simultaneously prepared imports cannot reserve the same days.
    left=preview(package('Concurrent left',40,41));right=preview(package('Concurrent right',40,41))
    def commit_status(p):
     req=urllib.request.Request(base+'setup/confirm',data=json.dumps({'revision':p['revision'],'token':p['token']}).encode(),headers={'Content-Type':'application/json'})
     try:
      with urllib.request.urlopen(req,timeout=30) as response:return response.status
     except urllib.error.HTTPError as error:return error.code
    with ThreadPoolExecutor(max_workers=2) as pool:check(sorted(pool.map(commit_status,[left,right]))==[200,409])
    check(sum(e['startDate']==day(40) for e in state()['events'])==1)
    plan={'eventId':a,'courts':[1,2],'courtsConfigured':True,'dailySessions':[{'start':'10:00:00','end':'18:00:00'}],'officials':[{'id':'o','name':'Official One'}],'matchMinutes':30,'restMinutes':0}
    mutate('tournament/event',{'plan':plan});dates(a,8,9);check(not call('tournament/'+a)['dailySessions']);dates(a,10,11)
    legacy={**plan,'dailySessions':[],'sessions':[{'date':day(d),'start':'10:00:00','end':'18:00:00'} for d in [10,11]]}
    mutate('tournament/event',{'plan':legacy});dates(a,8,9);check(not call('tournament/'+a)['eventPlan']['sessions']);dates(a,10,11);mutate('tournament/event',{'plan':plan})
    mutate('setup/settings',{'timeZone':state()['timeZone'],'courtCount':1});check(call('tournament/'+a)['eventCourts']==[1]) # Obsolete event pool [1,2] does not prevent a safe master change.
    mutate('setup/settings',{'timeZone':state()['timeZone'],'courtCount':4})
    pid=next(p['id'] for p in state()['programs'] if p['eventId']==a and p['entryType']=='S')
    rules={'name':'Date test','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
    preset=mutate('presets/',{'preset':{'id':'','name':'Date test','rules':rules,'status':'A','isDefault':True}})['presets'][0]['id']
    def generate():
     draft=mutate('tournament/preview',{'eventId':a,'programIds':[pid],'courts':[1,2],'earliestStart':day(10)+'T10:00:00','settings':{'format':'KO','groupCount':1,'advancePerGroup':1},'seeds':{e['id']:None for e in state()['entries'] if e['programId']==pid}},method='POST')
     check(call('tournament/'+a)['datesEditable']) # Unsaved previews do not lock dates.
     return mutate('tournament/confirm',{'token':draft['token']},method='POST')
    draw=generate();check(not draw['datesEditable']);before=state();dates(a,8,9,400);check(state()==before)
    mutate('tournament/reset',{'programId':pid},400,method='POST');m=next(p for p in draw['plans'] if p['programId']==pid)['matches'][0];mutate('tournament/reset-schedule',{'eventId':a,'roundKeys':[pid+':'+m['phase']+':'+str(m['round'])]},method='POST');check(call('tournament/'+a)['datesEditable']);mutate('tournament/reset',{'programId':pid},method='POST');dates(a,8,9);dates(a,10,11);mutate('tournament/event',{'plan':plan})
    draw=generate();m=next(p for p in draw['plans'] if p['programId']==pid)['matches'][0]
    mutate('tournament/result',{'matchId':m['id'],'matchVersion':m['matchVersion'],'games':[{'a':5,'b':0}],'presetId':preset});check(not call('tournament/'+a)['datesEditable']);dates(a,8,9,400)
    print(f'{checks} event-date/import calendar checks passed; disposable SQL removed on exit.')
   finally:
    if process:process.terminate();process.wait(timeout=10);process=None
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
