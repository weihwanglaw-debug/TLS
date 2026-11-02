"""Session-specific programs/courts against disposable SQL; local event records are untouched."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta,datetime
from sql_test_database import TestDatabase,project,dll
import test_inputs

base='http://localhost:5207/api/';checks=0;process=None
def check(ok):
 global checks
 assert ok;checks+=1
def call(path,body=None,expect=200,method=None):
 global checks
 request=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json'},method=method or ('POST' if body is not None else 'GET'))
 try:
  with urllib.request.urlopen(request,timeout=30) as response:status=response.status;raw=response.read()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read()
 assert status==expect,(path,status,expect,raw[:1000]);checks+=1;return json.loads(raw)
def state():return call('setup/')['data']
def revision():return state()['revision']
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5207))!=0
db=TestDatabase()
try:
 def start():
  global process
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(150):
   try:return state()
   except urllib.error.URLError:time.sleep(.1)
  raise RuntimeError('Session test API did not start')
 start();package=test_inputs.package();first=(date.today()+timedelta(days=10)).isoformat();second=(date.today()+timedelta(days=11)).isoformat()
 package['events'][0].update(startDate=first,endDate=second,maxProgramsPerParticipant=None)
 person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':f'P{i}','name':f'Session Player {i}','email':f'p{i}@example.com'} for i in range(12)]
 package['programs']=[{**program,'id':f'PR{i}','name':f'Program {chr(65+i)}'} for i in range(3)]
 package['entries']=[{'id':f'E{p}-{i}','eventId':'EV001','programId':f'PR{p}','participantIds':[f'P{i+(8 if p==1 else 0)}'],'name':'','seed':None,'status':'A'} for p in range(3) for i in range(8 if p==0 else 4)]
 preview=call('setup/preview?kind=trs&revision='+str(revision()),package)
 s=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=s['events'][0]['id'];pids=[p['id'] for p in s['programs']]
 original={k:s[k] for k in ['participants','entries','programs','events']}
 rules={'name':'Session rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
 call('presets/',{'revision':revision(),'preset':{'id':'','name':'Session rules','status':'A','isDefault':True,'rules':rules}},method='PUT')
 def view():return call('tournament/'+eid)
 generated=[]
 for pid in pids:
  seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':revision(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
  p=call('tournament/preview',{'revision':revision(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}})
  call('tournament/confirm',{'revision':p['revision'],'token':p['token']});generated.append(next(p for p in view()['plans'] if p['programId']==pid)['matches'])
 a,b,c=[[m['id'] for m in rows] for rows in generated]
 body={'eventId':eid,'matchMinutes':40,'restMinutes':120,'sessions':[{'date':first,'start':'10:00:00','end':'12:00:00','courts':[1],'matchIds':a},{'date':first,'start':'14:00:00','end':'16:00:00','courts':[2],'matchIds':a+b}], 'days':[{'date':first,'courts':[1,2],'matchIds':a+b}]}
 def preview(value,expect=200):return call('scheduling/preview',{'revision':revision(),**value},expect)
 def save(p,expect=200):return call('scheduling/confirm',{'revision':p['revision'],'token':p['token']},expect)
 before=view();p=preview(body)
 check(len(p['matches'])==6 and not p['unplaced']);morning=[m for m in p['matches'] if m['start'][11:16]<'12:00'];afternoon=[m for m in p['matches'] if m['start'][11:16]>='14:00']
 check(len(morning)==3 and all(m['programId']==pids[0] and m['court']==1 for m in morning))
 check(len(afternoon)==3 and all(m['court']==2 for m in afternoon))
 check(sum(m['programId']==pids[0] for m in afternoon)==1 and sum(m['programId']==pids[1] for m in afternoon)==2)
 check(view()==before) # Preview writes no SQL, including planning settings.
 for mutate in [
  lambda x:x['sessions'][0].update(courts=[]),
  lambda x:x['sessions'][0].update(courts=[1,1]),
  lambda x:x['sessions'][0].update(courts=[99]),
  lambda x:x['sessions'][0].update(matchIds=a+['foreign']),
  lambda x:x['sessions'][0].update(matchIds=a+[a[0]]),
  lambda x:x['sessions'][0].update(matchIds=None),
  lambda x:x['sessions'][0].update(start='15:00:00',end='17:00:00'),
  lambda x:x['sessions'][0].update(date=second),
  lambda x:x['sessions'][1].update(matchIds=a),
 ]:
  invalid=copy.deepcopy(body);mutate(invalid);preview(invalid,400)
 check(view()==before)
 # A restricted afternoon cannot spill B into the free morning or another day.
 small=copy.deepcopy(body);small['sessions'][1]['end']='14:40:00';p=preview(small)
 check(len(p['matches'])==4 and len(p['unplaced'])==2);check(all(m['programId']==pids[1] for m in p['unplaced']))
 # Shared people in C must rest after their morning A matches, even on another court.
 shared=copy.deepcopy(body);shared['sessions'][1].update(start='12:00:00',end='16:00:00',matchIds=a+b+c);shared['days'][0]['matchIds']=a+b+c
 p=preview(shared);people={e['id']:set(e['participantIds']) for e in s['entries']}
 for match in p['matches']:
  if match['programId']!=pids[2]:continue
  for other in p['matches']:
   if other['programId']!=pids[0]:continue
   if (people[match['entryA']]|people[match['entryB']])&(people[other['entryA']]|people[other['entryB']]):
    check(datetime.fromisoformat(match['start'])>=datetime.fromisoformat(other['end'])+timedelta(minutes=120))
 # Closed courts and stale previews fail safely.
 call('dashboard/court',{'revision':revision(),'court':2,'closed':True});preview(body,400);call('dashboard/court',{'revision':revision(),'court':2,'closed':False})
 p=preview(body);plan=view()['eventPlan'];call('tournament/event',{'revision':revision(),'plan':plan},method='PUT');save(p,409)
 p=preview(body);save(p);save(p,400);stored=view();check([{k:v for k,v in w.items() if k!='programLimits'} for w in stored['eventPlan']['sessions']]==body['sessions'])
 savedSlots={m['id']:(m['start'],m['end'],m['court']) for plan in stored['plans'] for m in plan['matches']}
 # Reopening does not reslot existing matches. Excluding a saved program/court/time is rejected.
 p=preview(body);check(p['kept']==6 and not p['matches']);save(p)
 for mutate in [
  lambda x:x['sessions'][0].update(courts=[2]),
  lambda x:x['sessions'][0].update(end='10:20:00'),
  lambda x:x['sessions'][0].update(matchIds=[]),
 ]:
  invalid=copy.deepcopy(body);mutate(invalid);invalid['days'][0]['courts']=list(set(c for w in invalid['sessions'] for c in w['courts']));preview(invalid,400)
 # Manual overrides must also obey the selected session programs and courts.
 m=next(m for plan in stored['plans'] for m in plan['matches'] if m['programId']==pids[1])
 call('tournament/schedule',{'revision':revision(),'matchId':m['id'],'court':1,'start':first+'T10:00:00'},400,method='PUT')
 check({m['id']:(m['start'],m['end'],m['court']) for plan in view()['plans'] for m in plan['matches']}==savedSlots)
 # New independently generated round can be selected in a session after its own results.
 preset=call('presets/')['presets'][0]['id']
 for m in next(p for p in stored['plans'] if p['programId']==pids[0])['matches']:
  call('tournament/result',{'revision':revision(),'matchId':m['id'],'presetId':preset,'games':[{'a':5,'b':0}],'startedAt':m['start'],'finishedAt':m['end']},method='PUT')
 p=call('tournament/preview',{'revision':revision(),'eventId':eid,'programIds':[pids[0]],'courts':[],'drawOnly':True});call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
 nextIds=[m['id'] for p in view()['plans'] for m in p['matches'] if m['programId']==pids[0] and m['round']==2]
 later=copy.deepcopy(body);later['sessions'].append({'date':second,'start':'10:00:00','end':'12:00:00','courts':[3],'matchIds':nextIds});later['days'].append({'date':second,'courts':[3],'matchIds':nextIds})
 p=preview(later);check(len(p['matches'])==2 and all(m['start'].startswith(second) and m['court']==3 for m in p['matches']));save(p)
 check({m['id']:(m['start'],m['end'],m['court']) for plan in view()['plans'] for m in plan['matches'] if m['id'] in savedSlots}==savedSlots)
 check(any(m['status']=='SC' for plan in view()['plans'] if plan['programId']==pids[1] for m in plan['matches']))
 snapshot=view();process.terminate();process.wait(timeout=10);process=None;start();check(view()==snapshot);check({k:state()[k] for k in original}==original)
 print(f'PASS: {checks} session scheduling checks: per-session programs/courts, carryover, rest, no-fit, saved-slot protection, stale tokens, independent next rounds and SQL persistence.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
