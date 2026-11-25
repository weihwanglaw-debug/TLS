"""Date-onward reset preview/confirm against disposable SQL only."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5208/api/';checks=0;process=None
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
with socket.socket() as probe:probe.settimeout(0.5);assert probe.connect_ex(('127.0.0.1',5208))!=0
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
 original=planner();originalIds={p['programId']:[m['id'] for m in p['matches']] for p in tournament()['plans']};originalSessions=copy.deepcopy(original['plan']['sessions']);originalMinutes=copy.deepcopy(original['plan']['minutes'])
 def reset(fromDate,expected=200,endpoint='scheduling/planner/reset',rev=None):return call(endpoint,{'revision':revision() if rev is None else rev,'eventId':eid,'fromDate':fromDate},expected)
 def inspect(fromDate):return call('scheduling/planner/reset-preview',{'revision':revision(),'eventId':eid,'fromDate':fromDate})
 for endpoint in ['scheduling/planner/reset','tournament/reset-schedule']:
  before=matches();call(endpoint,{'revision':revision(),'eventId':eid,'roundKeys':[nodes[0]['roundKey']]},400);check(matches()==before,'old destructive round payload is rejected')
 reset((date.fromisoformat(first)-timedelta(days=1)).isoformat(),400);reset((date.fromisoformat(last)+timedelta(days=1)).isoformat(),400)
 before=tournament();p=inspect(last);check(p['bookingCount']==1 and len(p['days'])==1 and not p['blockers'],'preview covers selected date onward');check(tournament()==before,'preview does not mutate bookings or results')
 m=matches()[nodes[0]['matchId']];call('tournament/result',{'revision':revision(),'matchId':m['id'],'matchVersion':m['matchVersion'],'games':[],'walkoverWinner':m['entryA']},method='PUT')
 m=matches()[nodes[2]['matchId']];call('tournament/result',{'revision':revision(),'matchId':m['id'],'matchVersion':m['matchVersion'],'games':[{'a':5,'b':0}]},method='PUT')
 call('dashboard/pair',{'revision':revision(),'matchId':nodes[1]['matchId'],'court':1,'umpireId':officialId,'presetId':preset})
 before=matches();stale=inspect(last);call('dashboard/court',{'revision':revision(),'court':4,'closed':True});reset(last,409,rev=stale['revision']);check(matches()==before,'stale reset leaves every booking unchanged');call('dashboard/court',{'revision':revision(),'court':4,'closed':False})
 reset(last);after=matches();check(all(after[id]==m for id,m in before.items() if id!=nodes[4]['matchId']),'earlier-day results, walkover and called match remain identical');check(after[nodes[4]['matchId']]['start'] is None and after[nodes[4]['matchId']]['status']=='SC','only later booking cleared');check(tournament()['calledMatchIds']==[nodes[1]['matchId']],'earlier call retained')
 check(planner()['plan']['sessions']==originalSessions and planner()['plan']['minutes']==originalMinutes,'sessions and duration estimates retained');reset(last,400)
 plan=planner()['plan'];plan['placements'].append(next(b for b in original['booked'] if b['slotId']==nodes[4]['id']));p=call('scheduling/planner/preview',{'revision':revision(),'eventId':eid,'plan':plan});call('scheduling/planner/confirm',{'revision':p['revision'],'token':p['token'],'acceptWarnings':True})
 p=inspect(first);check(len(p['blockers'])==3,'preview identifies all called/completed/walkover blockers');check(all(b['name'] and b['date']==first for b in p['blockers']),'blocker includes match identity and date')
 for endpoint in ['scheduling/planner/reset','tournament/reset-schedule']:
  before=tournament();reset(first,400,endpoint);check(tournament()==before,'one protected match blocks the entire reset atomically')
 call('dashboard/cancel',{'revision':revision(),'court':1})
 for index in [0,2]:
  m=matches()[nodes[index]['matchId']];call('tournament/clear-result',{'revision':revision(),'matchId':m['id'],'matchVersion':m['matchVersion'],'reason':'Correct result before date reset'})
 check(not inspect(first)['blockers'],'cleared history is not a blocker');reset(first,endpoint='tournament/reset-schedule');current=tournament();check(all(m['start'] is None and m['court'] is None for p in current['plans'] for m in p['matches'] if m['status']!='BY'),'all selected and later date bookings removed')
 check({p['programId']:[m['id'] for m in p['matches']] for p in current['plans']}==originalIds,'reset preserves draw records and pairings');check(not planner()['booked'],'no future reservations remain');check(planner()['plan']['sessions']==originalSessions,'all session hours and courts retained');check(planner()['plan']['minutes']==originalMinutes,'duration estimates retained')
 check(current['datesEditable'],'empty schedule unlocks event dates');snapshot=tournament();process.terminate();process.wait(timeout=10);process=None;start();check(tournament()==snapshot,'date reset persists across restart')
 call('tournament/reset',{'revision':revision(),'programId':pids[0]});check(not next(p for p in tournament()['plans'] if p['programId']==pids[0])['matches'],'unplayed draw can be reset afterward');call('tournament/reset-seeding',{'revision':revision(),'programId':pids[0]});check(not call('seeding/'+pids[0])['reviewed'],'seeding can be reset afterward')
 print(f'PASS: {checks} date-onward reset HTTP/data checks; disposable SQL cleaned.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
