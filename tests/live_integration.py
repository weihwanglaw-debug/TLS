"""Live court/device lifecycle in an isolated SQL database. Never writes to TLS/TRS."""
import copy,json,socket,subprocess,tempfile,time,uuid,urllib.request,urllib.error
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,root,project,dll
base='http://localhost:5198/api/';checks=0;process=None;sessions={}

def call(path,body=None,status=200,token=None,method=None,headers=None):
    global checks
    req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {}),**(headers or {})})
    try:
        with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
    except urllib.error.HTTPError as e:code=e.code;raw=e.read()
    assert code==status,(path,code,status,raw[:1500]);checks+=1;return json.loads(raw)
def dashboard():return call('dashboard/')
def setup():return call('setup/')['data']
def admin(path,body,status=200):return call('dashboard/'+path,{'revision':dashboard()['revision'],**body},status)
def pair(match,court,umpire,reason='',replace=False):return admin('pair',{'matchId':match['id'],'court':court,'umpireId':umpire,'reason':reason,'replaceWaiting':replace,'presetId':presetId,'reviewAvailable':True})
def tablet(code,name):
    p=call('tablet/pair',{'code':code,'deviceId':name+'-device','deviceName':name});sessions[p['token']]=p['match'];return p['token']
def command(token,action,side=None,status=200,body=None):
    request=body or {'version':sessions[token]['match']['version'],'operationId':str(uuid.uuid4()),'action':action,**({'side':side} if side else {})}
    if body is None and action in ['START','NEXT']:
        m=sessions[token]['match'];side='A' if action=='START' else ('A' if m['games'][-1]['a']>m['games'][-1]['b'] else 'B')
        request['serving']={'serverSide':side,'serverId':next(p['id'] for p in m['players'] if p['side']==side),'receiverId':next(p['id'] for p in m['players'] if p['side']!=side),'leftSide':('B' if m.get('serving',{}).get('leftSide')=='A' else 'A') if action=='NEXT' else 'A'}
    result=call('tablet/command',request,status,token)
    if status==200:sessions[token]=result
    return result,request

with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5198))!=0,'Live test port occupied'
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-live-test-') as temp:
    log=open(Path(temp)/'server.log','w',encoding='utf-8')
    def start():
        global process
        process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5198','LiveScore__HeartbeatSeconds':'1','LiveScore__DisconnectSeconds':'2'},stdout=log,stderr=log)
        for _ in range(100):
            if process.poll() is not None:raise RuntimeError('Live test server exited')
            try:return setup()
            except urllib.error.URLError:time.sleep(.1)
        raise RuntimeError('Live test server did not start')
    def stop():
        global process
        if process:process.terminate();process.wait(timeout=10);process=None
    try:
        start();empty=dashboard();assert len(empty['courts'])==4 and all(c['status']=='A' for c in empty['courts'])
        now=datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0);first=(now-timedelta(days=1)).date();last=(now+timedelta(days=1)).date()
        people=[{'id':f'p{i}','eventId':'E','name':f'Player {i}','dob':'1990-01-01','gender':'M','email':f'p{i}@example.com','mobile':f'+658000000{i}'} for i in range(1,7)]
        programs=[{'id':p,'eventId':'E','name':'Program '+p,'sport':'Badminton','entryType':'S','minPlayers':1,'maxPlayers':1,'minAge':1,'maxAge':100,'gender':'O','minCapacity':2,'maxCapacity':16,'capacityUnit':'E','status':'A'} for p in ['A','B','C']]
        entries=[{'id':p+str(i),'eventId':'E','programId':p,'participantIds':[person],'status':'A'} for p,ids in [('A',['p1','p2']),('B',['p1','p3']),('C',['p4','p5','p6'])] for i,person in enumerate(ids)]
        package={'schemaVersion':2,'source':'TRS','sourceSystemId':'live-test','sourceTimeZone':'Asia/Singapore','exportedAt':datetime.now(timezone.utc).isoformat(),'registrationClosed':True,'hasFixtures':False,'events':[{'id':'E','name':'Live lifecycle test','venue':'Test Hall','startDate':first.isoformat(),'endDate':last.isoformat(),'status':'D'}],'programs':programs,'participants':people,'entries':entries}
        preview=call('setup/preview?kind=trs&revision='+str(setup()['revision']),package);saved=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];event=saved['events'][0]['id'];pids={p['name'][-1]:p['id'] for p in saved['programs']}
        def view():return call('tournament/'+event)
        def mutation(path,body):return call('tournament/'+path,{'revision':view()['revision'],**body},method='PUT')
        plan={'eventId':event,'sessions':[{'date':(first+timedelta(days=i)).isoformat(),'start':'00:00:00','end':'23:59:00'} for i in range(3)],'officials':[{'id':event+'-O001','name':'Umpire One'},{'id':event+'-O002','name':'Umpire Two'}],'matchMinutes':5,'restMinutes':0,'reviewCourts':[1,2]}
        mutation('event',{'plan':plan});rules={'name':'Live test five points','gamesToWin':1,'pointsToWin':5,'winBy':2,'pointCap':7,'intervalAt':3,'decidingEndsAt':3,'cardsEnabled':True,'countShuttles':True,'challengesEnabled':True,'challengesPerGame':2}
        mutation('draw-settings',{'eventId':event,'programIds':list(pids.values()),'groupCount':1,'advancePerGroup':2})
        presetId=call('presets/',{'revision':view()['revision'],'preset':{'id':'','name':rules['name'],'status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
        for pid in pids.values():mutation('seeds',{'programId':pid,'seeds':{e['id']:None for e in saved['entries'] if e['programId']==pid}})
        gen={'eventId':event,'programIds':list(pids.values()),'courts':[1,2],'earliestStart':(now-timedelta(minutes=5)).isoformat(),'matchMinutes':5}
        generated=call('tournament/preview',{'revision':view()['revision'],**gen});call('tournament/confirm',{'revision':generated['revision'],'token':generated['token']})
        matches=[m for p in view()['plans'] for m in p['matches']];a=next(m for m in matches if m['programId']==pids['A']);b=next(m for m in matches if m['programId']==pids['B']);cm=[m for m in matches if m['programId']==pids['C']]
        queued=dashboard()['queue']
        for courtRow in dashboard()['courts']:
            expected=sorted([q for q in queued if q['match']['court']==courtRow['number'] and q['match']['plannedStart'] and q['match']['plannedEnd']],key=lambda q:(q['match']['plannedStart'],q['match']['number']))
            assert (courtRow['next']['match']['id'] if courtRow['next'] else None)==(expected[0]['match']['id'] if expected else None)
        assert len(dashboard()['queue'])==5;assert next(q for q in dashboard()['queue'] if q['match']['id']==a['id'])['delayed']
        call('tablet/state',status=401);call('dashboard/',status=403,headers={'Host':'evil.test:5198'});call('tablet/pair',{'code':'ZZZZZZZZ','deviceId':'invalid-device','deviceName':'Invalid'},401)
        invitation=pair(a,1,event+'-O001');codeA=invitation['code'];assert len(codeA)==8
        assert 170<(datetime.fromisoformat(invitation['expiresAt'])-datetime.now(timezone.utc)).total_seconds()<=180
        db._sql('expire-pairing');call('tablet/pair',{'code':codeA,'deviceId':'expired-device','deviceName':'Expired'},401)
        renewed=admin('pairing',{'matchId':a['id'],'version':invitation['match']['version']});codeA=renewed['code']
        assert codeA!=invitation['code']
        call('tablet/pair',{'code':invitation['code'],'deviceId':'old-code-device','deviceName':'Old code'},401)
        reserved=next(o for o in dashboard()['officials'] if o['id']==event+'-O001')['reservations']
        assert reserved==[{'matchId':a['id'],'court':1}]
        # A waiting call reserves the umpire, even though another court is free.
        admin('pair',{'matchId':cm[0]['id'],'court':3,'umpireId':event+'-O001','presetId':presetId,'reviewAvailable':True},400)
        codeA=pair(a,1,event+'-O001')['code'] # Renewing its own waiting assignment is allowed.
        call('tablet/pair',{'code':codeA,'deviceId':'device-id','deviceName':'Tablet','ipAddress':'10.0.0.99'},400)
        tokenA=tablet(codeA,'Tablet A');db._sql('expire-pairing');call('tablet/state',token=tokenA);call('tablet/pair',{'code':codeA,'deviceId':'another-device','deviceName':'Another'},401)
        assert 'email' not in json.dumps(sessions[tokenA]).lower() and 'tokenHash' not in json.dumps(sessions[tokenA])
        raw=json.dumps(setup()['venue']);assert tokenA not in raw and codeA not in raw
        peer=dashboard()['courts'][0]['device']['ipAddress'];assert peer in ['127.0.0.1','::1'];assert dashboard()['courts'][0]['device']['state']=='C'
        before=setup()['revision'];audit=int(db._sql('audit'));call('tablet/heartbeat',{},token=tokenA);call('tablet/heartbeat',{},token=tokenA);assert setup()['revision']==before and int(db._sql('audit'))==audit
        command(tokenA,'START');assert dashboard()['courts'][0]['status']=='P';assert setup()['revision']==before
        admin('pair',{'matchId':cm[0]['id'],'court':3,'umpireId':event+'-O001','presetId':presetId,'reviewAvailable':True},400)
        player=next(e for e in saved['entries'] if e['id']==a['entryA'])['participantIds'][0]
        def detail(action,**extra):
            return command(tokenA,action,body={'version':sessions[tokenA]['match']['version'],'operationId':str(uuid.uuid4()),'action':action,**extra})[0]
        assert detail('SHUTTLE')['match']['resultDetails']['shuttlesUsed']==1
        assert detail('CARD',participantId=player,cardType='Y')['match']['resultDetails']['cards'][0]['participantId']==player
        detail('CHALLENGE',side='A',outcome='U');detail('CHALLENGE',side='A',outcome='U')
        command(tokenA,'CHALLENGE',status=400,body={'version':sessions[tokenA]['match']['version'],'operationId':str(uuid.uuid4()),'action':'CHALLENGE','side':'A','outcome':'U'})
        command(tokenA,'CARD',status=400,body={'version':sessions[tokenA]['match']['version'],'operationId':str(uuid.uuid4()),'action':'CARD','participantId':'not-in-match','cardType':'Y'})
        current=next(m for p in view()['plans'] for m in p['matches'] if m['id']==a['id']);assert current['resultDetails']['shuttlesUsed']==1 and len(current['resultDetails']['challenges'])==2
        pairB=pair(b,2,event+'-O002');tokenB=tablet(pairB['code'],'Tablet B');command(tokenB,'START')
        assert sessions[tokenB]['match']['status']=='IP', 'Player readiness is the organiser responsibility'
        assert not any('Player' in q['waitingFor'] or 'minimum rest' in q['waitingFor'] for q in dashboard()['queue'])
        admin('cancel',{'court':2},400)
        admin('pair',{'matchId':cm[0]['id'],'court':1,'umpireId':event+'-O001'},400)
        fixtureBefore=db._sql('program-json')
        firstPoint,receipt=command(tokenA,'POINT','A');assert firstPoint['match']['scoreA']==1
        assert db._sql('program-json')==fixtureBefore, 'A rally must not rewrite the fixture document'
        duplicate,_=command(tokenA,'POINT',body=receipt);assert duplicate['match']['version']==firstPoint['match']['version'] and duplicate['match']['scoreA']==1
        command(tokenA,'POINT',body={**receipt,'side':'B'},status=409)
        time.sleep(2.2);assert dashboard()['courts'][0]['device']['state']=='D' and dashboard()['courts'][0]['status']=='P'
        preset=call('presets/')['presets'][0]
        edited={**preset,'rules':{**preset['rules'],'pointsToWin':7,'pointCap':9}}
        call('presets/',{'revision':view()['revision'],'preset':edited},method='PUT')
        staleVersion=sessions[tokenA]['match']['version']-1
        admin('pairing',{'matchId':a['id'],'version':staleVersion},409)
        renewal=admin('pairing',{'matchId':a['id'],'version':sessions[tokenA]['match']['version']})
        call('tablet/state',token=tokenA)
        replacement=pair(a,1,event+'-O001','Failed tablet')['code'];
        call('tablet/pair',{'code':renewal['code'],'deviceId':'obsolete-device','deviceName':'Obsolete'},401)
        call('tablet/heartbeat',{},token=tokenA);tokenA2=tablet(replacement,'Replacement A');call('tablet/heartbeat',{},401,tokenA);assert sessions[tokenA2]['match']['scoreA']==1 and sessions[tokenA2]['match']['resultDetails']['shuttlesUsed']==1 and sessions[tokenA2]['match']['rules']['pointsToWin']==5
        call('presets/',{'revision':view()['revision'],'preset':preset},method='PUT')
        # Restart preserves the score and session; connectivity requires a fresh heartbeat.
        stop();start();assert dashboard()['courts'][0]['device']['state']=='D';fresh=call('tablet/state',token=tokenA2);sessions[tokenA2]=fresh;assert fresh['match']['scoreA']==1;call('tablet/heartbeat',{},token=tokenA2);assert dashboard()['courts'][0]['device']['state']=='C'
        requests=[{'version':fresh['match']['version'],'operationId':str(uuid.uuid4()),'action':'POINT','side':side} for side in ['A','B']]
        def concurrent(body):
            req=urllib.request.Request(base+'tablet/command',data=json.dumps(body).encode(),headers={'Content-Type':'application/json','Authorization':'Bearer '+tokenA2})
            try:
                with urllib.request.urlopen(req,timeout=30) as r:return r.status
            except urllib.error.HTTPError as e:return e.code
        with ThreadPoolExecutor(max_workers=2) as pool:assert sorted(pool.map(concurrent,requests))==[200,409]
        sessions[tokenA2]=call('tablet/state',token=tokenA2);command(tokenA2,'UNDO');assert sessions[tokenA2]['match']['scoreA']==1 and sessions[tokenA2]['match']['resultDetails']['shuttlesUsed']==1 and sessions[tokenA2]['match']['rules']['pointsToWin']==5
        call('presets/',{'revision':view()['revision'],'preset':preset},method='PUT') and sessions[tokenA2]['match']['scoreB']==0
        while not sessions[tokenA2]['match']['matchWon']:command(tokenA2,'POINT','A')
        assert dashboard()['courts'][0]['status']=='S'
        command(tokenA2,'POINT','B',status=400);command(tokenA2,'SUBMIT');assert dashboard()['courts'][0]['status']=='F';assert not next(p for p in dashboard()['programs'] if p['id']==pids['A'])['ready']
        admin('pair',{'matchId':cm[0]['id'],'court':3,'umpireId':event+'-O001','presetId':presetId,'reviewAvailable':True},400)
        admin('confirm',{'matchId':a['id'],'version':sessions[tokenA2]['match']['version']});assert dashboard()['courts'][0]['status']=='A';assert next(p for p in dashboard()['programs'] if p['id']==pids['A'])['ready'];call('tablet/state',status=401,token=tokenA2)
        assert next(o for o in dashboard()['officials'] if o['id']==event+'-O001')['reservations']==[]
        command(tokenB,'START',status=400);assert dashboard()['courts'][1]['status']=='P'
        nextGen=call('tournament/preview',{'revision':view()['revision'],**gen,'programIds':[pids['A']],'earliestStart':datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None).isoformat()})
        command(tokenB,'POINT','A');call('tournament/confirm',{'revision':nextGen['revision'],'token':nextGen['token']})
        assert any(m['phase']=='K' for p in view()['plans'] if p['programId']==pids['A'] for m in p['matches']) and next(p for p in dashboard()['programs'] if p['id']==pids['B'])['remaining']==1
        # Paper mode revokes the device without freeing its court.
        admin('paper',{'matchId':b['id'],'version':sessions[tokenB]['match']['version'],'reason':'Network failure'});assert dashboard()['courts'][1]['status']=='P' and dashboard()['courts'][1]['device']['state']=='M';call('tablet/heartbeat',{},401,tokenB)
        paper=next(q for q in setup()['programPlans'] if q['programId']==b['programId'])['matches'][0]
        paperMatch=next(c['match'] for c in dashboard()['courts'] if c['number']==2)
        recovery={'matchId':b['id'],'version':paperMatch['version'],'games':[],'scoreA':2,'scoreB':1,'serving':{'serverSide':'A','serverId':next(p['id'] for p in paperMatch['players'] if p['side']=='A'),'receiverId':next(p['id'] for p in paperMatch['players'] if p['side']=='B'),'leftSide':'A'}}
        recoveryRevision=setup()['revision'];recoveryAudit=int(db._sql('audit'))
        admin('resume',{**recovery,'version':recovery['version']-1},409)
        admin('resume',{**recovery,'scoreA':5,'scoreB':0},400)
        admin('resume',{**recovery,'games':[{'a':5,'b':0}]},400)
        admin('resume',{**recovery,'serving':{**recovery['serving'],'receiverId':recovery['serving']['serverId']}},400)
        unchanged=next(c['match'] for c in dashboard()['courts'] if c['number']==2)
        assert unchanged['paperMode'] and unchanged['scoreA']==paperMatch['scoreA'] and unchanged['scoreB']==paperMatch['scoreB']
        assert setup()['revision']==recoveryRevision and int(db._sql('audit'))==recoveryAudit
        resumed=admin('resume',recovery);assert resumed['match']['scoreA']==2 and resumed['match']['scoreB']==1 and not resumed['match']['paperMode']
        assert resumed['match']['startedAt']==paperMatch['startedAt'] and resumed['match']['court']==2 and resumed['match']['umpireId']==paperMatch['umpireId']
        assert not resumed['match']['canUndo'] and not resumed['match']['rallyHistoryComplete'] and resumed['match']['resumptions'][0]['a']==2
        tokenB=tablet(resumed['code'],'Resumed B');resumeSnapshot=copy.deepcopy(sessions[tokenB]);stop();start();sessions[tokenB]=call('tablet/state',token=tokenB);assert sessions[tokenB]['match']==resumeSnapshot['match']
        command(tokenB,'START',status=400);command(tokenB,'UNDO',status=400)
        command(tokenB,'POINT','A');assert sessions[tokenB]['match']['scoreA']==3
        command(tokenB,'UNDO');assert sessions[tokenB]['match']['scoreA']==2
        admin('paper',{'matchId':b['id'],'version':sessions[tokenB]['match']['version'],'reason':'Continue on paper again'});call('tablet/heartbeat',{},401,tokenB)
        call('tournament/result',{'revision':view()['revision'],'matchId':b['id'],'matchVersion':next(m for p in view()['plans'] for m in p['matches'] if m['id']==b['id'])['matchVersion'],'games':[{'a':5,'b':0}],'finishedAt':datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None).isoformat()},method='PUT');assert dashboard()['courts'][1]['status']=='A'
        # Waiting reassignment retires the previous tablet and does not destroy the queued fixture.
        ccode=pair(cm[0],1,event+'-O001')['code'];ctoken=tablet(ccode,'Tablet C');admin('court',{'court':1,'closed':True},400)
        admin('pair',{'matchId':cm[1]['id'],'court':1,'umpireId':event+'-O001'},400)
        # Legacy override flags cannot displace a called match, even with a reason.
        admin('pair',{'matchId':cm[1]['id'],'court':1,'umpireId':event+'-O001','presetId':presetId,'reason':'Change queue order','replaceWaiting':True},400)
        call('tablet/heartbeat',{},token=ctoken)
        beforeMove=setup();original={m['id']:m for p in beforeMove['programPlans'] for m in p['matches']}
        moved=pair(cm[0],2,event+'-O001')
        call('tablet/heartbeat',{},401,ctoken)
        afterMove=setup();updated={m['id']:m for p in afterMove['programPlans'] for m in p['matches']}
        assert updated[cm[0]['id']]['court']==2 and updated[cm[0]['id']]['start']==original[cm[0]['id']]['start']
        assert all(m==updated[mid] for mid,m in original.items() if mid!=cm[0]['id']), 'Calling must not rearrange other matches'
        assert dashboard()['courts'][0]['status']=='A' and dashboard()['courts'][1]['status']=='W'
        # Competing/stale administrators cannot reserve the same court.
        call('dashboard/pair',{'revision':beforeMove['revision'],'matchId':cm[1]['id'],'court':2,'umpireId':event+'-O001','presetId':presetId},409)
        admin('pair',{'matchId':cm[1]['id'],'court':2,'umpireId':event+'-O001','presetId':presetId},400)
        newToken=tablet(moved['code'],'Moved tablet')
        scheduledBefore=next(q['match'] for q in dashboard()['queue'] if q['match']['id']==cm[0]['id'])
        admin('cancel',{'court':2});call('tablet/state',status=401,token=newToken)
        returned=next(q['match'] for q in dashboard()['queue'] if q['match']['id']==cm[0]['id'])
        assert returned['plannedStart']==scheduledBefore['plannedStart'] and returned['court']==scheduledBefore['court'] and returned['umpireId'] is None
        assert next(o for o in dashboard()['officials'] if o['id']==event+'-O001')['reservations']==[]
        call('tablet/pair',{'code':moved['code'],'deviceId':'cancelled-device','deviceName':'Cancelled'},401)
        admin('pairing',{'matchId':cm[0]['id'],'version':returned['version']},400)
        pair(cm[1],1,event+'-O001')
        assert any(q['match']['id']==cm[0]['id'] for q in dashboard()['queue'])
        admin('cancel',{'court':1});assert dashboard()['courts'][0]['status']=='A';admin('court',{'court':1,'closed':True});assert dashboard()['courts'][0]['status']=='X'
        admin('pair',{'matchId':cm[0]['id'],'court':1,'umpireId':event+'-O001'},400);admin('court',{'court':1,'closed':False})
        # Closed courts are excluded from fixture generation and reductions respect venue state.
        admin('court',{'court':4,'closed':True});call('setup/settings',{'revision':setup()['revision'],'timeZone':'Asia/Singapore','courtCount':3},400,method='PUT');admin('court',{'court':4,'closed':False})
        # The manual TRS-style result editor consumes tablet data without replacing its timestamps/details.
        reviewToken=tablet(pair(cm[0],1,event+'-O001')['code'],'Review tablet');command(reviewToken,'START')
        reviewPlayer=sessions[reviewToken]['match']['players'][0]['id']
        command(reviewToken,'CARD',body={'version':sessions[reviewToken]['match']['version'],'operationId':str(uuid.uuid4()),'action':'CARD','participantId':reviewPlayer,'cardType':'Y'})
        for _ in range(5):command(reviewToken,'POINT','A')
        command(reviewToken,'SUBMIT')
        record=next(m for p in view()['plans'] for m in p['matches'] if m['id']==cm[0]['id'])
        result={'matchId':record['id'],'matchVersion':record['matchVersion'],'games':record['games'],'details':record['resultDetails'],'startedAt':record['actualStartedAt'],'finishedAt':record['actualFinishedAt']}
        call('tournament/result',{'revision':view()['revision'],**result,'games':[{'a':5,'b':1}]},400,method='PUT')
        call('tournament/result',{'revision':view()['revision'],**result},method='PUT')
        confirmed=next(m for p in view()['plans'] for m in p['matches'] if m['id']==record['id'])
        assert confirmed['status']=='C' and confirmed['resultSource']=='T' and confirmed['resultDetails']==record['resultDetails'] and confirmed['actualStartedAt']==record['actualStartedAt'] and confirmed['actualFinishedAt']==record['actualFinishedAt']
        call('tablet/state',status=401,token=reviewToken)
        call('tournament/result',{'revision':view()['revision'],**result},409,method='PUT')
        call('tournament/result',{'revision':view()['revision'],**result,'matchVersion':confirmed['matchVersion'],'games':[{'a':5,'b':1}],'reason':'Correction after paper review'},method='PUT')
        corrected=next(m for p in view()['plans'] for m in p['matches'] if m['id']==record['id']);assert corrected['resultDetails']==record['resultDetails']
        before=dashboard();stop();start();assert dashboard()['courtCount']==4 and dashboard()['courts'][0]['status']=='A'
        db.command('--upgrade-live','true');assert int(db._sql('audit'))>10
        print(f'PASS: {checks} live HTTP assertions; pairing, actual IP, heartbeat loss/recovery, restart, replacement, command idempotency/concurrency, available-court moves and reservation protection, paper reconciliation and independent program advancement.')
    finally:
        stop();log.close()
        if __import__('sys').exc_info()[0] is not None:print((Path(temp)/'server.log').read_text()[-2000:])
finally:db.close()

