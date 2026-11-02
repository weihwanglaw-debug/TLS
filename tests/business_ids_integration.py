"""Readable IDs and approved-cleanup behaviour against disposable SQL only."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error
from concurrent.futures import ThreadPoolExecutor
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
from sba_inputs import workbook
import test_inputs

base='http://localhost:5212/api/';checks=0;process=None
def check(value):
 global checks
 assert value;checks+=1
def call(path,body=None,status=200,method=None,headers=None):
 global checks
 raw=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
 req=urllib.request.Request(base+path,data=raw,method=method or ('POST' if body is not None else 'GET'),headers=headers or {'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=45) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 if status is not None:assert code==status,(path,code,status,raw[:1000])
 checks+=1;return (code,json.loads(raw)) if status is None else json.loads(raw)
def state():return call('setup/')['data']
def rev():return state()['revision']
def preview(p,replace=None):return call('setup/preview?kind=trs&revision='+str(rev())+('&replaceEventId='+replace if replace else ''),p)
def confirm(p,status=200):return call('setup/confirm',{'revision':p['revision'],'token':p['token']},status)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5212))!=0
db=TestDatabase()
try:
 def start():
  global process
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(120):
   try:return state()
   except urllib.error.URLError:time.sleep(.1)
  raise RuntimeError('ID test API did not start')
 def stop():
  global process
  if process:process.terminate();process.wait(timeout=10);process=None
 db.command('--upgrade-business-ids','true');db.command('--upgrade-business-ids','true');start()
 p=test_inputs.package();p['events'][0]['maxProgramsPerParticipant']=None
 before=state();draft=preview(p);check(state()==before)
 s=confirm(draft)['data'];eid=s['events'][0]['id'];check(eid=='EV000001')
 check([x['id'] for x in s['programs']]==[eid+'-P001',eid+'-P002'])
 check([x['id'] for x in s['participants']]==[eid+'-U'+str(i).zfill(5) for i in range(1,5)])
 check(all(x['id'].startswith(x['programId']+'-E') for x in s['entries']))
 oldpeople={x['name']:x['id'] for x in s['participants']};oldprograms={x['name']:x['id'] for x in s['programs']}
 edited=copy.deepcopy(p);edited['programs'].reverse();edited['participants'].reverse();edited['entries'].reverse();edited['events'][0]['name']='Readable ID event renamed'
 s=confirm(preview(edited,eid))['data'];check({x['name']:x['id'] for x in s['participants']}==oldpeople);check({x['name']:x['id'] for x in s['programs']}==oldprograms)
 edited['programs'].append({**p['programs'][0],'id':'PR003','name':'Third program'})
 edited['participants'].append({**p['participants'][0],'id':'P005','name':'New Player','email':'new@example.com','mobile':'+6581234500'})
 edited['entries'] += [{'id':'EN005','eventId':'EV001','programId':'PR003','participantIds':['P003'],'name':'','seed':None,'status':'A'},{'id':'EN006','eventId':'EV001','programId':'PR003','participantIds':['P004'],'name':'','seed':None,'status':'A'},{'id':'EN007','eventId':'EV001','programId':'PR001','participantIds':['P005'],'name':'','seed':None,'status':'A'}]
 s=confirm(preview(edited,eid))['data'];check(s['programs'][-1]['id']==eid+'-P003');check(s['participants'][-1]['id']==eid+'-U00005')
 # Independent previews may propose the same number, but only one revision commits.
 def another(number):
  q=copy.deepcopy(p);source='SRC'+str(number);q['events'][0].update(id=source,name='Concurrent event '+str(number),startDate=(date.today()+timedelta(days=number*20)).isoformat(),endDate=(date.today()+timedelta(days=number*20+1)).isoformat())
  for key in ['programs','participants','entries']:
   for row in q[key]:row['eventId']=source
  return q
 q2,q3=another(2),another(3);d2,d3=preview(q2),preview(q3)
 def attempt(d):return call('setup/confirm',{'revision':d['revision'],'token':d['token']},None)
 with ThreadPoolExecutor(2) as pool:results=list(pool.map(attempt,[d2,d3]))
 check(sorted(code for code,_ in results)==[200,409]);loser=q2 if results[0][0]==409 else q3;confirm(preview(loser));check(len({e['id'] for e in state()['events']})==3)
 rules={'name':'Readable rules','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 preset=call('presets/',{'revision':rev(),'preset':{'id':'','name':'Readable rules','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0];check(preset['id']=='GP0001')
 pid=oldprograms['Test Singles']
 def view():return call('tournament/'+eid)
 plan=view()['eventPlan'];plan['officials']=[{'id':'temporary-a','name':'Official A'},{'id':'temporary-b','name':'Official B'}]
 plan=call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')['eventPlan'];check([o['id'] for o in plan['officials']]==[eid+'-O001',eid+'-O002'])
 plan['officials']=[plan['officials'][1],{'id':'temporary-c','name':'Official C'}]
 plan=call('tournament/event',{'revision':rev(),'plan':plan},method='PUT')['eventPlan'];check([o['id'] for o in plan['officials']]==[eid+'-O002',eid+'-O003'])
 seed=call('seeding/'+pid);call('seeding/'+pid,{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT')
 def draw():return call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,'settings':{'format':'RR','groupCount':1,'advancePerGroup':1}})
 d=draw();first={m['id'] for m in d['matches']};check(first=={pid+'-M0001',pid+'-M0002',pid+'-M0003'});group=d['groups'][0]['groups'][0]['id'];check(group==pid+'-G001')
 call('tournament/confirm',{'revision':d['revision'],'token':d['token']});call('tournament/reset',{'revision':rev(),'programId':pid})
 d=draw();check(not first.intersection(m['id'] for m in d['matches']));check(d['groups'][0]['groups'][0]['id']==pid+'-G002');call('tournament/confirm',{'revision':d['revision'],'token':d['token']})
 snapshot=view();stop();start();check(view()==snapshot)
 # Seed non-empty master rankings/clubs, and confirm cleanup preserves their exact values internally.
 boundary='TLS-ID-MASTER';body=('--'+boundary+'\r\nContent-Disposition: form-data; name="file"; filename="rankings.xlsx"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+workbook()+('\r\n--'+boundary+'--\r\n').encode()
 call('sba/import',body,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
 call('setup/settings',{'revision':rev(),'timeZone':'Asia/Singapore','appName':'TLS ID verification','courtCount':6},method='PUT')
 oldevents={e['id'] for e in state()['events']};oldpresets=state()['venue']['presets'];db._sql('legacy-preset-id');stop()
 cleared=db.command('--clear-test-records','true');summary=json.loads(next(line for line in cleared.stdout.splitlines() if line.startswith('{')));check(summary['masterDataPreserved'] and summary['rankings']>0 and summary['clubs']>0)
 s=start();check(all(not s[key] for key in ['events','programs','participants','entries','imports','programPlans','eventPlans']));check(not s['venue']['bindings']);check([{k:v for k,v in row.items() if k!='id'} for row in s['venue']['presets']]==[{k:v for k,v in row.items() if k!='id'} for row in oldpresets]);check(s['venue']['presets'][0]['id']=='GP0002');check(s['appName']=='TLS ID verification' and s['courtCount']==6);check(int(db._sql('audit'))==1)
 reimport=confirm(preview(p))['data'];newid=reimport['events'][0]['id'];check(newid not in oldevents);check(int(newid[2:])>max(int(e[2:]) for e in oldevents))
 check(all(x['programId'].startswith(newid+'-P') for x in reimport['entries']))
 newpreset=call('presets/',{'revision':rev(),'preset':{'id':'','name':'Next rules','status':'A','isDefault':False,'rules':rules}},method='PUT')['presets'][-1];check(newpreset['id']=='GP0003')
 print(f'PASS: {checks} readable-ID checks: import mapping, replacements, concurrent commits, groups/matches after reset, officials, restart, master-preserving cleanup and counter retention.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
