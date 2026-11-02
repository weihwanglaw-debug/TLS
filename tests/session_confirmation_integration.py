"""Session confirmation validation uses disposable SQL only."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import datetime,timedelta,timezone
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5214/api/';checks=0;process=None

def check(value):
 global checks
 assert value;checks+=1

def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1200]);checks+=1;return json.loads(raw)

def state():return call('setup/')['data']
def rev():return state()['revision']
def now():return datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0)
def window(day,start,end,courts=None,ids=None,limits=None):return {'date':day,'start':start,'end':end,'courts':[1,2,3,4] if courts is None else courts,'matchIds':ids or [],'programLimits':limits or {}}
def validate(windows,status=200,revision=None,index=None):return call('scheduling/validate-session',{'revision':rev() if revision is None else revision,'eventId':eid,'sessions':windows,**({'sessionIndex':index} if index is not None else {})},status)
def view():return call('tournament/'+eid)
def save_plan(windows):
 plan=view()['eventPlan'];plan['dailySessions']=[];plan['sessions']=windows
 return call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')
def preview(windows):
 return call('scheduling/preview',{'revision':rev(),'eventId':eid,'matchMinutes':40,'restMinutes':0,'sessions':windows,'days':[{'date':w['date'],'courts':w['courts'],'matchIds':w['matchIds']} for w in windows]})
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5214))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 else:raise RuntimeError('Session API did not start')
 today=now().date().isoformat();tomorrow=(now().date()+timedelta(days=1)).isoformat()
 p=test_inputs.package();p['events'][0].update(startDate=today,endDate=tomorrow,maxProgramsPerParticipant=None)
 d=call('setup/preview?kind=trs&revision='+str(rev()),p);s=call('setup/confirm',{'revision':d['revision'],'token':d['token']})['data'];eid=s['events'][0]['id'];pid=s['programs'][0]['id']
 morning=window(tomorrow,'10:00:00','12:00:00');snap=state();check(validate([morning])['valid']);check(state()==snap)
 for windows in [[window(tomorrow,'14:00:00','12:00:00')],[window(tomorrow,'12:00:00','12:00:00')],[morning,window(tomorrow,'11:00:00','13:00:00')],[window(tomorrow,'10:00:00','12:00:00',[])],[window(today,'00:00:00','23:59:00')],[window(tomorrow,'10:00:00','12:00:00',limits={pid:13})]]:
  validate(windows,400);check(state()==snap)
 validate([morning,window(tomorrow,'12:00:00','14:00:00')]);check(state()==snap)
 validate([morning],409,revision=0);check(state()==snap)
 # Existing started sessions are not retrospectively rejected; changing their start is.
 past=window(today,'00:00:00','23:59:00');save_plan([past]);snap=state();validate([past,morning]);check(state()==snap)
 validate([{**past,'start':'00:00:01'},morning],400);check(state()==snap)
 save_plan([])
 rules={'name':'Session check','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 call('presets/',{'revision':rev(),'preset':{'id':'','name':'Session check','status':'A','isDefault':True,'rules':rules}},method='PUT')
 seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
 d=call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}});call('tournament/confirm',{'revision':d['revision'],'token':d['token']})
 ids=[m['id'] for plan in view()['plans'] for m in plan['matches'] if m['status']!='BY']
 # Save Session validates programs and quantities together, without persisting a partial form.
 target=window(tomorrow,'10:00:00','12:00:00',ids=ids,limits={pid:len(ids)})
 snap=state();validate([target],index=0);check(state()==snap)
 validate([morning],400,index=0);check(state()==snap)
 validate([{**target,'programLimits':{pid:0}}],400,index=0);check(state()==snap)
 validate([{**target,'programLimits':{pid:len(ids)+1}}],400,index=0);check(state()==snap)
 validate([target],400,index=-1);validate([target],400,index=1);check(state()==snap)
 # A saved planning draft survives server restart and never publishes slots.
 draft_body={'revision':rev(),'eventId':eid,'sessions':[target],'sessionIndex':0,'dirtySessions':[],'moveMatchIds':[]}
 saved=call('scheduling/draft',draft_body);check(saved['revision']>draft_body['revision'])
 check(all(not m.get('start') for plan in view()['plans'] for m in plan['matches']))
 check(view()['eventPlan']['planningDraft']['sessions']==[target])
 call('scheduling/draft',draft_body,409)
 process.terminate();process.wait(timeout=10)
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 check(view()['eventPlan']['planningDraft']['sessions']==[target])
 check(all(not m.get('start') for plan in view()['plans'] for m in plan['matches']))
 # Past-time validation also applies to final preview/confirm, not just the session button.
 bad=window(today,'00:00:01','23:59:00',ids=ids,limits={pid:len(ids)})
 call('scheduling/preview',{'revision':rev(),'eventId':eid,'matchMinutes':40,'restMinutes':0,'sessions':[bad],'days':[{'date':today,'courts':bad['courts'],'matchIds':ids}]},400)
 if now().hour<23:
  start=now()+timedelta(seconds=4);end=start+timedelta(minutes=45)
  imminent=window(start.date().isoformat(),start.time().isoformat(),end.time().isoformat(),ids=ids,limits={pid:len(ids)})
  d=preview([imminent]);snap=state();time.sleep(max(0,(start-now()).total_seconds())+1)
  call('scheduling/confirm',{'revision':d['revision'],'token':d['token']},400);check(state()==snap)
 scheduled=window(tomorrow,'10:00:00','12:00:00',ids=ids,limits={pid:len(ids)})
 d=preview([scheduled]);call('scheduling/confirm',{'revision':d['revision'],'token':d['token']});snap=state()
 check(view()['eventPlan']['planningDraft'] is None)
 validate([{**scheduled,'start':'11:00:00','programLimits':{pid:0}}],400);check(state()==snap)
 validate([{**scheduled,'courts':[2,3,4],'programLimits':{pid:0}}],400);check(state()==snap)
 validate([{**scheduled,'programLimits':{pid:0}}],index=0);check(state()==snap)
 print(f'PASS: {checks} session confirmation checks: no-write validation, ordering/overlap/courts/capacity, server venue clock, existing started sessions, stale revision, final confirmation and saved-match protection.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
