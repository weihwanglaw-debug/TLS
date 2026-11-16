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
 check(not capacity['matches'] and not capacity['unplaced']);check(state()==before);check(int(db._sql('audit').strip())==audit)
 for c,p in zip(capacity['sessions'],preview['sessions']):check(c['capacity']==p['capacity'])
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
 body=request(windows);cap,preview=parity(body)
 check([w['capacity'] for w in cap['sessions']]==[16,16])
 # Quantities in one session cannot shrink another session's capacity.
 for count in [0,8,15,16]:
  changed=copy.deepcopy(body);changed['sessions'][0]['programLimits'][a]=count
  result,_=parity(changed);check([w['capacity'] for w in result['sessions']]==[16,16])
 check(call('scheduling/validate-session',{'revision':rev(),'eventId':eid,'sessions':windows,'sessionIndex':0})['valid']);check(state()==before)
 # A legacy rest value is accepted but has no effect on the proposed slots.
 noRest=copy.deepcopy(body);noRest['restMinutes']=0
 _,withoutRest=parity(noRest);check(withoutRest['matches']==preview['matches'])
 # Real player and court overlaps remain impossible; back-to-back matches are allowed.
 people={e['id']:set(e['participantIds']) for e in state()['entries']}
 backToBack=False
 for x in preview['matches']:
  for y in preview['matches']:
   if x['id']==y['id']:continue
   shared=(people[x['entryA']]|people[x['entryB']])&(people[y['entryA']]|people[y['entryB']])
   if shared or x['court']==y['court']:check(x['end']<=y['start'] or y['end']<=x['start'])
   if shared and x['end']==y['start']:backToBack=True
 check(backToBack)
 normalized=copy.deepcopy(body)
 fewer=copy.deepcopy(body)
 for w in fewer['sessions']:w['courts']=[1,2]
 fewer['days'][0]['courts']=[1,2]
 smaller,_=parity(fewer);check([w['capacity'] for w in smaller['sessions']]==[8,8])
 shorter=copy.deepcopy(body);shorter['sessions'][1]['end']='21:00:00'
 smaller,_=parity(shorter);check([w['capacity'] for w in smaller['sessions']]==[16,12])
 # Capacity ignores save/placement rules, and incomplete cards do not affect siblings.
 overlap=copy.deepcopy(body);overlap['sessions'][1].update(start='17:00:00',end='19:00:00')
 result=call('scheduling/capacity',overlap);check([w['capacity'] for w in result['sessions']]==[16,12])
 call('scheduling/preview',overlap,400)
 past=copy.deepcopy(body);past['sessions'][0]['date']=(date.today()-timedelta(days=1)).isoformat()
 result=call('scheduling/capacity',past);check(result['sessions'][0]['capacity']==16)
 incomplete=copy.deepcopy(body);incomplete['sessions'][0].update(start='19:00:00',end='18:00:00')
 result=call('scheduling/capacity',incomplete);check(result['sessions'][0]['capacity'] is None and result['sessions'][0]['issue']);check(result['sessions'][1]['capacity']==16)
 incomplete['sessions'][0].update(start='15:00:00',courts=[])
 result=call('scheduling/capacity',incomplete);check(result['sessions'][0]['capacity']==0);check(result['sessions'][1]['capacity']==16)
 malformedSelection=copy.deepcopy(body);malformedSelection['sessions'][0].update(matchIds=['not-a-match'],programLimits={'unknown':999})
 check(call('scheduling/capacity',malformedSelection)['sessions'][0]['capacity']==16);check(state()==before)
 # Legacy date-level court selections use the same formula in capacity and Preview.
 legacy={'revision':rev(),'eventId':eid,'matchMinutes':40,'restMinutes':0,'sessions':[{'date':second,'start':'10:00:00','end':'13:00:00'}],'days':[{'date':second,'courts':[1,2],'matchIds':[]}]}
 legacyCap,legacyPreview=parity(legacy);check(legacyCap['sessions'][0]['capacity']==8)
 stale=copy.deepcopy(normalized);stale['revision']=0;call('scheduling/capacity',stale,409);check(state()==before)
 check(call('scheduling/capacity',request([window(second,'10:00:00','22:00:00',singles,{a:24})]))['sessions'][0]['capacity']>8)
 # Confirmed slots reduce only the allowance for new matches; total capacity stays fixed.
 confirmed=call('scheduling/preview',body);call('scheduling/confirm',{'revision':confirmed['revision'],'token':confirmed['token']})
 fresh=copy.deepcopy(body);fresh['revision']=rev()
 after=state();result=call('scheduling/capacity',fresh)
 check([w['capacity'] for w in result['sessions']]==[16,16]);check(result['sessions'][0]['available']==0);check(state()==after)
 # Excess new requests are rejected at Save Session, without rescheduling existing matches.
 full=copy.deepcopy(fresh['sessions']);full[0]['programLimits']={a:16}
 call('scheduling/validate-session',{'revision':rev(),'eventId':eid,'sessions':full,'sessionIndex':0},400);check(state()==after)
 print(f'PASS: {checks} capacity checks: fixed court-time capacity, independent session quantities, legacy rest ignored, player/court overlap prevention, back-to-back play, preview parity, changed courts/duration/date, stale revision and no writes.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
