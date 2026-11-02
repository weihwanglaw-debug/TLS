"""Disposable SQL tournament, exceptional-result recovery and measured four-court load."""
import copy,json,socket,subprocess,time,uuid,urllib.request,urllib.error
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime,timedelta,timezone
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5216/api/';checks=0;process=None;latencies=[]
socket.setdefaulttimeout(1)
def call(path,body=None,status=200,token=None,method=None,measure=False):
 global checks
 request=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 started=time.perf_counter()
 try:
  with urllib.request.urlopen(request,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 if measure:latencies.append((time.perf_counter()-started)*1000)
 assert code==status,(path,code,status,raw[:1500]);checks+=1;return json.loads(raw)
def check(value):
 global checks
 assert value;checks+=1
def state():return call('setup/')['data']
def rev():return state()['revision']
def view():return call('tournament/'+eid)
def matches():return [m for p in view()['plans'] for m in p['matches']]
def latest(m):return next(x for x in matches() if x['id']==m['id'])
def now():return datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0)
def draw(pid):
 p=call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,**({'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}} if not any(m['programId']==pid for m in matches()) else {})})
 return call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
def official(m,kind,winner,games,handling='U',expect=200):
 m=latest(m);body={'revision':rev(),'matchId':m['id'],'matchVersion':m['matchVersion'],'games':games,'outcome':kind,'officialWinner':winner,'reason':'Practical test referee correction'}
 impact=call('tournament/result-impact',body)
 result=call('tournament/result',{**body,'impactToken':impact['token'],'downstreamHandling':handling},expect,method='PUT')
 return impact,result
def pairing(m,court,official_id,exception=None):
 body={'revision':rev(),'matchId':m['id'],'court':court,'umpireId':official_id,'presetId':preset,'courtOverrideReason':exception}
 return call('dashboard/pair',body)
def join(m,court,official_id,device):
 invitation=pairing(m,court,official_id)
 return call('tablet/pair',{'code':invitation['code'],'deviceId':device,'deviceName':device})['token']
def command(token,action,side=None,status=200,**extra):
 m=call('tablet/state',token=token)['match'];body={'version':m['version'],'operationId':str(uuid.uuid4()),'action':action,**extra}
 if side:body['side']=side
 return call('tablet/command',body,status,token)
def serving(m,left='A'):
 return {'serverSide':'A','serverId':next(p['id'] for p in m['players'] if p['side']=='A'),'receiverId':next(p['id'] for p in m['players'] if p['side']=='B'),'leftSide':left}
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5216))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 package=test_inputs.package();package['events'][0].update(startDate=now().date().isoformat(),endDate=(now().date()+timedelta(days=1)).isoformat(),maxProgramsPerParticipant=None)
 person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':'P'+str(i),'name':'Court Test Player '+str(i),'email':str(i)+'@example.com'} for i in range(16)]
 package['programs']=[{**program,'id':'PR'+str(i),'name':'Court Program '+str(i)} for i in range(4)]
 package['entries']=[{'id':'E'+str(i),'eventId':'EV001','programId':'PR'+str(i//4),'participantIds':['P'+str(i)],'status':'A'} for i in range(16)]
 imported=call('setup/preview?kind=trs&revision='+str(rev()),package);s=call('setup/confirm',{'revision':imported['revision'],'token':imported['token']})['data'];eid=s['events'][0]['id']
 rules={'name':'21-point test','gamesToWin':2,'pointsToWin':21,'winBy':2,'pointCap':30,'intervalAt':11,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':11}
 preset=call('presets/',{'revision':rev(),'preset':{'id':'','name':'21-point test','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
 plan=view()['eventPlan'];plan.update(matchMinutes=5,restMinutes=0,officials=[{'id':'temporary-'+str(i),'name':'Umpire '+str(i)} for i in range(4)])
 call('tournament/event',{'revision':rev(),'plan':plan},method='PUT');officials=view()['eventPlan']['officials']
 for p in s['programs']:
  seed=call('seeding/'+p['id']);call('seeding/'+p['id'],{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT');draw(p['id'])
 start=now()+timedelta(seconds=15);end=start+timedelta(minutes=60)
 window={'date':start.date().isoformat(),'start':start.time().isoformat(),'end':end.time().isoformat(),'courts':[1,2,3,4],'matchIds':[m['id'] for m in matches()],'programLimits':{p['id']:2 for p in s['programs']}}
 call('scheduling/draft',{'revision':rev(),'eventId':eid,'sessions':[window],'sessionIndex':0,'dirtySessions':[],'moveMatchIds':[]})
 check(view()['eventPlan']['planningDraft']['sessions']==[window]);check(all(m['start'] is None for m in matches()))
 p=call('scheduling/preview',{'revision':rev(),'eventId':eid,'sessions':[window],'matchMinutes':5,'restMinutes':0,'days':[{'date':window['date'],'courts':window['courts'],'matchIds':window['matchIds']}]})
 check(len(p['matches'])==8);call('scheduling/confirm',{'revision':p['revision'],'token':p['token']});check(view()['eventPlan']['planningDraft'] is None)
 time.sleep(max(0,(start-now()).total_seconds())+.2)
 active=[next(m for m in sorted(matches(),key=lambda m:m['start']) if m['court']==c) for c in range(1,5)]
 tokens=[join(m,c,officials[c-1]['id'],'load-tablet-'+str(c)) for c,m in enumerate(active,1)]
 for token in tokens:command(token,'START',serving=serving(call('tablet/state',token=token)['match']))
 def score(token):
  m=call('tablet/state',token=token)['match']
  for game in range(2):
   if game:m=call('tablet/command',{'version':m['version'],'operationId':str(uuid.uuid4()),'action':'NEXT','serving':serving(m,'B')},token=token,measure=True)['match']
   for _ in range(21):m=call('tablet/command',{'version':m['version'],'operationId':str(uuid.uuid4()),'action':'POINT','side':'A'},token=token,measure=True)['match']
  call('tablet/command',{'version':m['version'],'operationId':str(uuid.uuid4()),'action':'SUBMIT'},token=token,measure=True)
 def poll(token):
  for _ in range(20):call('tablet/state',token=token,measure=True);time.sleep(.03)
 def dashboard():
  for _ in range(20):call('dashboard/?eventId='+eid,measure=True);time.sleep(.03)
 begin=time.perf_counter()
 with ThreadPoolExecutor(max_workers=9) as pool:
  futures=[pool.submit(score,t) for t in tokens]+[pool.submit(poll,t) for t in tokens]+[pool.submit(dashboard)]
  for future in futures:future.result()
 elapsed=time.perf_counter()-begin
 for m in active:
  m=latest(m);check(m['status']=='RC');call('dashboard/confirm',{'revision':rev(),'matchId':m['id'],'version':m['matchVersion']})
 remaining=[m for m in matches() if m['status']=='SC']
 retirement=remaining[0];official(retirement,'R',retirement['entryA'],[{'a':21,'b':15},{'a':12,'b':8}]);check(latest(retirement)['games']==[{'a':21,'b':15},{'a':12,'b':8}])
 for m in remaining[1:]:official(m,'N',m['entryA'],[{'a':21,'b':0},{'a':21,'b':0}])
 parent=active[0];draw(parent['programId']);final=next(m for m in matches() if m['programId']==parent['programId'] and m['round']==2)
 # Safe correction updates the final before any meaningful progression.
 impact,_=official(parent,'M',parent['entryB'],[{'a':21,'b':0},{'a':21,'b':0}]);check(not impact['dangerous'] and len(impact['matches'])==1);check(latest(final)['entryA']==parent['entryB'] or latest(final)['entryB']==parent['entryB'])
 # Schedule and start that final, then verify explicit late-correction handling.
 plan=view()['eventPlan'];plan['sessions'][0]['matchIds'].append(final['id']);call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')
 call('tournament/schedule',{'revision':rev(),'matchId':final['id'],'court':1,'start':(start+timedelta(minutes=30)).isoformat(),'umpireId':officials[0]['id']},method='PUT')
 final=latest(final);token=join(final,1,officials[0]['id'],'late-correction-tablet');command(token,'START',serving=serving(call('tablet/state',token=token)['match']))
 impact,_=official(parent,'M',parent['entryA'],[],expect=400);check(impact['dangerous']);check(latest(final)['status']=='IP')
 official(parent,'M',parent['entryA'],[],handling='K');check(latest(final)['status']=='IP');call('tablet/state',token=token)
 official(parent,'M',parent['entryB'],[],handling='R');check(latest(final)['status']=='SC');call('tablet/state',status=401,token=token)
 check(len(next(p for p in view()['plans'] if p['programId']==parent['programId'])['officialDecisions'])>=3)
 # A stale impact review cannot override a newer official decision.
 m=latest(parent);body={'revision':rev(),'matchId':m['id'],'matchVersion':m['matchVersion'],'outcome':'D','officialWinner':m['entryA'],'games':[],'reason':'DQ test'}
 impact=call('tournament/result-impact',body);official(parent,'M',parent['entryA'],[])
 call('tournament/result',{**body,'impactToken':impact['token'],'downstreamHandling':'U'},409,method='PUT')
 # Separate disposable event: TLS intentionally disallows overlapping event dates.
 process.terminate();process.wait(timeout=10);db.close();db=TestDatabase()
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 preset=call('presets/',{'revision':rev(),'preset':{'id':'','name':'21-point test','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
 # Court 1 now, Court 2 tomorrow: normal start on Court 2 fails.
 package=test_inputs.package();package['sourceSystemId']='court-availability-test';package['events'][0]['name']='Court availability event';package['events'][0].update(startDate=now().date().isoformat(),endDate=(now().date()+timedelta(days=1)).isoformat());package['programs']=package['programs'][:1];package['entries']=package['entries'][:2]
 imported=call('setup/preview?kind=trs&revision='+str(rev()),package);new=call('setup/confirm',{'revision':imported['revision'],'token':imported['token']})['data'];eid=new['events'][0]['id'];pid=next(p['id'] for p in new['programs'] if p['eventId']==eid)
 plan=view()['eventPlan'];plan.update(matchMinutes=5,restMinutes=0,sessions=[{'date':now().date().isoformat(),'start':'00:00:00','end':'23:59:59','courts':[1],'matchIds':[]},{'date':(now().date()+timedelta(days=1)).isoformat(),'start':'00:00:00','end':'23:59:59','courts':[2],'matchIds':[]}],officials=[{'id':'','name':'Court Official'}])
 call('tournament/event',{'revision':rev(),'plan':plan},method='PUT');official_id=view()['eventPlan']['officials'][0]['id'];seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT');draw(pid);m=matches()[0]
 plan=view()['eventPlan'];
 for w in plan['sessions']:w['matchIds']=[m['id']]
 call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')
 call('tournament/schedule',{'revision':rev(),'matchId':m['id'],'court':2,'start':plan['sessions'][1]['date']+'T10:00:00','umpireId':official_id},method='PUT')
 token=join(latest(m),2,official_id,'court-exception-tablet');setup=serving(call('tablet/state',token=token)['match']);command(token,'START',status=400,serving=setup)
 pairing(latest(m),2,official_id,'Referee has made Court 2 available');command(token,'START',serving=setup)
 official(m,'D',m['entryB'],[{'a':3,'b':5}]);check(latest(m)['resultKind']=='D');call('tablet/state',status=401,token=token)
 ordered=sorted(latencies);print(f'PASS: {checks} practical recovery/API checks. Four concurrent courts: {len(ordered)} measured requests in {elapsed:.2f}s; median {ordered[len(ordered)//2]:.1f} ms, p95 {ordered[int(len(ordered)*.95)]:.1f} ms, max {max(ordered):.1f} ms.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
