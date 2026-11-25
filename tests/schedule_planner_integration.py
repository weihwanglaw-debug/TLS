"""Whole-tournament planner against disposable SQL. Never writes operational TLS/TRS data."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error,tempfile
from pathlib import Path
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
  with urllib.request.urlopen(req,timeout=2 if path=='setup/' else 60) as response:actual=response.status;raw=response.read()
 except urllib.error.HTTPError as ex:actual=ex.code;raw=ex.read()
 assert actual==status,(path,actual,status,raw[:1200]);checks+=1
 return json.loads(raw)
def revision():return call('setup/')['data']['revision']
with socket.socket() as probe:probe.settimeout(0.5);assert probe.connect_ex(('127.0.0.1',5271))!=0
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
 plan=v['plan'];plan['sessions']=[{'date':first,'start':'09:00:00','end':'20:00:00','courts':[1,2,3,4]},{'date':last,'start':'09:00:00','end':'20:00:00','courts':[1,2,3,4]}];plan['minutes']={n['roundKey']:60 if n['final'] else 40 if n['phase']=='K' and n['round']==2 else 30 for n in v['nodes']};plan['restWarningMinutes']=30
 def request(plan,action='check',**extra):return {'revision':revision(),'eventId':eid,'plan':plan,'action':action,**extra}
 def preview(plan,action='check',status=200,**extra):return call('scheduling/planner/preview',request(plan,action,**extra),status)
 def confirm(p,status=200,accept=True):return call('scheduling/planner/confirm',{'revision':p['revision'],'token':p['token'],'acceptWarnings':accept},status)
 call('scheduling/planner/draft',{},status=404);call('scheduling/draft',{},status=404)
 check('draft' not in v and 'planningDraft' not in tournament()['eventPlan'],'saved editing-state fields removed')
 bad=copy.deepcopy(plan);bad['sessions'][1]['date']=first;bad['sessions'][1]['start']='10:00:00';preview(bad,status=400)
 confirm(preview(plan),status=400)
 before=tournament();p=preview(plan,'auto');check(tournament()==before,'preview does not persist matches');check(len(p['plan']['placements'])==26,'full allocation');check(not any(f['kind']=='E' for f in p['findings']),'valid plan')
 check(all(datetime.fromisoformat(b['start']).minute%15==0 for b in p['plan']['placements']),'15-minute starts')
 check(all(b['minutes']==plan['minutes'][next(n['roundKey'] for n in v['nodes'] if n['id']==b['slotId'])] for b in p['plan']['placements']),'exact individual duration')
 check(not any(f['kind']=='W' for f in p['findings']),'generous room honours rest')
 confirmed=confirm(p);check(len(confirmed['booked'])==26,'current+future bookings persist');bookings={b['slotId']:b for b in confirmed['booked']}
 snapshot=planner();process.terminate();process.wait(timeout=10);process=None;start();check(planner()==snapshot,'restart persistence')
 bad=copy.deepcopy(confirmed['plan']);key=v['nodes'][0]['roundKey'];bad['minutes'][key]+=5;preview(bad,status=400)
 # A reset/redraw introduces unplaced slots without blocking maintenance of existing bookings.
 affected=[n['roundKey'] for n in v['nodes'] if n['programId']==pids[2] and n['round']==1][0]
 byesBefore={m['id']:(m['status'],m['winnerId']) for pp in tournament()['plans'] for m in pp['matches'] if m['status']=='BY'}
 # Keep this program entirely on the later date before clearing from that date onward.
 shifted=planner()['plan'];nodeMap={n['id']:n for n in planner()['nodes']}
 for booking in shifted['placements']:
  n=nodeMap[booking['slotId']]
  if n['programId']==pids[2]:booking.update(start=last+f"T{15+n['round']}:00:00",court=3+n['position']%2)
 confirm(preview(shifted))
 resetLate=call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':last})
 check({m['id']:(m['status'],m['winnerId']) for pp in tournament()['plans'] for m in pp['matches'] if m['status']=='BY'}==byesBefore and bool(byesBefore),'schedule reset preserves automatic bye advancement')
 check(len(resetLate['plan']['sessions'])==2,'round reset preserves session configuration')
 check(all(b in resetLate['plan']['placements'] for b in resetLate['booked']),'round reset retains unaffected plan allocations')
 check(bool(resetLate['notices']),'reset reports removed placements')
 call('tournament/reset',{'revision':revision(),'programId':pids[2]})
 check(len(planner()['unpreparedPrograms'])==1,'program can temporarily have no draw after initial confirmation')
 confirm(preview(planner()['plan']),status=400)
 generate(pids[2]);late=planner()
 check(late['hasConfirmed'] and len(late['booked'])<len(late['nodes']),'later drawn slots are unplaced information')
 confirm(preview(late['plan']),status=400)
 # Direct changes validate only the affected bookings, preserve other plans, and still enforce hard conflicts.
 late=planner();own=next(b for b in late['booked'] if b['slotId']==next(n['id'] for n in late['nodes'] if n['programId']==pids[1] and n['round']==2))
 beforeChange=copy.deepcopy(late['booked'])
 result=call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':own['slotId'],'start':own['start'],'court':own['court'],'acceptWarnings':True})
 check(result['saved'],'atomic change remains available while new matches are unplaced')
 check(all(b in result['view']['booked'] for b in beforeChange if b['slotId']!=own['slotId']),'direct change leaves unrelated bookings intact')
 # Semi-final swaps save both selected bookings atomically, without scheduling the late program.
 swapView=planner();semiIds=[n['id'] for n in swapView['nodes'] if n['programId']==pids[1] and n['round']==2];swapBefore={b['slotId']:b for b in swapView['booked']};a,b=[swapBefore[id] for id in semiIds]
 swapped=call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':a['slotId'],'start':a['start'],'court':a['court'],'otherSlotId':b['slotId'],'acceptWarnings':True})
 swapAfter={b['slotId']:b for b in swapped['view']['booked']}
 check((swapAfter[a['slotId']]['start'],swapAfter[a['slotId']]['court'])==(b['start'],b['court']) and (swapAfter[b['slotId']]['start'],swapAfter[b['slotId']]['court'])==(a['start'],a['court']),'semi-final swap saves both placements')
 check(all(swapAfter[id]==old for id,old in swapBefore.items() if id not in semiIds),'swap preserves other bookings')
 # Equal duration is mandatory across programs; equal-duration cross-stage swaps remain forbidden.
 invalidSwapView=planner();snapshot=copy.deepcopy(invalidSwapView['booked']);groupNode=next(n for n in invalidSwapView['nodes'] if n['phase']=='G');groupKnockout=next(n for n in invalidSwapView['nodes'] if n['programId']==groupNode['programId'] and n['phase']=='K' and n['round']==1);bookedById={b['slotId']:b for b in snapshot}
 for firstId,otherId,reason in [(a['slotId'],groupNode['id'],'same estimated duration'),(groupNode['id'],groupKnockout['id'],'same stage and round')]:
  slot=bookedById[firstId];rejected=call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':firstId,'start':slot['start'],'court':slot['court'],'otherSlotId':otherId,'acceptWarnings':True},status=400)
  check(reason in rejected['message'],'API independently enforces swap compatibility')
  check(planner()['booked']==snapshot,'invalid duration or round swap leaves both bookings unchanged')
 # Moving a final before its feeder is rejected without a partial save.
 call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':own['slotId'],'start':first+'T08:00:00','court':a['court'],'acceptWarnings':True},status=400)
 check(planner()['booked']==swapped['view']['booked'],'invalid direct move is atomic')
 # Closing a court leaves existing slots as warnings and does not prevent a repair elsewhere.
 call('dashboard/court',{'revision':revision(),'court':own['court'],'closed':True})
 closed=planner();check(any(f['kind']=='W' and 'closed' in f['message'] for f in closed['findings']),'closed court shown as warning for existing bookings')
 moved=False
 for court in closed['courts']:
  req={'revision':revision(),'eventId':eid,'slotId':own['slotId'],'start':own['start'],'court':court,'acceptWarnings':True}
  try:
   repair=call('scheduling/planner/change',req);moved=repair['saved'];break
  except AssertionError as ex:
   if "400" not in str(ex):raise
 check(moved,'direct move off closed court succeeds despite other closed-court bookings')
 bad={'revision':revision(),'eventId':eid,'slotId':own['slotId'],'start':own['start'],'court':own['court'],'acceptWarnings':True}
 call('scheduling/planner/change',bad,status=400)
 call('dashboard/court',{'revision':revision(),'court':own['court'],'closed':False})
 # A short-rest direct change must first return warnings without saving.
 maintenance=planner();node=next(n for n in maintenance['nodes'] if n['id']==own['slotId']);byId={b['slotId']:b for b in maintenance['booked']}
 earliest=max(datetime.fromisoformat(byId[id]['start'])+timedelta(minutes=byId[id]['minutes']) for id in node['dependencies']);earliest+=timedelta(minutes=(-earliest.minute)%15)
 warningRequest=None;beforeWarning=copy.deepcopy(maintenance['booked']);warningRevision=revision()
 for court in maintenance['courts']:
  candidate={'revision':revision(),'eventId':eid,'slotId':own['slotId'],'start':earliest.isoformat(),'court':court}
  try:
   response=call('scheduling/planner/change',candidate)
   if not response['saved']:warningRequest=candidate;break
  except AssertionError as ex:
   if "400" not in str(ex):raise
 check(warningRequest is not None,'short-rest change returns actionable warnings')
 check(planner()['booked']==beforeWarning and revision()==warningRevision,'unacknowledged direct warning does not save or add history')
 call('scheduling/planner/change',{**warningRequest,'acceptWarnings':True})
 # Older scheduling writers are explicitly refused for planner-owned events.
 legacy={'revision':revision(),'eventId':eid,'sessions':closed['plan']['sessions'],'matchMinutes':30,'restMinutes':0,'days':[]}
 call('scheduling/preview',legacy,status=409)
 call('scheduling/draft',{},status=404)
 # Refill all late slots before the result-progression regression below.
 filled=confirm(preview(planner()['plan'],'auto'));bookings={b['slotId']:b for b in filled['booked']}
 # Completing group results then generating KO binds existing future slots once.
 preset=call('presets/')['presets'][0]['id']
 # Walkover release and undo preserve the timetable; dependent results are protected.
 def actual_matches():return {m['id']:m for pp in tournament()['plans'] for m in pp['matches']}
 def walkover(match):return call('tournament/result',{'revision':revision(),'matchId':match['id'],'matchVersion':match['matchVersion'],'presetId':preset,'games':[],'walkoverWinner':match['entryA']},method='PUT')
 # Group walkovers can close a court gap and undo without losing future qualifier slots.
 groupBefore=planner();groupTimings={b['slotId']:(b['start'],b['court'],b['minutes']) for b in groupBefore['booked']};groupNodes={n['id']:n for n in groupBefore['nodes']};groupSource=next(groupNodes[b['slotId']] for b in sorted(groupBefore['booked'],key=lambda b:b['start']) if groupNodes[b['slotId']]['phase']=='G')
 walkover(actual_matches()[groupSource['matchId']]);gp=call('scheduling/planner/earlier-preview',{'revision':revision(),'eventId':eid,'matchId':groupSource['matchId']});check(bool(gp['changes']),'group walkover offers earlier same-court matches')
 call('scheduling/planner/earlier-confirm',{'revision':gp['revision'],'token':gp['token'],'acceptWarnings':True});gm=actual_matches()[groupSource['matchId']];call('tournament/clear-result',{'revision':revision(),'matchId':gm['id'],'matchVersion':gm['matchVersion'],'reason':'Undo group walkover retiming'})
 check({b['slotId']:(b['start'],b['court'],b['minutes']) for b in planner()['booked']}==groupTimings,'group undo restores all times including future qualification bookings')
 target=next(m for pp in tournament()['plans'] if pp['programId']==pids[2] for m in pp['matches'] if m['status']=='SC')
 original={b['slotId']:b for b in planner()['booked']};walkover(target);released=planner()
 check(len(released['booked'])==25 and all(b==original[b['slotId']] for b in released['booked']),'walkover frees its court without shifting later games')
 check(any(n['matchId']==target['id'] and not n['requiresCourt'] for n in released['nodes']),'walkover is a result reference, not a court reservation')
 confirm(preview(released['plan']));noticesBeforeUndo=planner()['notices']
 match=actual_matches()[target['id']];call('tournament/clear-result',{'revision':revision(),'matchId':match['id'],'matchVersion':match['matchVersion'],'reason':'Undo walkover'})
 restored={b['slotId']:b for b in planner()['booked']};timing=lambda b:(b['start'],b['court'],b['minutes']);check(set(restored)==set(original) and all(timing(restored[id])==timing(b) for id,b in original.items()),'undo restores the original slot and future reservations')
 check(planner()['notices']==noticesBeforeUndo,'undo does not claim that retained timetable placements were removed')
 walkover(actual_matches()[target['id']]);generate(pids[2]);retained={b['slotId']:b for b in planner()['booked']}
 match=actual_matches()[target['id']];call('tournament/clear-result',{'revision':revision(),'matchId':match['id'],'matchVersion':match['matchVersion'],'reason':'Undo after unplayed advancement'})
 check(all(b==retained[b['slotId']] for b in planner()['booked'] if b['slotId'] in retained),'undo keeps future times while unplayed draw positions are rebuilt')
 walkover(actual_matches()[target['id']]);generate(pids[2]);later=next(m for pp in tournament()['plans'] if pp['programId']==pids[2] for m in pp['matches'] if m['round']==2 and m['status']=='SC')
 call('tournament/result',{'revision':revision(),'matchId':later['id'],'matchVersion':later['matchVersion'],'presetId':preset,'games':[{'a':5,'b':0}]},method='PUT')
 match=actual_matches()[target['id']];beforeUndo=tournament();call('tournament/clear-result',{'revision':revision(),'matchId':match['id'],'matchVersion':match['matchVersion'],'reason':'Unsafe undo'},status=400)
 check(tournament()==beforeUndo,'walkover undo cannot erase later results')
 beforeReset=tournament();call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':first},status=400);check(tournament()==beforeReset,'walkover or later result blocks date reset')
 later=actual_matches()[later['id']];call('tournament/clear-result',{'revision':revision(),'matchId':later['id'],'matchVersion':later['matchVersion'],'reason':'Clear later result before scheduling reset'});match=actual_matches()[target['id']];call('tournament/clear-result',{'revision':revision(),'matchId':match['id'],'matchVersion':match['matchVersion'],'reason':'Undo walkover before date reset'})
 call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':first})
 filled=confirm(preview(planner()['plan'],'auto'));bookings={b['slotId']:b for b in filled['booked']}
 rows=next(p for p in tournament()['plans'] if p['programId']==pids[0])['matches']
 for m in rows:call('tournament/result',{'revision':revision(),'matchId':m['id'],'matchVersion':m['matchVersion'],'presetId':preset,'games':[{'a':5,'b':0}],'startedAt':m['start'],'finishedAt':m['end']},method='PUT')
 generate(pids[0]);now=planner();newMatches=[n for n in now['nodes'] if n['programId']==pids[0] and n['phase']=='K' and n['round']==1]
 check(len(newMatches)==2 and all(n['matchId'] for n in newMatches),'qualifiers materialized')
 actual={m['id']:m for p in tournament()['plans'] for m in p['matches']}
 for n in newMatches:
  b=bookings[n['id']];m=actual[n['matchId']];check(m['start']==b['start'] and m['court']==b['court'] and m['durationMinutes']==b['minutes'],'future booking binds exact match')
 # Re-read or unrelated settings must not allocate a second slot or invalidate others.
 check(len(planner()['booked'])==26,'no duplicate materialization')
 current=planner();bad=copy.deepcopy(current['plan']);b=bad['placements'][0];b['start']=b['start'][:14]+'05:00';preview(bad,status=400)
 completed=next(m for p in tournament()['plans'] for m in p['matches'] if m['status']=='C')
 completedNode=next(n for n in now['nodes'] if n['matchId']==completed['id'])
 call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':completedNode['id'],'start':completed['start'],'court':completed['court'],'acceptWarnings':True},status=400)
 # Stale tokens reject with no rewrite.
 p=preview(current['plan']);call('tournament/event',{'revision':revision(),'plan':tournament()['eventPlan']},method='PUT');confirm(p,status=409)
 # Clearing a result keeps its replay slot but removes materialized and future-only dependants.
 source=next(m for p in tournament()['plans'] if p['programId']==pids[0] for m in p['matches'] if m['phase']=='G' and m['status']=='C')
 call('tournament/clear-result',{'revision':revision(),'matchId':source['id'],'matchVersion':source['matchVersion'],'reason':'Planner correction check'})
 cleared=planner();check(not any(b['slotId'].startswith(pids[0]+'/K/') for b in cleared['booked']),'result clear removes future-only dependent bookings')
 replay=next(m for p in tournament()['plans'] for m in p['matches'] if m['id']==source['id']);check(replay['start']==source['start'] and replay['status']=='SC','result clear retains replay schedule')
 # Reset group stage clears dependent future bookings and results but retains availability and estimates.
 gkey=next(n['roundKey'] for n in current['nodes'] if n['programId']==pids[0] and n['phase']=='G')
 call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':first},status=400)
 for m in list(actual_matches().values()):
  if m['status'] in ['C','WO']:
   fresh=actual_matches().get(m['id'])
   if fresh:call('tournament/clear-result',{'revision':revision(),'matchId':fresh['id'],'matchVersion':fresh['matchVersion'],'reason':'Explicit result correction before schedule reset'})
 reset=call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':first});check(not reset['booked'],'all date-onward bookings cleared');check(len(reset['plan']['sessions'])==2,'sessions retained')
 reset['plan']['minutes'][gkey]=25;preview(reset['plan']);check(True,'reset unlocks estimates without historical blocker')
 # Rehearse metadata cleanup and strict-reader restart on disposable data only.
 process.terminate();process.wait(timeout=10);process=None
 tables=db._sql('tables').strip();db._sql('old-schedule-metadata')
 with tempfile.TemporaryDirectory(prefix='tls-schedule-upgrade-') as folder:
  backup=Path(folder)/'event-operations.json';db.command('--upgrade-schedule','true','--backup-file',str(backup))
  check(backup.exists() and 'planningDraft' in backup.read_text(encoding='utf-8'),'upgrade backs up existing JSON before cleanup')
  upgraded=db._sql('event-json');rows=json.loads(upgraded)
  check(all('planningDraft' not in json.loads(row['Json']) and 'draft' not in json.loads(row['Json']).get('schedulePlanner',{}) for row in rows),'both obsolete metadata properties removed')
  db.command('--upgrade-schedule','true','--backup-file',str(Path(folder)/'second.json'))
  check(upgraded==db._sql('event-json') and tables==db._sql('tables').strip(),'cleanup is idempotent and changes no tables')
 start();check('plan' in planner(),'new strict reader opens upgraded scheduling data')
 print(f'PASS: {checks} planner HTTP/data checks; disposable SQL cleaned in finally.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
