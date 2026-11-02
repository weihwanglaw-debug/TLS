"""Serving, end changes and recovery against disposable SQL; never TLS/TRS."""
import copy,json,os,socket,subprocess,tempfile,time,uuid,urllib.request,urllib.error
from datetime import datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll

socket.setdefaulttimeout(1)
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
   rules={'name':'21-point best of three','gamesToWin':2,'pointsToWin':21,'winBy':2,'pointCap':30,'intervalAt':11,'decidingEndsAt':11,'cardsEnabled':True,'challengesEnabled':True,'challengesPerGame':2,'countShuttles':True}
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
   command('START',serving=setup)
   command('CARD',participantId=a[0],cardType='B',remark='Referee annotation test')
   assert current['match']['status']=='IP'
   command('POINT','A');command('UNDO')
   print('CONFIRMED: Black card recorded; match remains IP and accepts subsequent points.')
   for i in range(20):command('POINT','A');command('POINT','B')
   assert not current['match']['awaitingNextGame']
   command('POINT','A');assert not current['match']['awaitingNextGame'];command('POINT','B')
   for i in range(8):command('POINT','A');command('POINT','B')
   assert current['match']['scoreA']==29 and current['match']['scoreB']==29
   command('POINT','A');assert current['match']['games']==[{'a':30,'b':29}]
   command('UNDO');assert current['match']['scoreA']==29 and not current['match']['games']
   command('POINT','A');command('NEXT',serving={**setup,'leftSide':'B'})
   for i in range(21):command('POINT','B')
   command('NEXT',serving={'serverSide':'B','serverId':b[0],'receiverId':a[0],'leftSide':'A'})
   for i in range(21):command('POINT','A')
   assert current['match']['matchWon']
   command('UNDO');assert not current['match']['matchWon']
   command('POINT','A');command('SUBMIT');assert current['match']['status']=='RC'
   call('dashboard/confirm',{'revision':view()['revision'],'matchId':m['id'],'version':current['match']['version']})
   print(f'PASS: {checks} API assertions; full 21-point deuce, 29-all/30-29, game/match-point undo, three games and confirmation.')
  finally:
   stop();log.close()
finally:db.close()
