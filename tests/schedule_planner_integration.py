"""Whole-tournament planner against disposable SQL. Never writes operational TLS/TRS data."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta,datetime
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5271/api/';checks=0;process=None
def check(ok,label):
 global checks
 assert ok,label
 checks+=1
def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=60) as response:actual=response.status;raw=response.read()
 except urllib.error.HTTPError as ex:actual=ex.code;raw=ex.read()
 assert actual==status,(path,actual,status,raw[:1200]);checks+=1
 return json.loads(raw)
def revision():return call('setup/')['data']['revision']
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5271))!=0
db=TestDatabase()
try:
 def start():
  global process
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(150):
   try:return call('setup/')['data']
   except urllib.error.URLError:time.sleep(.1)
  raise RuntimeError('Planner test API did not start')
 start();package=test_inputs.package();first=(date.today()+timedelta(days=10)).isoformat();last=(date.today()+timedelta(days=11)).isoformat();package['events'][0].update(startDate=first,endDate=last,maxProgramsPerParticipant=None)
 person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':f'P{i}','name':f'Planner Player {i}','email':f'planner{i}@example.com'} for i in range(8)]
 package['programs']=[{**program,'id':f'PR{i}','name':f'Program {i}'} for i in range(3)]
 package['entries']=[{'id':f'E{p}-{i}','eventId':'EV001','programId':f'PR{p}','participantIds':[f'P{i}'],'name':'','seed':i+1,'status':'A'} for p in range(3) for i in range(5 if p==2 else 8)]
 preview=call('setup/preview?kind=trs&revision='+str(revision()),package);s=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=s['events'][0]['id'];pids=[p['id'] for p in s['programs']]
 rules={'name':'Planner rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
 call('presets/',{'revision':revision(),'preset':{'id':'','name':'Planner rules','status':'A','isDefault':True,'rules':rules}},method='PUT')
 def tournament():return call('tournament/'+eid)
 def planner():return call('scheduling/planner/'+eid)
 def generate(pid,settings=None):
  body={'revision':revision(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True}
  if settings:body['settings']=settings
  p=call('tournament/preview',body);return call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
 for i,pid in enumerate(pids):
  seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':revision(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
  generate(pid,{'format':'GK' if i==0 else 'KO','groupCount':2 if i==0 else 1,'advancePerGroup':2 if i==0 else 1})
 v=planner();check(len(v['nodes'])==26,'15 group/KO +7 KO +4 five-entry KO, excluding byes')
 check(sum(n['phase']=='G' for n in v['nodes'])==12,'two groups of4 have12 matches')
 check(any('Group' in n['labelA'] and 'winner' in n['labelA'] for n in v['nodes']),'qualifier labels')
 draft=v['draft'];draft['sessions']=[{'date':first,'start':'09:00:00','end':'20:00:00','courts':[1,2,3,4]},{'date':last,'start':'09:00:00','end':'20:00:00','courts':[1,2,3,4]}];draft['minutes']={n['roundKey']:60 if n['final'] else 40 if n['phase']=='K' and n['round']==2 else 30 for n in v['nodes']};draft['restWarningMinutes']=30
 def request(draft,action='check',**extra):return {'revision':revision(),'eventId':eid,'draft':draft,'action':action,**extra}
 def preview(draft,action='check',status=200,**extra):return call('scheduling/planner/preview',request(draft,action,**extra),status)
 def confirm(p,status=200,accept=True):return call('scheduling/planner/confirm',{'revision':p['revision'],'token':p['token'],'acceptWarnings':accept},status)
 saved=call('scheduling/planner/draft',request(draft));check(saved['hasDraft'],'draft saved')
 check(all(m['start'] is None for p in tournament()['plans'] for m in p['matches']),'draft does not book actual matches')
 reloaded=planner()['draft'];check(reloaded['minutes']==draft['minutes'] and reloaded['restWarningMinutes']==30 and reloaded['sessions'][0]['date']==first,'round durations survive JSON reload')
 oldRevision=revision();call('scheduling/planner/draft',request(reloaded));check(revision()==oldRevision,'unchanged draft save adds no revision or audit noise')
 bad=copy.deepcopy(draft);bad['sessions'][1]['date']=first;bad['sessions'][1]['start']='10:00:00';preview(bad,status=400)
 before=tournament();p=preview(draft,'auto');check(tournament()==before,'preview does not persist matches');check(len(p['draft']['placements'])==26,'full allocation');check(not any(f['kind']=='E' for f in p['findings']),'valid plan')
 check(all(datetime.fromisoformat(b['start']).minute%15==0 for b in p['draft']['placements']),'15-minute starts')
 check(all(b['minutes']==draft['minutes'][next(n['roundKey'] for n in v['nodes'] if n['id']==b['slotId'])] for b in p['draft']['placements']),'exact individual duration')
 check(not any(f['kind']=='W' for f in p['findings']),'generous room honours rest')
 confirmed=confirm(p);check(len(confirmed['booked'])==26,'current+future bookings persist');bookings={b['slotId']:b for b in confirmed['booked']}
 snapshot=planner();process.terminate();process.wait(timeout=10);process=None;start();check(planner()==snapshot,'restart persistence')
 bad=copy.deepcopy(confirmed['draft']);key=v['nodes'][0]['roundKey'];bad['minutes'][key]+=5;preview(bad,status=400)
 # Completing group results then generating KO binds existing future slots once.
 preset=call('presets/')['presets'][0]['id']
 rows=next(p for p in tournament()['plans'] if p['programId']==pids[0])['matches']
 for m in rows:call('tournament/result',{'revision':revision(),'matchId':m['id'],'presetId':preset,'games':[{'a':5,'b':0}],'startedAt':m['start'],'finishedAt':m['end']},method='PUT')
 generate(pids[0]);now=planner();newMatches=[n for n in now['nodes'] if n['programId']==pids[0] and n['phase']=='K' and n['round']==1]
 check(len(newMatches)==2 and all(n['matchId'] for n in newMatches),'qualifiers materialized')
 actual={m['id']:m for p in tournament()['plans'] for m in p['matches']}
 for n in newMatches:
  b=bookings[n['id']];m=actual[n['matchId']];check(m['start']==b['start'] and m['court']==b['court'] and m['durationMinutes']==b['minutes'],'future booking binds exact match')
 # Re-read or unrelated settings must not allocate a second slot or invalidate others.
 check(len(planner()['booked'])==26,'no duplicate materialization')
 current=planner();bad=copy.deepcopy(current['draft']);b=bad['placements'][0];b['start']=b['start'][:14]+'05:00';preview(bad,status=400)
 # Stale tokens reject with no rewrite.
 p=preview(current['draft']);edited=copy.deepcopy(current['draft']);edited['restWarningMinutes']=31;call('scheduling/planner/draft',request(edited));confirm(p,status=409)
 # Clearing a result keeps its replay slot but removes materialized and future-only dependants.
 source=next(m for p in tournament()['plans'] if p['programId']==pids[0] for m in p['matches'] if m['phase']=='G' and m['status']=='C')
 call('tournament/clear-result',{'revision':revision(),'matchId':source['id'],'matchVersion':source['matchVersion'],'reason':'Planner correction check'})
 cleared=planner();check(not any(b['slotId'].startswith(pids[0]+'/K/') for b in cleared['booked']),'result clear removes future-only dependent bookings')
 replay=next(m for p in tournament()['plans'] for m in p['matches'] if m['id']==source['id']);check(replay['start']==source['start'] and replay['status']=='SC','result clear retains replay schedule')
 # Reset group stage clears dependent future bookings and results but retains availability and estimates.
 gkey=next(n['roundKey'] for n in current['nodes'] if n['programId']==pids[0] and n['phase']=='G')
 reset=call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'roundKeys':[gkey]});check(not any(b['slotId'].startswith(pids[0]+'/') for b in reset['booked']),'dependent bookings cleared');check(len(reset['draft']['sessions'])==2,'sessions retained')
 reset['draft']['minutes'][gkey]=25;call('scheduling/planner/draft',request(reset['draft']));check(planner()['draft']['minutes'][gkey]==25,'reset unlocks estimates without historical blocker')
 print(f'PASS: {checks} planner HTTP/data checks; disposable SQL cleaned in finally.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
