"""Eight simulated tablets + dashboard + 0/2/4 displays; disposable SQL only."""
import argparse,json,socket,subprocess,threading,time,uuid,urllib.request,urllib.error,statistics
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime,timedelta,timezone
from sql_test_database import TestDatabase,project,dll
import test_inputs
parser=argparse.ArgumentParser();parser.add_argument('--seconds',type=int,default=20);parser.add_argument('--entries',type=int,choices=[4,32],default=32);args=parser.parse_args()
if not 5<=args.seconds<=25:parser.error('Use 5–25 seconds per phase so test games stay in progress.')
base='http://localhost:5222/api/';process=None

def call(path,body=None,token=None,method=None):
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:return json.load(r)
 except urllib.error.HTTPError as e:raise RuntimeError(path+': '+e.read().decode(errors='replace')) from None
def state():return call('setup/')['data']
def mutate(path,body,method=None):return call(path,{'revision':state()['revision'],**body},method=method)
socket.setdefaulttimeout(1)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5222))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(150):
  if process.poll() is not None:raise RuntimeError('Disposable test server exited before startup.')
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 offset=12-datetime.now(timezone.utc).hour;zone='UTC' if offset==0 else 'Etc/GMT'+('+' if offset<0 else '-')+str(abs(offset))
 localzone=timezone(timedelta(hours=offset));now=lambda:datetime.now(localzone).replace(tzinfo=None,microsecond=0)
 mutate('setup/settings',{'timeZone':zone,'courtCount':8},'PUT')
 package=test_inputs.package();package['events'][0].update(startDate=now().date().isoformat(),endDate=(now().date()+timedelta(days=1)).isoformat(),maxProgramsPerParticipant=None)
 person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':'P'+str(i),'name':'Load Player '+str(i),'email':str(i)+'@example.com'} for i in range(8*args.entries)]
 package['programs']=[{**program,'id':'PR'+str(i),'name':'Load Program '+str(i),'maxCapacity':128} for i in range(8)]
 package['entries']=[{'id':'E'+str(i),'eventId':'EV001','programId':'PR'+str(i//args.entries),'participantIds':['P'+str(i)],'status':'A'} for i in range(8*args.entries)]
 draft=call('setup/preview?kind=trs&revision='+str(state()['revision']),package);s=call('setup/confirm',{'revision':draft['revision'],'token':draft['token']})['data'];eid=s['events'][0]['id']
 rules={'name':'Load rehearsal 50-point','gamesToWin':3,'pointsToWin':50,'winBy':2,'pointCap':99,'intervalAt':25,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':25}
 preset=mutate('presets/',{'preset':{'id':'','name':'Load rehearsal','status':'A','isDefault':True,'rules':rules}},'PUT')['presets'][0]['id']
 plan=call('tournament/'+eid)['eventPlan'];plan.update(matchMinutes=5,restMinutes=0,officials=[{'id':'temporary-'+str(i),'name':'Load Umpire '+str(i)} for i in range(8)])
 mutate('tournament/event',{'plan':plan},'PUT');officials=call('tournament/'+eid)['eventPlan']['officials']
 for p in s['programs']:
  seeds=call('seeding/'+p['id']);mutate('seeding/'+p['id'],{'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seeds['entries']}},'PUT')
  preview=mutate('tournament/preview',{'eventId':eid,'programIds':[p['id']],'courts':[],'drawOnly':True,'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}})
  mutate('tournament/confirm',{'token':preview['token']})
 matches=[m for p in call('tournament/'+eid)['plans'] for m in p['matches']]
 start=now()+timedelta(minutes=30);end=start+timedelta(hours=2)
 window={'date':start.date().isoformat(),'start':start.time().isoformat(),'end':end.time().isoformat(),'courts':list(range(1,9)),'matchIds':[m['id'] for m in matches]}
 preview=mutate('scheduling/preview',{'eventId':eid,'sessions':[window],'matchMinutes':5,'restMinutes':0,'days':[{'date':window['date'],'courts':window['courts'],'matchIds':window['matchIds']}]});mutate('scheduling/confirm',{'token':preview['token']})
 matches=[m for p in call('tournament/'+eid)['plans'] for m in p['matches']];tablets=[]
 for court in range(1,9):
  m=next(m for m in matches if m['court']==court)
  invite=mutate('dashboard/pair',{'matchId':m['id'],'court':court,'umpireId':officials[court-1]['id'],'presetId':preset,'reason':''})
  paired=call('tablet/pair',{'code':invite['code'],'deviceId':'load-tablet-'+str(court),'deviceName':'Load tablet '+str(court)});token=paired['token'];m=paired['match']['match']
  serving={'serverSide':'A','serverId':next(p['id'] for p in m['players'] if p['side']=='A'),'receiverId':next(p['id'] for p in m['players'] if p['side']=='B'),'leftSide':'A'}
  live=call('tablet/command',{'version':m['version'],'operationId':str(uuid.uuid4()),'action':'START','serving':serving,'confirmEarlyStart':True},token)['match'];tablets.append({'token':token,'version':live['version'],'points':0})
 print(f'Fixture: 8 courts, 8 programs, {8*args.entries} players, {len(matches)} scheduled matches; 8 active tablets. HTTP clients on localhost; synthetic scoring, no real Wi-Fi.',flush=True)
 for displays in [0,2,4]:
  records={};errors=[];lock=threading.Lock();stop=threading.Event();deadline=time.perf_counter()+args.seconds
  def measured(kind,path,body=None,token=None):
   before=time.perf_counter()
   try:
    result=call(path,body,token)
    with lock:records.setdefault(kind,[]).append((time.perf_counter()-before)*1000)
    return result
   except Exception as e:
    with lock:errors.append(kind+': '+str(e))
    raise
  def tablet(index,t):
   stop.wait(index*.09);next_point=next_poll=next_heartbeat=time.perf_counter()
   while time.perf_counter()<deadline and not stop.is_set():
    clock=time.perf_counter()
    if clock>=next_point:
     result=measured('POINT','tablet/command',{'version':t['version'],'operationId':str(uuid.uuid4()),'action':'POINT','side':'A' if t['points']%2==0 else 'B'},t['token']);t['version']=result['match']['version'];t['points']+=1;next_point=clock+1
    if clock>=next_poll:measured('tablet state','tablet/state',token=t['token']);next_poll=clock+2
    if clock>=next_heartbeat:measured('heartbeat','tablet/heartbeat',{},t['token']);next_heartbeat=clock+5
    stop.wait(.02)
  def reader(kind,path):
   while time.perf_counter()<deadline and not stop.is_set():
    measured(kind,path);stop.wait(2)
  started=time.perf_counter()
  with ThreadPoolExecutor(max_workers=13) as pool:
   futures=[pool.submit(tablet,i,t) for i,t in enumerate(tablets)]+[pool.submit(reader,'dashboard','dashboard/?eventId='+eid)]+[pool.submit(reader,'display','display') for _ in range(displays)]
   for future in futures:
    try:future.result()
    except Exception:stop.set()
  stats={}
  for kind,values in records.items():
   values.sort();stats[kind]={'count':len(values),'medianMs':round(statistics.median(values),1),'p95Ms':round(values[min(len(values)-1,(95*len(values)+99)//100-1)],1),'maxMs':round(max(values),1)}
  print(json.dumps({'displayClients':displays,'seconds':round(time.perf_counter()-started,1),'errors':errors,'metrics':stats}),flush=True)
  assert not errors,errors
 for t in tablets:
  m=call('tablet/state',token=t['token'])['match'];assert m['scoreA']+m['scoreB']==t['points'] and len(m['rallies'])==t['points']
 print('PASS: no lost/duplicate points; all confirmed totals match the submitted commands. Disposable database removed on exit.',flush=True)
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
