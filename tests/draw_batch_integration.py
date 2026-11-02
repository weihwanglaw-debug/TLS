"""Batch draw reuse against disposable SQL; no local event data is modified."""
import copy,json,socket,subprocess,tempfile,time,urllib.error,urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import date,timedelta
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5205/api/';checks=0;process=None

def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1500]);checks+=1;return json.loads(raw)
def check(ok):
 global checks
 assert ok;checks+=1
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5205))!=0

db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-draw-batch-') as folder:
  log=open(Path(folder)/'server.log','w')
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5205'},stdout=log,stderr=log)
   for _ in range(150):
    try:return call('setup/')['data']
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Batch test API not ready')
  def stop():
   global process
   if process:process.terminate();process.wait(timeout=10);process=None
  def state():return call('setup/')['data']
  def mutate(path,body,status=200,method='POST'):return call(path,{'revision':state()['revision'],**body},status,method)
  def package(tag,offset,specs):
   p=test_inputs.package();p['sourceSystemId']='batch-'+tag;p['events'][0].update(name=tag,startDate=(date.today()+timedelta(days=offset)).isoformat(),endDate=(date.today()+timedelta(days=offset+1)).isoformat(),maxProgramsPerParticipant=None)
   template=p['participants'][0];program=p['programs'][0];p['programs']=[];p['participants']=[];p['entries']=[];number=0
   for i,(name,count,kind,sport,status) in enumerate(specs):
    pid='PR'+str(i);members=1 if kind=='S' else 2;p['programs'].append({**program,'id':pid,'name':name,'entryType':kind,'sport':sport,'status':status,'minPlayers':members,'maxPlayers':members,'maxCapacity':128})
    for j in range(count):
     ids=[]
     for k in range(members):
      person='P'+str(number);number+=1;ids.append(person);p['participants'].append({**template,'id':person,'name':name+' Player '+str(j)+'-'+str(k),'email':person+'@example.com'})
     p['entries'].append({'id':'E'+str(i)+'-'+str(j),'eventId':'EV001','programId':pid,'participantIds':ids,'seed':None,'status':status})
   return p
  def import_event(p):
   draft=call('setup/preview?kind=trs&revision='+str(state()['revision']),p);data=call('setup/confirm',{'revision':draft['revision'],'token':draft['token']})['data'];eid=next(e['id'] for e in data['events'] if e['name']==p['events'][0]['name']);return eid,{x['name']:x['id'] for x in data['programs'] if x['eventId']==eid}
  def review(pid,seeded=False):
   entries=[e for e in state()['entries'] if e['programId']==pid];seeds={e['id']:i+1 if seeded and i<2 else None for i,e in enumerate(entries)}
   return mutate('seeding/'+pid,{'mode':'M' if seeded else 'N','seedCount':2 if seeded else 0,'seeds':seeds},method='PUT')
  def draw(eid,pid,fmt='GK'):
   draft=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':fmt,'groupCount':4,'advancePerGroup':2}});return mutate('tournament/confirm',{'token':draft['token']})
  def view(eid):return call('tournament/'+eid)
  def plans(eid):return {p['programId']:p for p in view(eid)['plans']}
  def listing(eid,source):return call('tournament/'+eid+'/apply-draw-settings?sourceProgramId='+source)
  try:
   start();eid,ids=import_event(package('Batch eligibility',10,[('Source',16,'S','Badminton','A'),('Manual',8,'S','Badminton','A'),('No seeds',12,'D','Badminton','A'),('Unreviewed',8,'S','Badminton','A'),('Existing',8,'S','Badminton','A'),('Small',6,'S','Badminton','A'),('Team',8,'T','Basketball','A'),('Inactive',8,'S','Badminton','X')]))
   rules={'name':'Batch default','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
   preset=mutate('presets/',{'preset':{'id':'','name':'Batch default','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]
   source=ids['Source'];review(source,True)
   mutate('tournament/apply-draw-settings',{'eventId':eid,'sourceProgramId':source,'programIds':[ids['Manual']]},400)
   draw(eid,source)
   for name in ['Manual','No seeds','Existing','Small']:review(ids[name],name=='Manual')
   draw(eid,ids['Existing'],'KO');before=state();choices=listing(eid,source);check(state()==before)
   reasons={p['name']:p['reason'] for p in choices['programs']};check(reasons['Manual']==reasons['No seeds']=='');check(reasons['Unreviewed']=='Seeding not reviewed');check(reasons['Existing']=='Draw already exists');check(reasons['Team']=='Unsupported sport/format');check(reasons['Inactive']=='Program inactive');check(reasons['Small'].startswith('Incompatible settings:'));check(all(p['id']!=source for p in choices['programs']))
   def apply(targets,status=200,revision=None):return call('tournament/apply-draw-settings',{'revision':state()['revision'] if revision is None else revision,'eventId':eid,'sourceProgramId':source,'programIds':targets},status)
   for bad in [[],[source],[ids['Manual'],ids['Manual']],['missing'],[ids['Manual'],ids['Small']],[ids['Manual'],ids['Unreviewed']],[ids['Manual'],ids['Existing']],[ids['Team']],[ids['Inactive']]]:
    before=state();apply(bad,400);check(state()==before)
   foreign,foreignids=import_event(package('Batch foreign event',15,[('Foreign',4,'S','Badminton','A')]))
   apply([foreignids['Foreign']],400);call('tournament/'+foreign+'/apply-draw-settings?sourceProgramId='+source,status=400)
   # Both stale source/master edits and changed seeding are guarded by the stored revision.
   old=choices['revision'];review(ids['Manual'],True);apply([ids['Manual']],409,old)
   off={**preset,'isDefault':False};mutate('presets/',{'preset':off},method='PUT');check(all(p['reason'] for p in listing(eid,source)['programs']));apply([ids['Manual']],400);mutate('presets/',{'preset':preset},method='PUT')
   prior=plans(eid);snap=state();request={'revision':snap['revision'],'eventId':eid,'sourceProgramId':source,'programIds':[ids['Manual'],ids['No seeds']]}
   def commit():
    req=urllib.request.Request(base+'tournament/apply-draw-settings',data=json.dumps(request).encode(),headers={'Content-Type':'application/json'})
    try:
     with urllib.request.urlopen(req,timeout=30) as r:return r.status
    except urllib.error.HTTPError as e:return e.code
   with ThreadPoolExecutor(max_workers=2) as pool:check(sorted(pool.map(lambda _:commit(),range(2)))==[200,409])
   after=plans(eid);saved=state();check(saved['revision']==snap['revision']+1);check(after[source]==prior[source]);check(after[ids['Existing']]==prior[ids['Existing']])
   for name in ['Manual','No seeds']:
    plan=after[ids[name]];check(plan['format']=='GK' and plan['groupCount']==4 and plan['advancePerGroup']==2);check(plan['seeds']==prior[ids[name]]['seeds'] and plan['seedCount']==prior[ids[name]]['seedCount'] and plan['seedingMode']==prior[ids[name]]['seedingMode']);check(all(m['start'] is None and m['court'] is None and m['presetId']==preset['id'] for m in plan['matches']));own={e['id'] for e in saved['entries'] if e['programId']==ids[name]};check(all(m['entryA'] in own and (m['entryB'] is None or m['entryB'] in own) for m in plan['matches']))
   check(all(saved[k]==snap[k] for k in ['participants','entries','programs','events']));numbers=[m['number'] for p in after.values() for m in p['matches']];check(len(numbers)==len(set(numbers)));check(all(not after[ids[n]]['matches'] for n in ['Unreviewed','Small','Team','Inactive']));check(not view(eid)['eventPlan']['scheduleDays']);apply(request['programIds'],409,request['revision'])
   old=plans(eid);stop();start();check(plans(eid)==old);check(all(p['reason']=='Draw already exists' for p in listing(eid,source)['programs'] if p['name'] in ['Manual','No seeds']))
   # The aggregate cap must roll back, even after an earlier target was generated in memory.
   cap,capids=import_event(package('Batch capacity',20,[('RR Source',8,'S','Badminton','A'),('Large A',45,'S','Badminton','A'),('Large B',45,'S','Badminton','A'),('Large C',45,'S','Badminton','A')]))
   for pid in capids.values():review(pid)
   draw(cap,capids['RR Source'],'RR');before=state();mutate('tournament/apply-draw-settings',{'eventId':cap,'sourceProgramId':capids['RR Source'],'programIds':[capids[n] for n in ['Large A','Large B','Large C']]},400);check(state()==before)
   result=mutate('tournament/apply-draw-settings',{'eventId':cap,'sourceProgramId':capids['RR Source'],'programIds':[capids[n] for n in ['Large A','Large B']]});check(result['count']==2);check(sum(len(p['matches']) for p in plans(cap).values())==2008)
   print(f'PASS: {checks} batch draw checks; eligibility, own seeds/entries, one-step atomic generation, stale/concurrent requests, capacity rollback and restart.')
  finally:stop();log.close()
finally:db.close()
