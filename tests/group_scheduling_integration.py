"""Group rotations, quotas, carryover and slot swaps against disposable SQL; local event records are untouched."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta,datetime
from sql_test_database import TestDatabase,project,dll
import test_inputs

base='http://localhost:5209/api/';checks=0;process=None
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
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5209))!=0
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
 package['entries']=[{'id':f'E{p}-{i}','eventId':'EV001','programId':f'PR{p}','participantIds':[f'P{i+(8 if p==1 else 0)}'],'name':'','seed':None,'status':'A'} for p in range(3) for i in range(7 if p==0 else 4)]
 preview=call('setup/preview?kind=trs&revision='+str(revision()),package)
 s=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=s['events'][0]['id'];pids=[p['id'] for p in s['programs']]
 original={k:s[k] for k in ['participants','entries','programs','events']}
 rules={'name':'Session rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
 call('presets/',{'revision':revision(),'preset':{'id':'','name':'Session rules','status':'A','isDefault':True,'rules':rules}},method='PUT')
 def view():return call('tournament/'+eid)

 generated=[]
 for pid in pids:
  seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':revision(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
  p=call('tournament/preview',{'revision':revision(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'GK','groupCount':2 if pid==pids[0] else 1,'advancePerGroup':2}})
  call('tournament/confirm',{'revision':p['revision'],'token':p['token']});generated.append(next(p for p in view()['plans'] if p['programId']==pid)['matches'])
 a,b,c=[[m['id'] for m in rows] for rows in generated]
 def preview(value,expect=200):
  body={'revision':revision(),**value}
  if expect==200:
   capacity=call('scheduling/capacity',body)
   result=call('scheduling/preview',body)
   check(capacity['matches']==result['matches']);check(capacity['unplaced']==result['unplaced'])
   for actual,summary in zip(capacity['sessions'],result['sessions']):
    check(actual['capacity']>=summary['capacity'])
    check([(p['programId'],p['requested'],p['scheduled']) for p in actual['programs'] if p['requested'] or p['scheduled']]==[(p['programId'],p['requested'],p['scheduled']) for p in summary['programs'] if p['requested'] or p['scheduled']])
   return result
  return call('scheduling/preview',body,expect)
 def save(p,expect=200):return call('scheduling/confirm',{'revision':p['revision'],'token':p['token']},expect)
 def session(day,start,end,courts,ids,limits):return {'date':day,'start':start,'end':end,'courts':courts,'matchIds':ids,'programLimits':limits}
 def request(windows,rest=0,minutes=40):
  dates=sorted(set(w['date'] for w in windows))
  return {'eventId':eid,'matchMinutes':minutes,'restMinutes':rest,'sessions':windows,'days':[{'date':d,'courts':sorted(set(c for w in windows if w['date']==d for c in w['courts'])),'matchIds':list(dict.fromkeys(id for w in windows if w['date']==d for id in w['matchIds']))} for d in dates]}
 before=view()
 # Group rotations balance both groups before a player starts another group round.
 rotated=preview(request([session(first,'08:00:00','22:00:00',[1,2,3],a,{pids[0]:len(a)})],120))
 check(len(rotated['matches'])==len(a) and not rotated['unplaced']);check(view()==before)
 groups=next(p for p in before['plans'] if p['programId']==pids[0])['groups']
 for g in groups:
  rows=sorted([m for m in rotated['matches'] if m['groupId']==g['id']],key=lambda m:(m['start'],m['court']))
  if len(g['entryIds'])%2==0:check(set([rows[0]['entryA'],rows[0]['entryB']]).isdisjoint([rows[1]['entryA'],rows[1]['entryB']]))
  for id in g['entryIds']:
   games=sorted([m for m in rows if id in [m['entryA'],m['entryB']]],key=lambda m:m['start'])
   for x,y in zip(games,games[1:]):check(datetime.fromisoformat(y['start'])>=datetime.fromisoformat(x['end'])+timedelta(minutes=120))
 # Shared players across programs retain their planning rest, not just group-local rest.
 shared=preview(request([session(first,'08:00:00','22:00:00',[1,2,3,4],a+c,{pids[0]:len(a),pids[2]:len(c)})],120))
 check(any(m['programId']==pids[2] for m in shared['matches']))
 people={e['id']:set(e['participantIds']) for e in state()['entries']}
 for x in shared['matches']:
  for y in shared['matches']:
   if x['id']==y['id'] or x['start']>y['start']:continue
   if (people[x['entryA']]|people[x['entryB']])&(people[y['entryA']]|people[y['entryB']]):check(datetime.fromisoformat(y['start'])>=datetime.fromisoformat(x['end'])+timedelta(minutes=120))
 # Quantities are maximum new assignments, not extra matches used to fill spare courts.
 quota=request([session(first,'10:00:00','12:00:00',[1,2,3,4],a+b,{pids[0]:2,pids[1]:3})])
 q=preview(quota);check(len(q['matches'])==5);check(sum(m['programId']==pids[0] for m in q['matches'])==2);check(sum(m['programId']==pids[1] for m in q['matches'])==3)
 check(q['sessions'][0]['capacity']==5);check(len(q['unplaced'])==len(a+b)-5)
 invalid=copy.deepcopy(quota);invalid['sessions'][0]['programLimits'][pids[0]]=len(a)+1;preview(invalid,400);check(view()==before)
 # A short session reports a quota shortfall rather than assigning beyond its finish.
 short=copy.deepcopy(quota);short['sessions'][0].update(end='10:40:00',courts=[1]);short['days'][0]['courts']=[1]
 limited=preview(short);check(limited['sessions'][0]['capacity']==len(limited['matches'])==1);check(view()==before)
 short['sessions'][0]['programLimits']={pids[0]:1,pids[1]:0}
 p=preview(short);check(len(p['matches'])==1 and len(p['unplaced'])==len(a+b)-1)
 # Odd and unequal groups use the same rotation logic, without adding fake opponents.
 # Cross-date candidate pools are shared, but each real match is allocated once.
 split=request([session(first,'10:00:00','12:00:00',[1],a+b,{pids[0]:2,pids[1]:1}),session(second,'08:00:00','22:00:00',[1,2],a+b,{pids[0]:len(a),pids[1]:len(b)})])
 p=preview(split);check(len(p['matches'])==len(a+b));check(len(set(m['id'] for m in p['matches']))==len(a+b));check(any(m['start'].startswith(first) for m in p['matches']) and any(m['start'].startswith(second) for m in p['matches']))
 # Swapping a preview rotates its token, does not write SQL and retains all match numbers.
 pair=sorted([m for m in p['matches'] if m['start'].startswith(first)],key=lambda m:m['start'])[:2]
 originalSlots={m['id']:(m['start'],m['court']) for m in p['matches']}
 swapped=call('scheduling/swap',{'revision':p['revision'],'eventId':eid,'token':p['token'],'matchA':pair[0]['id'],'matchB':pair[1]['id']})
 check(swapped['token']!=p['token']);check(view()==before);save(p,400)
 updated={m['id']:m for m in swapped['matches']}
 check((updated[pair[0]['id']]['start'],updated[pair[0]['id']]['court'])==originalSlots[pair[1]['id']])
 check(all((m['start'],m['court'])==originalSlots[m['id']] for m in swapped['matches'] if m['id'] not in {pair[0]['id'],pair[1]['id']}))
 # Persist a partial first day, then reopen and append the remaining matches tomorrow.
 partial=request([session(first,'10:00:00','12:00:00',[1],a+b,{pids[0]:2,pids[1]:1})])
 p=preview(partial);check(len(p['matches'])==3);save(p)
 stored=view();check(stored['eventPlan']['sessions'][0]['programLimits']=={pids[0]:0,pids[1]:0});slots={m['id']:(m['start'],m['end'],m['court']) for plan in stored['plans'] for m in plan['matches'] if m['start']}
 pending=[m['id'] for plan in stored['plans'] for m in plan['matches'] if m['programId'] in pids[:2] and not m['start']]
 remainingA=sum(id in a for id in pending);remainingB=sum(id in b for id in pending)
 later=request([session(first,'10:00:00','12:00:00',[1],a+b,{pids[0]:0,pids[1]:0}),session(second,'08:00:00','22:00:00',[1,2],pending,{pids[0]:remainingA,pids[1]:remainingB})],minutes=60)
 p=preview(later);check(len(p['matches'])==len(pending));save(p)
 check({m['id']:(m['start'],m['end'],m['court']) for plan in view()['plans'] for m in plan['matches'] if m['id'] in slots}==slots)
 check(all(m['start'] for plan in view()['plans'] if plan['programId'] in pids[:2] for m in plan['matches']))
 # Different durations must not overrun the destination or neighbouring match.
 current=view();dayOne=sorted([m for plan in current['plans'] for m in plan['matches'] if m['id'] in slots],key=lambda m:m['start']);long=next(m for plan in current['plans'] for m in plan['matches'] if m['start'] and m['durationMinutes']==60)
 def swap(x,y,expect=200,rev=None):return call('scheduling/swap',{'revision':revision() if rev is None else rev,'eventId':eid,'matchA':x['id'],'matchB':y['id']},expect)
 snap=view();swap(dayOne[0],long,400);check(view()==snap)
 oldRevision=revision();swap(dayOne[0],dayOne[1]);updated={m['id']:m for plan in view()['plans'] for m in plan['matches']}
 check(updated[dayOne[0]['id']]['start']==dayOne[1]['start']);check(updated[dayOne[1]['id']]['start']==dayOne[0]['start'])
 swap(dayOne[0],dayOne[1],409,oldRevision)
 check(all((updated[mid]['start'],updated[mid]['end'],updated[mid]['court'])==slot for mid,slot in slots.items() if mid not in {dayOne[0]['id'],dayOne[1]['id']}))
 # Closed destinations reject atomically even when the match has not started.
 call('dashboard/court',{'revision':revision(),'court':dayOne[0]['court'],'closed':True})
 snap=view();swap(dayOne[0],dayOne[1],400);check(view()==snap)
 call('dashboard/court',{'revision':revision(),'court':dayOne[0]['court'],'closed':False})
 # A called match is excluded even before scoring starts; no partial changes on failure.
 plan=view()['eventPlan'];plan['officials']=[{'id':eid+'-O001','name':'Rotation Umpire'}];call('tournament/event',{'revision':revision(),'plan':plan},method='PUT')
 target=updated[dayOne[0]['id']]
 call('dashboard/pair',{'revision':revision(),'matchId':target['id'],'court':target['court'],'umpireId':eid+'-O001','presetId':target['presetId']})
 snap=view();swap(target,dayOne[1],400);check(view()==snap);check(target['id'] in snap['calledMatchIds'])
 # The persisted addition data survives a server restart unchanged.
 snapshot=view();process.terminate();process.wait(timeout=10);process=None;start();check(view()==snapshot);check({k:state()[k] for k in original}==original)
 print(f'PASS: {checks} group scheduling checks: rotations, quotas, rest, cross-date continuation, partial saves, preview/saved swaps, duration conflicts, called protection, stale revisions and SQL persistence.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
