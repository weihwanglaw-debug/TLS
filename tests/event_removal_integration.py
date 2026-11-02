"""Event removal uses a disposable SQL database, never the local TLS records."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error,tempfile
from sql_test_database import TestDatabase,project,dll
import test_inputs
from datetime import date,timedelta
base='http://localhost:5213/api/';checks=0;process=None;log=tempfile.TemporaryFile(mode='w+',encoding='utf-8')

def check(value):
 global checks
 assert value;checks+=1

def call(path,body=None,status=200,method=None):
 global checks
 raw=json.dumps(body).encode() if body is not None else None
 req=urllib.request.Request(base+path,data=raw,method=method or ('POST' if raw is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1500]);checks+=1
 return json.loads(raw)

def state():return call('setup/')['data']
def rev():return state()['revision']
def import_event(package):
 d=call('setup/preview?kind=trs&revision='+str(rev()),package)
 return call('setup/confirm',{'revision':d['revision'],'token':d['token']})['data']
def start():
 global process
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=log,stderr=subprocess.STDOUT)
 for _ in range(120):
  if process.poll() is not None:raise RuntimeError('Removal test API exited')
  try:return state()
  except urllib.error.URLError:time.sleep(.1)
 raise RuntimeError('Removal test API did not start')
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5213))!=0

db=TestDatabase()
try:
 db.command('--upgrade-business-ids','true');start()
 p=test_inputs.package();p['events'][0]['maxProgramsPerParticipant']=None
 s=import_event(p);eid=s['events'][0]['id'];pid=s['programs'][0]['id']
 q=copy.deepcopy(p);q['sourceSystemId']='second-source';q['events'][0].update(name='Other Event',startDate=(date.today()+timedelta(days=30)).isoformat(),endDate=(date.today()+timedelta(days=31)).isoformat());other=import_event(q)['events'][-1]['id']
 before=state();check(call('setup/events/'+eid+'/removal')['programs']==2);check(state()==before)
 call('setup/events/missing/removal',status=404)
 call('setup/events/'+eid,method='DELETE',status=400)
 call('setup/events/'+eid+'?revision=0',method='DELETE',status=409);check(state()==before)
 rules={'name':'Removal rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 call('presets/',{'revision':rev(),'preset':{'id':'','name':'Removal rules','status':'A','isDefault':True,'rules':rules}},method='PUT')
 seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
 checked=call('setup/events/'+eid+'/removal')
 d=call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'RR','groupCount':1,'advancePerGroup':1}})
 # An unconfirmed preview is not a generated fixture; checking is read only.
 call('setup/events/'+eid+'/removal');call('tournament/confirm',{'revision':d['revision'],'token':d['token']})
 before=state()
 error=call('setup/events/'+eid+'/removal',status=409);check('fixtures have been generated' in error['message'])
 call('setup/events/'+eid+'?revision='+str(checked['revision']),method='DELETE',status=409)
 call('setup/events/'+eid+'?revision='+str(rev()),method='DELETE',status=409);check(state()==before)
 # Remove only the fixture-free event and retain the other event and masters.
 summary=call('setup/events/'+other+'/removal');audits=int(db._sql('audit').strip())
 s=call('setup/events/'+other+'?revision='+str(summary['revision']),method='DELETE')['data']
 check(s['events']==[e for e in before['events'] if e['id']!=other]);check(s['programPlans']==before['programPlans'])
 for key in ['programs','participants','entries','imports','eventPlans']:
  check(s[key]==[x for x in before[key] if x['eventId']!=other])
 for key in ['venue','courtCount','timeZone','appName','logoLightUrl','logoDarkUrl']:check(s[key]==before[key])
 check(int(db._sql('audit').strip())==audits+1)
 call('setup/events/'+other+'/removal',status=404)
 call('setup/events/'+other+'?revision='+str(rev()),method='DELETE',status=404)
 # Removed source mappings no longer revive old IDs; counters remain committed.
 s=import_event(q);new=s['events'][-1]['id'];check(new!=other);check(int(new[2:])>int(other[2:]))
 process.terminate();process.wait(timeout=10);process=None;check(start()==s)
 print(f'PASS: {checks} event removal checks: read-only check, fixture/stale guards, event isolation, masters, audit, reimport IDs and restart.')
except:
 log.seek(0);print(log.read()[-6500:]);raise
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close();log.close()
