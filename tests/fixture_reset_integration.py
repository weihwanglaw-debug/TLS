"""Backward reset flow and postponement on disposable SQL; never changes the local event."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5208/api/';checks=0;process=None
def check(ok):
 global checks
 assert ok;checks+=1
def call(path,body=None,expect=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json'},method=method or ('POST' if body is not None else 'GET'))
 try:
  with urllib.request.urlopen(req,timeout=30) as r:status=r.status;raw=r.read()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read()
 assert status==expect,(path,status,expect,raw[:800]);checks+=1;return json.loads(raw)
def state():return call('setup/')['data']
def mutate(path,body,expect=200,method='POST'):return call(path,{'revision':state()['revision'],**body},expect,method)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5208))!=0
db=TestDatabase()
try:
 def start():
  global process
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(150):
   try:return state()
   except urllib.error.URLError:time.sleep(.1)
  raise RuntimeError('Reset test API did not start')
 start();p=call('setup/preview?kind=trs&revision='+str(state()['revision']),test_inputs.package());s=call('setup/confirm',{'revision':p['revision'],'token':p['token']})['data'];eid=s['events'][0]['id'];pids=[p['id'] for p in s['programs']];original={k:s[k] for k in ['participants','entries','programs']}
 def view():return call('tournament/'+eid)
 def plan(pid):return next(p for p in view()['plans'] if p['programId']==pid)
 def key(pid):
  m=plan(pid)['matches'][0];return f"{pid}:{m['phase']}:{m['round']}"
 def reset(keys,expect=200):return mutate('tournament/reset-schedule',{'eventId':eid,'roundKeys':keys},expect)
 rules={'name':'Reset rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 preset=mutate('presets/',{'preset':{'id':'','name':'Reset rules','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
 def seeds(pid):
  entries=call('seeding/'+pid)['entries'];mutate('seeding/'+pid,{'mode':'N','seedCount':0,'seeds':{e['id']:None for e in entries}},method='PUT')
 def draw(pid):
  p=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}});call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
 for pid in pids:seeds(pid);draw(pid)
 check(view()['datesEditable']);check(view()['eventPlan']['matchMinutes']==40 and view()['eventPlan']['restMinutes']==120)
 ep=view()['eventPlan'];ep['officials']=[{'id':eid+'-O001','name':'Umpire'}];mutate('tournament/event',{'plan':ep},method='PUT')
 def schedule():
  v=view();d=v['eventInfo']['startDate'];ids=[m['id'] for p in v['plans'] for m in p['matches'] if m['status']!='BY'];body={'eventId':eid,'matchMinutes':40,'restMinutes':120,'sessions':[{'date':d,'start':'10:00:00','end':'18:00:00','courts':[1,2],'matchIds':ids}],'days':[{'date':d,'courts':[1,2],'matchIds':ids}]}
  p=mutate('scheduling/preview',body);check(len(p['matches'])==2);call('scheduling/confirm',{'revision':p['revision'],'token':p['token']})
 schedule();check(not view()['datesEditable']);before=view();keys=[key(pid) for pid in pids];seedsBefore=[copy.deepcopy(plan(pid)['seeds']) for pid in pids]
 for endpoint in ['reset','reset-latest-round','reset-seeding']:mutate('tournament/'+endpoint,{'programId':pids[0]},400)
 d=(date.fromisoformat(before['eventInfo']['startDate'])+timedelta(days=5)).isoformat()
 mutate('tournament/event-dates',{'eventId':eid,'startDate':d,'endDate':d},400,'PUT')
 check(view()==before)
 rows=call('tournament/'+eid+'/reset-schedule');check(len(rows['rounds'])==2 and all(not r['reason'] for r in rows['rounds']))
 reset([],400);reset([keys[0],keys[0]],400);reset([keys[0],'foreign'],400);check(view()==before)
 # Stale selections cannot reset changed records.
 ep=view()['eventPlan'];mutate('tournament/event',{'plan':ep},method='PUT');call('tournament/reset-schedule',{'revision':rows['revision'],'eventId':eid,'roundKeys':keys},409)
 untouched=copy.deepcopy(plan(pids[1]));reset([keys[0]]);check(plan(pids[1])==untouched);check(plan(pids[0])['seeds']==seedsBefore[0]);check(all(m['start'] is None and m['court'] is None for m in plan(pids[0])['matches']))
 check(not view()['datesEditable']);check(all(not any(id==m['id'] for id in w['matchIds']) for w in view()['eventPlan']['sessions'] for m in plan(pids[0])['matches']))
 reset([keys[1]]);check(view()['datesEditable']);check(len(view()['eventPlan']['sessions'])==1)
 # Step backwards through draw and seeding without touching participants.
 mutate('tournament/reset',{'programId':pids[0]});check(call('seeding/'+pids[0])['reviewed']);mutate('tournament/reset-seeding',{'programId':pids[0]});seed=call('seeding/'+pids[0]);check(not seed['reviewed'] and not any(seed['seeds'].values()))
 mutate('tournament/preview',{'eventId':eid,'programIds':[pids[0]],'courts':[],'drawOnly':True},400)
 other=copy.deepcopy(plan(pids[1]));mutate('tournament/event-dates',{'eventId':eid,'startDate':d,'endDate':d},method='PUT');check(plan(pids[1])==other);ep=view()['eventPlan'];check(not ep['sessions'] and not ep['dailySessions'] and not ep['scheduleDays']);check(ep['matchMinutes']==40 and ep['restMinutes']==120 and ep['officials'][0]['id']==eid+'-O001')
 seeds(pids[0]);draw(pids[0]);schedule();keys=[key(pid) for pid in pids]
 # A waiting tablet binding blocks reset until release, without affecting another round.
 m=plan(pids[0])['matches'][0];mutate('dashboard/pair',{'matchId':m['id'],'court':m['court'],'umpireId':eid+'-O001','presetId':preset,'reason':''})
 check(any(r['reason']=='Release waiting tablet assignments first' for r in call('tournament/'+eid+'/reset-schedule')['rounds']));reset([keys[0]],400)
 mutate('dashboard/cancel',{'court':m['court']});check(not next(r for r in call('tournament/'+eid+'/reset-schedule')['rounds'] if r['key']==keys[0])['reason'])
 # Recording a result in the other program blocks its entire round and makes a mixed batch atomic.
 m=plan(pids[1])['matches'][0];mutate('tournament/result',{'matchId':m['id'],'games':[{'a':5,'b':0}],'presetId':preset},method='PUT');before=view();reset(keys,400);check(view()==before);check(not view()['datesEditable']);mutate('tournament/reset',{'programId':pids[1]},400)
 played=copy.deepcopy(plan(pids[1]));reset([keys[0]]);check(plan(pids[1])==played);check(not view()['datesEditable'])
 snapshot=view();process.terminate();process.wait(timeout=10);process=None;start();check(view()==snapshot);check({k:state()[k] for k in original}==original)
 print(f'PASS: {checks} reset checks: backward order, selective/atomic reset, history/binding/stale guards, postponement preserving draws, timing defaults and SQL restart.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
