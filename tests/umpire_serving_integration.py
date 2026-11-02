"""Serving, end changes and recovery against disposable SQL; never TLS/TRS."""
import copy,json,os,socket,subprocess,tempfile,time,uuid,urllib.request,urllib.error
from datetime import datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll

base='http://localhost:5194/api/';checks=0;process=None
def call(path,body=None,status=200,token=None,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as error:code=error.code;raw=error.read()
 assert code==status,(path,code,status,raw[:1600]);checks+=1;return json.loads(raw)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5194))!=0
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-serving-') as temp:
  log=open(Path(temp)/'server.log','w',encoding='utf-8')
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5194'},stdout=log,stderr=log)
   for _ in range(150):
    try:return call('setup/')['data']
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Serving server not ready')
  def stop():
   global process
   if process:process.terminate();process.wait(timeout=10);process=None
  try:
   state=start();now=datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0);first=(now-timedelta(days=1)).date();last=(now+timedelta(days=1)).date()
   people=[{'id':f'P{i}','eventId':'E','name':f'Serving test {i}','dob':'1995-01-01','gender':'M','email':f'serving{i}@example.com','mobile':f'+658000000{i}'} for i in range(6)]
   programs=[{'id':id,'eventId':'E','name':name,'sport':'Badminton','entryType':kind,'minPlayers':n,'maxPlayers':n,'minAge':18,'maxAge':65,'gender':'O','minCapacity':2,'maxCapacity':8,'capacityUnit':'E','status':'A'} for id,name,kind,n in [('D','Doubles','D',2),('S','Singles','S',1)]]
   entries=[{'id':id,'eventId':'E','programId':pid,'participantIds':members,'status':'A'} for id,pid,members in [('DA','D',['P0','P1']),('DB','D',['P2','P3']),('SA','S',['P4']),('SB','S',['P5'])]]
   package={'schemaVersion':2,'source':'TRS','sourceSystemId':'serving-test','sourceTimeZone':'Asia/Singapore','exportedAt':datetime.now(timezone.utc).isoformat(),'registrationClosed':True,'hasFixtures':False,'events':[{'id':'E','name':'Serving lifecycle','venue':'Test hall','startDate':str(first),'endDate':str(last),'status':'D'}],'programs':programs,'participants':people,'entries':entries}
   preview=call('setup/preview?kind=trs&revision='+str(state['revision']),package);saved=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=saved['events'][0]['id'];pids={p['name']:p['id'] for p in saved['programs']}
   def view():return call('tournament/'+eid)
   def mutate(path,body,status=200,method='PUT'):return call('tournament/'+path,{'revision':view()['revision'],**body},status,method=method)
   plan={'eventId':eid,'courts':[1,2,3,4],'courtsConfigured':True,'dailySessions':[{'start':'00:00:00','end':'23:59:59'}],'officials':[{'id':'O1','name':'Official One'},{'id':'O2','name':'Official Two'}],'matchMinutes':1,'restMinutes':0}
   mutate('event',{'plan':plan})
   rules={'name':'Five point best of three','gamesToWin':2,'pointsToWin':5,'winBy':2,'pointCap':7,'intervalAt':3,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':True,'challengesPerGame':2,'countShuttles':True}
   preset=call('presets/',{'revision':view()['revision'],'preset':{'id':'','name':rules['name'],'rules':rules,'status':'A','isDefault':True}},method='PUT')['presets'][0]['id']
   for pid in pids.values():
    draft=mutate('preview',{'eventId':eid,'programIds':[pid],'courts':[1,2],'earliestStart':str(first)+'T00:00:00','settings':{'format':'KO','groupCount':1,'advancePerGroup':1},'seeds':{e['id']:None for e in saved['entries'] if e['programId']==pid}},method='POST');mutate('confirm',{'token':draft['token']},method='POST')
   matches={p['programId']:p['matches'][0] for p in view()['plans']};m=matches[pids['Doubles']]
   def pair(m,court,official,review=True,status=200,reason=''):
    return call('dashboard/pair',{'revision':view()['revision'],'matchId':m['id'],'court':court,'umpireId':next(o['id'] for o in view()['eventPlan']['officials'] if o['name']=={'O1':'Official One','O2':'Official Two'}[official]),'presetId':preset,'reviewAvailable':review,'reason':reason},status)
   pair(m,1,'O1',False,400);code=pair(m,1,'O1')['code']
   def tablet(code,label):return call('tablet/pair',{'code':code,'deviceId':label+'-device','deviceName':label})
   grant=tablet(code,'First tablet');token=grant['token'];current=grant['match']
   def command(action,side=None,status=200,**extra):
    global current
    result=call('tablet/command',{'version':current['match']['version'],'operationId':str(uuid.uuid4()),'action':action,**({'side':side} if side else {}),**extra},status,token)
    if status==200:current=result
    return result
   a=[p['id'] for p in current['match']['players'] if p['side']=='A'];b=[p['id'] for p in current['match']['players'] if p['side']=='B']
   setup={'serverSide':'A','serverId':a[0],'receiverId':b[0],'leftSide':'A'}
   command('START',status=400);command('START',status=400,serving={**setup,'receiverId':a[0]});command('START',serving=setup)
   assert current['match']['serverId']==a[0] and current['match']['receiverId']==b[0]
   command('POINT','A');assert current['match']['serverId']==a[0] and current['match']['receiverId']==b[1]
   command('POINT','B');assert current['match']['serverId']==b[1] and current['match']['receiverId']==a[0]
   command('POINT','B');assert current['match']['serverId']==b[1] and current['match']['receiverId']==a[1]
   command('UNDO');assert current['match']['serverId']==b[1] and len(current['match']['rallies'])==2
   command('POINT','A');assert current['match']['serverId']==a[1] and current['match']['receiverId']==b[0]
   command('SETUP',status=400,serving=setup)
   command('SETUP',serving=setup,remark='Correct umpire observation');command('UNDO');assert current['match']['serverId']==a[1]
   command('CARD',participantId=a[0],cardType='Y');command('SHUTTLE')
   for _ in range(2):command('CHALLENGE','A',outcome='U')
   command('CHALLENGE','A',status=400,outcome='U');command('CHALLENGE','A',outcome='S')
   snapshot=copy.deepcopy(current['match']);stop();start();current=call('tablet/state',token=token);assert current['match']==snapshot
   newCode=pair(m,1,'O1',reason='Replace device')['code'];call('tablet/state',token=token);replacement=tablet(newCode,'Replacement tablet');call('tablet/state',status=401,token=token);token=replacement['token'];current=replacement['match'];assert current['match']['serving']==snapshot['serving'] and current['match']['rallies']==snapshot['rallies']
   command('PAPER',remark='Temporary paper scoring');call('tablet/state',status=401,token=token)
   recovered=call('dashboard/resume',{'revision':view()['revision'],'matchId':m['id'],'version':current['match']['version'],'games':[],'scoreA':2,'scoreB':1,'serving':{'serverSide':'A','serverId':a[0],'receiverId':b[1],'leftSide':'A'}})
   recovery=tablet(recovered['code'],'Doubles recovery');token=recovery['token'];current=recovery['match']
   assert current['match']['serverId']==a[0] and current['match']['receiverId']==b[1] and current['match']['scoreA']==2
   assert current['match']['resultDetails']==snapshot['resultDetails'] and not current['match']['rallyHistoryComplete']
   while not current['match']['awaitingNextGame']:command('POINT','A')
   assert current['match']['games'][0]=={'a':5,'b':1}
   command('NEXT',status=400,serving={**setup,'leftSide':'A'});command('NEXT',status=400,serving={'serverSide':'B','serverId':b[0],'receiverId':a[0],'leftSide':'B'})
   command('NEXT',serving={'serverSide':'A','serverId':a[1],'receiverId':b[1],'leftSide':'B'});assert current['match']['serving']['leftSide']=='B'
   for _ in range(5):command('POINT','B')
   command('PAPER',remark='Reconnect between games')
   boundary={'revision':view()['revision'],'matchId':m['id'],'version':current['match']['version'],'games':current['match']['games'],'scoreA':0,'scoreB':0,'serving':{'serverSide':'B','serverId':b[1],'receiverId':a[0],'leftSide':'A'}}
   call('dashboard/resume',{**boundary,'serving':{'serverSide':'A','serverId':a[0],'receiverId':b[0],'leftSide':'A'}},400)
   call('dashboard/resume',{**boundary,'games':boundary['games'][:1]},400)
   resumed=call('dashboard/resume',boundary);recovery=tablet(resumed['code'],'Between games recovery');token=recovery['token'];current=recovery['match']
   assert len(current['match']['games'])==2 and current['match']['scoreA']==0 and current['match']['scoreB']==0
   for _ in range(2):command('POINT','B')
   command('POINT','B');assert current['match']['serving']['leftSide']=='B' and current['match']['serving']['decidingEndsChanged']
   command('UNDO');assert current['match']['serving']['leftSide']=='A' and not current['match']['serving']['decidingEndsChanged']
   for _ in range(3):command('POINT','B')
   command('SUBMIT');assert current['match']['status']=='RC' and not current['match']['rallyHistoryComplete'] and len(current['match']['resumptions'])==2
   call('dashboard/confirm',{'revision':view()['revision'],'matchId':m['id'],'version':current['match']['version']})
   # A tablet can explicitly hand a live singles match to paper without freeing its court.
   sm=matches[pids['Singles']];single=tablet(pair(sm,2,'O2')['code'],'Singles tablet');token=single['token'];current=single['match'];a=[p['id'] for p in current['match']['players'] if p['side']=='A'];b=[p['id'] for p in current['match']['players'] if p['side']=='B']
   command('START',serving={'serverSide':'B','serverId':b[0],'receiverId':a[0],'leftSide':'B'});command('POINT','A');assert current['match']['serverId']==a[0]
   # Reproduce an older saved live row with no serving/rally fields.
   script=r'''
$ErrorActionPreference='Stop'
if($env:TLS_TEST_NAME -notmatch '^TLS_Test_[a-f0-9]{32}$'){throw 'Unsafe test database'}
$cb=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($env:TLS_TEST_BASE);$cb['Initial Catalog']=$env:TLS_TEST_NAME
$cn=New-Object System.Data.SqlClient.SqlConnection($cb.ConnectionString);$cn.Open()
try{$cmd=$cn.CreateCommand();$cmd.CommandText="UPDATE MatchLiveRecords SET Json=JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(Json,'$.state.serving',NULL),'$.state.rallies',NULL),'$.state.rallyHistoryComplete',NULL) WHERE MatchId=@id";[void]$cmd.Parameters.AddWithValue('@id',$env:TLS_TEST_MATCH);[void]$cmd.ExecuteNonQuery()}finally{$cn.Dispose()}
'''
   legacy=subprocess.run(['powershell','-NoProfile','-Command',script],env={**db.control,'TLS_TEST_MATCH':sm['id']},capture_output=True,text=True);assert legacy.returncode==0,'Legacy test preparation failed (credentials withheld).'
   current=call('tablet/state',token=token);assert current['match']['serving'] is None and not current['match']['rallyHistoryComplete']
   command('POINT','A',status=400);command('SETUP',serving={'serverSide':'A','serverId':a[0],'receiverId':b[0],'leftSide':'B'})
   command('POINT','A');assert current['match']['scoreA']==2 and len(current['match']['rallies'])==1 and not current['match']['rallyHistoryComplete']
   command('PAPER',remark='Device issue');assert current['match']['paperMode'];call('tablet/state',status=401,token=token)
   assert next(c for c in call('dashboard/')['courts'] if c['number']==2)['status']=='P'
   mutate('result',{'matchId':sm['id'],'matchVersion':current['match']['version'],'games':[{'a':5,'b':0},{'a':5,'b':0}],'finishedAt':datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None).isoformat()})
   assert next(c for c in call('dashboard/')['courts'] if c['number']==2)['status']=='A'
   print(f'PASS: {checks} serving HTTP checks; singles/doubles, setup validation, rotation, undo, next-game choices, deciding ends, restart/replacement and tablet paper handover.')
  finally:stop();log.close()
finally:db.close()
