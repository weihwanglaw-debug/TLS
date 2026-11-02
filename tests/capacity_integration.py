"""Capacity/preview parity for the reported 16-player group event; disposable SQL only."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5215/api/';checks=0;process=None

def check(value):
 global checks
 assert value;checks+=1

def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method,headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=60) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1000]);checks+=1;return json.loads(raw)

def state():return call('setup/')['data']
def rev():return state()['revision']
def view():return call('tournament/'+eid)
def request(windows,rest=120):return {'revision':rev(),'eventId':eid,'matchMinutes':40,'restMinutes':rest,'sessions':windows,'days':[{'date':d,'courts':sorted(set(c for w in windows if w['date']==d for c in w['courts'])),'matchIds':list(dict.fromkeys(id for w in windows if w['date']==d for id in w['matchIds']))} for d in sorted(set(w['date'] for w in windows))]}
def window(day,start,end,ids,limits,courts=None):return {'date':day,'start':start,'end':end,'courts':courts or [1,2,3,4],'matchIds':ids,'programLimits':limits}
def parity(body):
 before=state();audit=int(db._sql('audit').strip());capacity=call('scheduling/capacity',body);preview=call('scheduling/preview',body)
 check(capacity['matches']==preview['matches']);check(capacity['unplaced']==preview['unplaced']);check(state()==before);check(int(db._sql('audit').strip())==audit)
 for c,p in zip(capacity['sessions'],preview['sessions']):check(c['capacity']>=p['capacity'])
 return capacity,preview
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5215))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 else:raise RuntimeError('Capacity API did not start')
 first=(date.today()+timedelta(days=10)).isoformat();second=(date.today()+timedelta(days=11)).isoformat();p=test_inputs.package()
 p['events'][0].update(startDate=first,endDate=second,maxProgramsPerParticipant=None)
 person=p['participants'][0];p['participants']=[{**person,'id':f'U{i}','name':f'Capacity Player {i}','email':f'capacity{i}@example.com','gender':'M' if i<16 else 'F'} for i in range(24)]
 p['entries']=[{'id':f'S{i}','eventId':'EV001','programId':'PR001','participantIds':[f'U{i}'],'name':'','seed':None,'status':'A'} for i in range(16)]+[{'id':f'D{i}','eventId':'EV001','programId':'PR002','participantIds':[f'U{i}',f'U{i+16}'],'name':'','seed':None,'status':'A'} for i in range(8)]
 d=call('setup/preview?kind=trs&revision='+str(rev()),p);s=call('setup/confirm',{'revision':d['revision'],'token':d['token']})['data'];eid=s['events'][0]['id'];a,b=[p['id'] for p in s['programs']]
 preset={'revision':rev(),'preset':{'id':'','name':'Capacity rules','status':'A','isDefault':True,'rules':{'name':'Capacity rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}}}
 req=urllib.request.Request(base+'presets/',data=json.dumps(preset).encode(),method='PUT',headers={'Content-Type':'application/json'})
 with urllib.request.urlopen(req) as r:r.read()
 for pid,fmt,groups in [(a,'GK',4),(b,'KO',1)]:
  entries=[e for e in state()['entries'] if e['programId']==pid]
  req=urllib.request.Request(base+'seeding/'+pid,data=json.dumps({'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in entries}}).encode(),method='PUT',headers={'Content-Type':'application/json'})
  with urllib.request.urlopen(req) as r:r.read()
  d=call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':fmt,'groupCount':groups,'advancePerGroup':2}});call('tournament/confirm',{'revision':d['revision'],'token':d['token']})
 plan=view()['eventPlan'];plan['restMinutes']=120;plan['matchMinutes']=40;call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')
 plans=view()['plans'];singles=[m['id'] for m in plans[0]['matches']];doubles=[m['id'] for m in plans[1]['matches'] if m['status']!='BY']
 before=state();windows=[window(first,'15:00:00','18:00:00',[],{}),window(first,'19:00:00','22:00:00',[],{})]
 cap,_=parity(request(windows));check(cap['sessions'][0]['capacity']>0)
 windows[0].update(matchIds=singles,programLimits={a:16});windows[1].update(matchIds=singles+doubles,programLimits={a:8,b:4})
 body=request(windows);cap,preview=parity(body);check(cap['sessions'][0]['capacity']==8);check(sum(m['programId']==a and m['start']<first+'T18:00:00' for m in preview['matches'])==8)
 error=call('scheduling/validate-session',{'revision':rev(),'eventId':eid,'sessions':windows,'sessionIndex':0},400);check('Only 8 of 16' in error['message']);check(state()==before)
 # Applying the returned placed counts produces exactly the same proposed slots, with no quota shortfalls.
 normalized=copy.deepcopy(body)
 for w,c in zip(normalized['sessions'],cap['sessions']):w['programLimits']={x['programId']:x['scheduled'] for x in c['programs'] if x['requested']}
 ncap,npreview=parity(normalized);check(npreview['matches']==preview['matches'])
 check(all(p['requested']==p['scheduled'] for w in npreview['sessions'] for p in w['programs']))
 check(call('scheduling/validate-session',{'revision':rev(),'eventId':eid,'sessions':normalized['sessions'],'sessionIndex':0})['valid']);check(state()==before)
 # Each program's maximum is demonstrably placeable alongside the other requested quantities.
 for index,c in enumerate(ncap['sessions']):
  for program in c['programs']:
   trial=copy.deepcopy(normalized);w=trial['sessions'][index];w['programLimits'][program['programId']]=program['maximum'];w['programLimits'].setdefault(a,0);w['programLimits'].setdefault(b,0);w['matchIds']=singles+doubles;trial['days'][0]['matchIds']=singles+doubles
   trialCap,trialPreview=parity(trial)
   got=sum(m['programId']==program['programId'] and m['start']>=w['date']+'T'+w['start'] and m['end']<=w['date']+'T'+w['end'] for m in trialPreview['matches'])
   check(got>=program['maximum'])
 shorter=copy.deepcopy(normalized);shorter['restMinutes']=20;parity(shorter)
 fewer=copy.deepcopy(normalized)
 for w in fewer['sessions']:w['courts']=[1,2]
 fewer['days'][0]['courts']=[1,2];parity(fewer)
 stale=copy.deepcopy(normalized);stale['revision']=0;call('scheduling/capacity',stale,409);check(state()==before)
 check(call('scheduling/capacity',request([window(second,'10:00:00','22:00:00',singles,{a:24})]))['sessions'][0]['capacity']>8)
 print(f'PASS: {checks} capacity checks: reported group setup, empty selection, shared players/rest, quota reconciliation, per-program maxima, exact preview placement parity, changed courts/rest/date, stale revision and no writes.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
