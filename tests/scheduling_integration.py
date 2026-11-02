"""Draws and dated scheduling are separate; only disposable SQL data is used."""
import copy,json,socket,subprocess,tempfile,time,urllib.request,urllib.error
from datetime import date,timedelta
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5202/api/';checks=0;process=None;db=TestDatabase()
def call(path,body=None,method=None,expect=200):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==expect,(path,code,expect,raw[:600]);checks+=1;return json.loads(raw)
def check(ok):
 global checks
 assert ok;checks+=1
def state():return call('setup/')['data']
def view():return call('tournament/'+eid)
def revision():return state()['revision']
def draw(pid,settings=None,expect=200):return call('tournament/preview',{'revision':revision(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,**({'settings':settings} if settings else {})},expect=expect)
def confirm(p):return call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5202))!=0
try:
 with tempfile.TemporaryDirectory(prefix='tls-schedule-') as folder:
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
   for _ in range(100):
    try:return state()
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Test API did not start')
  start();package=test_inputs.package();first=(date.today()+timedelta(days=10)).isoformat();second=(date.today()+timedelta(days=11)).isoformat();package['events'][0].update(startDate=first,endDate=second,maxProgramsPerParticipant=None)
  person=package['participants'][0];package['participants']=[{**person,'id':'P'+str(i),'name':'Scheduling Player '+str(i),'email':str(i)+'@example.com'} for i in range(8)]
  program=package['programs'][0];package['programs']=[{**program,'id':'PR'+str(i),'name':'Scheduling Program '+str(i)} for i in range(3)]
  package['entries']=[{'id':'E'+str(p)+str(i),'eventId':'EV001','programId':'PR'+str(p),'participantIds':['P'+str(i+(4 if p==1 else 0))],'name':'','seed':None,'status':'A'} for p in range(3) for i in range(4)]
  p=call('setup/preview?kind=trs&revision='+str(revision()),package);s=call('setup/confirm',{'revision':p['revision'],'token':p['token']})['data'];eid=s['events'][0]['id'];pids=[p['id'] for p in s['programs']];original={k:s[k] for k in ['participants','entries','programs','events']}
  for pid in pids:
   seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':revision(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},'PUT')
  rules={'name':'Schedule rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
  preset=call('presets/',{'revision':revision(),'preset':{'id':'','name':'Schedule rules','status':'A','isDefault':True,'rules':rules}},'PUT')['presets'][0]
  check(view()['eventCourts']==[1,2,3,4]) # Master courts need no Event Details selection.
  # No sessions or officials exist yet, but drawing and swaps work.
  generated=[]
  for pid in pids:
   p=draw(pid,{'format':'KO','groupCount':1,'advancePerGroup':1});check(all(m['start'] is None and m['court'] is None for m in p['matches']));check(not next(x for x in view()['plans'] if x['programId']==pid)['matches'])
   if pid==pids[0]:
    a,b=p['matches'][0]['entryA'],p['matches'][0]['entryB'];p=call('tournament/preview-swap',{'revision':p['revision'],'token':p['token'],'entryA':a,'entryB':b});check(all(m['start'] is None for m in p['matches']))
   confirm(p);generated.append(next(x for x in view()['plans'] if x['programId']==pid)['matches'])
  call('dashboard/pair',{'revision':revision(),'matchId':generated[0][0]['id'],'court':1,'umpireId':'missing'},expect=400)
  plan=view()['eventPlan'];plan.update(courts=[1],courtsConfigured=True,progression='A');call('tournament/event',{'revision':revision(),'plan':plan},'PUT')
  ids1=[m['id'] for rows in generated[:2] for m in rows];ids2=[m['id'] for m in generated[2]]
  body={'eventId':eid,'matchMinutes':30,'restMinutes':20,'sessions':[{'date':d,'start':'10:00:00','end':'12:00:00'} for d in [first,second]],'days':[{'date':first,'courts':[1,2,3,4],'matchIds':ids1},{'date':second,'courts':[1],'matchIds':ids2}]}
  def preview(body,expect=200):return call('scheduling/preview',{'revision':revision(),**body},expect=expect)
  def save(p,expect=200):return call('scheduling/confirm',{'revision':p['revision'],'token':p['token']},expect=expect)
  invalid=copy.deepcopy(body);invalid['sessions'][0]['end']='09:00:00';preview(invalid,400)
  invalid=copy.deepcopy(body);invalid['sessions'].append(invalid['sessions'][0]);preview(invalid,400)
  invalid=copy.deepcopy(body);invalid['days'][1]['matchIds'].append(ids1[0]);overlap=preview(invalid);check(len({m['id'] for m in overlap['matches']})==len(overlap['matches']))
  invalid=copy.deepcopy(body);invalid['days'][0]['matchIds']=['foreign'];preview(invalid,400)
  invalid=copy.deepcopy(body);invalid['days'][0]['matchIds']=[None];preview(invalid,400)
  invalid=copy.deepcopy(body);invalid['days'][0]['courts']=[99];preview(invalid,400)
  tooSmall=copy.deepcopy(body)
  for session in tooSmall['sessions']:session['end']='10:15:00'
  short=preview(tooSmall);check(len(short['unplaced'])==6 and not short['matches']);check(all(m['start'] is None for p in view()['plans'] for m in p['matches']))
  check(view()['eventCourts']==[1,2,3,4]) # Ignore legacy single-court event pool.
  call('dashboard/court',{'revision':revision(),'court':4,'closed':True});preview(body,400);call('dashboard/court',{'revision':revision(),'court':4,'closed':False})
  proposed=preview(body);check(len(proposed['matches'])==6 and not proposed['unplaced']);check(all(m['umpireId'] is None for m in proposed['matches']));save(proposed);save(proposed,400)
  call('setup/settings',{'revision':revision(),'timeZone':state()['timeZone'],'courtCount':3},'PUT',expect=400) # Saved day court references remain protected.
  # Stage allocation is locked to its selected date until an explicit whole-round move.
  yids=[m['id'] for m in generated[1]];moving=copy.deepcopy(body);moving['days'][0]['matchIds']=[id for id in ids1 if id not in yids];moving['days'][1]['matchIds']+=yids;moving['days'][1]['courts']=[1,2]
  preview(moving,400)
  invalid=copy.deepcopy(moving);invalid['moveMatchIds']=yids[:1];preview(invalid,400)
  moving['moveMatchIds']=yids
  invalid=copy.deepcopy(moving);invalid['matchMinutes']=240;preview(invalid,400)
  moved=preview(moving);check(len(moved['matches'])==2);check(all(m['start'].startswith(second) for m in moved['matches']));check(all(m['start'].startswith(first) for m in view()['plans'][1]['matches']));save(moved)
  body={k:v for k,v in moving.items() if k!='moveMatchIds'}
  prior={m['id']:(m['start'],m['end'],m['court']) for p in view()['plans'] for m in p['matches']}
  check(all(m['start'].startswith(first) for m in view()['plans'][0]['matches']));check(all(m['start'].startswith(second) for m in view()['plans'][2]['matches']))

  for m in view()['plans'][0]['matches']:call('tournament/result',{'revision':revision(),'matchId':m['id'],'presetId':preset['id'],'games':[{'a':5,'b':0}],'startedAt':m['start'],'finishedAt':m['end']},'PUT')
  invalid=copy.deepcopy(body);xids=[m['id'] for m in generated[0]];invalid['days'][0]['matchIds']=[];invalid['days'][1]['matchIds']+=xids;invalid['moveMatchIds']=xids;preview(invalid,400) # Played rounds cannot move.
  # Legacy A cannot block this program; other first rounds remain unfinished.
  p=draw(pids[0]);check(len(p['matches'])==1);confirm(p);final=view()['plans'][0]['matches'][-1];check(final['start'] is None)
  call('tournament/result',{'revision':revision(),'matchId':final['id'],'presetId':preset['id'],'games':[{'a':5,'b':0}]},'PUT',400)
  body['days'][1]['matchIds'].append(final['id']);p=preview(body);check(len(p['matches'])==1 and p['kept']==6);check(p['matches'][0]['start'].startswith(second));save(p)
  check(all((m['start'],m['end'],m['court'])==prior[m['id']] for p in view()['plans'] for m in p['matches'] if m['id'] in prior))
  check(all(m['status']=='SC' for m in view()['plans'][1]['matches']));check(view()['progress'][0]['waiting'] is None)
  p=preview(body);plan=view()['eventPlan'];call('tournament/event',{'revision':revision(),'plan':plan},'PUT');save(p,409)
  invalid=copy.deepcopy(body);invalid['sessions'][1]['end']='10:15:00';preview(invalid,400) # Existing slots are protected.
  saved=view();process.terminate();process.wait(timeout=10);process=None;start();check(view()==saved);check({k:state()[k] for k in original}==original)
 print(f'PASS: {checks} scheduling checks: separate draws, daily allocation, no-fit preview, legacy independent progression, preserved slots, stale tokens and SQL restart.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
