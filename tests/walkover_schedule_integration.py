"""Walkover timing preview/confirm/undo against disposable SQL only."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5276/api/';checks=0;process=None
def check(value,message):
 global checks
 assert value,message
 checks+=1
def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=2 if path=='setup/' else 60) as response:actual=response.status;raw=response.read()
 except urllib.error.HTTPError as ex:actual=ex.code;raw=ex.read()
 assert actual==status,(path,actual,status,raw[:900]);checks+=1
 return json.loads(raw)
def revision():return call('setup/')['data']['revision']
with socket.socket() as probe:probe.settimeout(0.5);assert probe.connect_ex(('127.0.0.1',5276))!=0
db=TestDatabase()
try:
 def start():
  global process
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(150):
   try:return call('setup/')['data']
   except urllib.error.URLError:time.sleep(.1)
  raise RuntimeError('Walkover test API failed to start')
 start();package=test_inputs.package();first=(date.today()+timedelta(days=10)).isoformat();last=(date.today()+timedelta(days=11)).isoformat()
 package['events'][0].update(startDate=first,endDate=last,maxProgramsPerParticipant=None);person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':f'P{i}','name':f'Walkover player {i}','email':f'wo{i}@example.com'} for i in range(10)]
 package['programs']=[{**program,'id':f'PR{i}','name':f'Program {i}'} for i in range(5)]
 package['entries']=[{'id':f'E{p}-{i}','eventId':'EV001','programId':f'PR{p}','participantIds':[f'P{p*2+i}'],'name':'','seed':i+1,'status':'A'} for p in range(5) for i in range(2)]
 imported=call('setup/preview?kind=trs&revision='+str(revision()),package);setup=call('setup/confirm',{'revision':imported['revision'],'token':imported['token']})['data'];eid=setup['events'][0]['id'];pids=[p['id'] for p in setup['programs']]
 def tournament():return call('tournament/'+eid)
 def planner():return call('scheduling/planner/'+eid)
 rules={'name':'Walkover test','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
 call('presets/',{'revision':revision(),'preset':{'id':'','name':'Walkover test','status':'A','isDefault':True,'rules':rules}},method='PUT');preset=call('presets/')['presets'][0]['id']
 ep=tournament()['eventPlan'];ep['officials']=[{'id':'WO-O1','name':'Jim'}];call('tournament/event',{'revision':revision(),'plan':ep},method='PUT');officialId=tournament()['eventPlan']['officials'][0]['id']
 for pid in pids:
  seeds=call('seeding/'+pid);call('seeding/'+pid,{'revision':revision(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seeds['entries']}},method='PUT')
  p=call('tournament/preview',{'revision':revision(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'KO'}});call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
 view=planner();nodes=view['nodes'];plan=view['plan'];plan.update(sessions=[{'date':first,'start':'09:00:00','end':'13:00:00','courts':[1,2]},{'date':last,'start':'09:00:00','end':'13:00:00','courts':[1,2]}],minutes={n['roundKey']:40 for n in nodes},restWarningMinutes=30,startStepMinutes=15)
 times=['09:00:00','09:45:00','10:30:00','09:00:00','09:00:00'];plan['placements']=[{'slotId':n['id'],'start':(last if i==4 else first)+'T'+times[i],'court':2 if i==3 else 1,'minutes':40} for i,n in enumerate(nodes)]
 p=call('scheduling/planner/preview',{'revision':revision(),'eventId':eid,'plan':plan});call('scheduling/planner/confirm',{'revision':p['revision'],'token':p['token'],'acceptWarnings':True})
 def matches():return {m['id']:m for pp in tournament()['plans'] for m in pp['matches']}
 sourceId=nodes[0]['matchId']
 def record():
  m=matches()[sourceId];call('tournament/result',{'revision':revision(),'matchId':sourceId,'matchVersion':m['matchVersion'],'games':[],'walkoverWinner':m['entryA']},method='PUT')
 def undo(status=200):
  m=matches()[sourceId];return call('tournament/clear-result',{'revision':revision(),'matchId':sourceId,'matchVersion':m['matchVersion'],'reason':'Undo walkover test'},status)
 def earlier(matchId=None):return call('scheduling/planner/earlier-preview',{'revision':revision(),'eventId':eid,'matchId':matchId or sourceId})
 def apply(p,status=200,accept=True):return call('scheduling/planner/earlier-confirm',{'revision':p['revision'],'token':p['token'],'acceptWarnings':accept},status)
 original=planner()['booked'];record();view=planner();check(view['walkovers'][0]['booking']['start']==first+'T09:00:00' and view['walkovers'][0]['winnerSide']=='A','walkover retains original row and explicit winner')
 snapshot=copy.deepcopy(matches());p=earlier();check(matches()==snapshot,'preview writes no match times');check(len(p['changes'])==2,'only the two later same-court same-session matches move')
 check([c['after']['start'][11:16] for c in p['changes']]==['09:00','09:45'],'earlier times respect 15-minute grid and exact40-minute games');check(any(f['kind']=='W' for f in p['findings']),'earlier-time warnings are returned for review')
 apply(p,status=400,accept=False);check(matches()==snapshot,'missing acknowledgement makes no partial changes')
 moved=apply(p);apply(p,status=400);check(moved['walkovers'][0]['hasEarlierTimes'],'undo metadata attached to existing walkover');movedBookings={b['slotId']:b for b in moved['booked']}
 check(all(movedBookings[b['slotId']]==b for b in original if b['slotId'] not in [nodes[0]['id'],nodes[1]['id'],nodes[2]['id']]),'other court and next day unchanged')
 process.terminate();process.wait(timeout=10);process=None;start();check(planner()==moved,'earlier times and undo data survive restart')
 # A later manual move makes undo unsafe, and must not be silently overwritten.
 selected=movedBookings[nodes[1]['id']];call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':selected['slotId'],'start':first+'T11:30:00','court':1,'acceptWarnings':True})
 snapshot=matches();undo(status=400);check(matches()==snapshot,'undo rejects changed booking without partial restoration')
 call('scheduling/planner/change',{'revision':revision(),'eventId':eid,'slotId':selected['slotId'],'start':selected['start'],'court':selected['court'],'acceptWarnings':True})
 # Called rows block both restoration and movement across them.
 nextId=nodes[1]['matchId'];call('dashboard/pair',{'revision':revision(),'matchId':nextId,'court':1,'umpireId':officialId,'presetId':preset})
 snapshot=matches();undo(status=400);check(matches()==snapshot,'undo protects called shifted matches')
 call('dashboard/cancel',{'revision':revision(),'court':1})
 undo();restored=planner();timing=lambda rows:{b['slotId']:(b['start'],b['court'],b['minutes']) for b in rows};check(timing(restored['booked'])==timing(original),'undo restores walkover and all moved timings');check(not tournament()['eventPlan']['schedulePlanner']['walkoverMoves'],'successful undo removes metadata')
 # Consecutive walkovers retain independent undo data and reverse safely in order.
 record();apply(earlier());secondMatch=matches()[nodes[1]['matchId']];call('tournament/result',{'revision':revision(),'matchId':secondMatch['id'],'matchVersion':secondMatch['matchVersion'],'games':[],'walkoverWinner':secondMatch['entryA']},method='PUT');apply(earlier(secondMatch['id']))
 check(len(tournament()['eventPlan']['schedulePlanner']['walkoverMoves'])==2,'second walkover preserves first undo reference');undo(status=400)
 secondMatch=matches()[secondMatch['id']];call('tournament/clear-result',{'revision':revision(),'matchId':secondMatch['id'],'matchVersion':secondMatch['matchVersion'],'reason':'Undo later walkover first'});undo();check(timing(planner()['booked'])==timing(original),'consecutive undo restores exact original timetable')
 record();call('dashboard/pair',{'revision':revision(),'matchId':nextId,'court':1,'umpireId':officialId,'presetId':preset});p=earlier();check(not p['changes'] and len(p['unchanged'])==2,'cannot move through called match');apply(p,status=400)
 call('dashboard/cancel',{'revision':revision(),'court':1});p=earlier();call('dashboard/court',{'revision':revision(),'court':4,'closed':True});apply(p,status=409);call('dashboard/court',{'revision':revision(),'court':4,'closed':False})
 moved=apply(earlier());call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':first},status=400);check(bool(tournament()['eventPlan']['schedulePlanner']['walkoverMoves']),'blocked reset keeps undo metadata');undo();reset=call('scheduling/planner/reset',{'revision':revision(),'eventId':eid,'fromDate':last});check(not tournament()['eventPlan']['schedulePlanner']['walkoverMoves'],'safe reset has no ghost undo metadata')
 check(any(b['slotId']==nodes[1]['id'] for b in reset['booked']),'reset preserves unrelated scheduled matches')
 print(f'PASS: {checks} walkover scheduling HTTP/data checks; disposable SQL cleaned.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
