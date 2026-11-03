"""Qualifier preview swaps and BYE review against disposable SQL."""
import copy,json,socket,subprocess,time,urllib.request,urllib.error,itertools
from datetime import date,timedelta
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5223/api/';checks=0;process=None

def call(path,body=None,status=200,method=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1000]);checks+=1;return json.loads(raw)
def check(ok):
 global checks
 assert ok;checks+=1
def state():return call('setup/')['data']
def mutate(path,body,status=200,method=None):return call(path,{'revision':state()['revision'],**body},status,method)
def view():return call('tournament/'+eid)
socket.setdefaulttimeout(1)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5223))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  if process.poll() is not None:raise RuntimeError('Disposable test server exited before startup.')
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 package=test_inputs.package();package['events'][0].update(startDate=(date.today()+timedelta(days=1)).isoformat(),endDate=(date.today()+timedelta(days=2)).isoformat())
 person=package['participants'][0];program=package['programs'][0];package['programs']=[program]
 package['participants']=[{**person,'id':'P'+str(i),'name':'Qualifier '+str(i),'email':str(i)+'@example.com'} for i in range(6)]
 package['entries']=[{'id':'E'+str(i),'eventId':'EV001','programId':program['id'],'participantIds':['P'+str(i)],'status':'A'} for i in range(6)]
 preview=call('setup/preview?kind=trs&revision='+str(state()['revision']),package);s=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=s['events'][0]['id'];pid=s['programs'][0]['id']
 rules={'name':'Short','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 preset=mutate('presets/',{'preset':{'id':'','name':'Qualifier test','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
 plan=view()['eventPlan'];plan.update(dailySessions=[{'start':'09:00:00','end':'18:00:00'}],matchMinutes=5,restMinutes=0,officials=[{'id':'temp-'+str(i),'name':'Qualifier Umpire '+str(i)} for i in range(4)])
 mutate('tournament/event',{'plan':plan},method='PUT')
 entries=call('seeding/'+pid)['entries'];mutate('seeding/'+pid,{'mode':'N','seedCount':0,'seeds':{e['id']:None for e in entries}},method='PUT')
 preview=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[1,2,3,4],'earliestStart':package['events'][0]['startDate']+'T10:00:00','settings':{'format':'GK','groupCount':3,'advancePerGroup':2}})
 ids=[e['id'] for e in entries];preview=call('tournament/preview-swap',{'revision':preview['revision'],'token':preview['token'],'entryA':ids[0],'entryB':ids[1]})
 mutate('tournament/confirm',{'token':preview['token']})
 for m in view()['plans'][0]['matches']:mutate('tournament/result',{'matchId':m['id'],'matchVersion':m['matchVersion'],'presetId':preset,'games':[{'a':5,'b':0}]},method='PUT')
 saved=copy.deepcopy(view()['plans'][0])
 scheduled=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[1,2,3,4],'earliestStart':package['events'][0]['startDate']+'T14:00:00'})
 call('tournament/preview-swap',{'revision':scheduled['revision'],'token':scheduled['token'],'entryA':ids[0],'entryB':ids[1]},400)
 check(view()['plans'][0]==saved)
 preview=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True})
 revision=state()['revision'];stage=preview['matches'];origins=preview['view']['plans'][0]['qualificationOrigins'];check(len([m for m in stage if m['status']=='BY'])==2)
 groupof={id:g['id'] for g in saved['groups'] for id in g['entryIds']}
 def conflicts(a,b):
  swap=lambda id:b if id==a else a if id==b else id
  return sum(m['entryB'] is not None and groupof[swap(m['entryA'])]==groupof[swap(m['entryB'])] for m in stage)
 invalid=next((a,b) for a,b in itertools.combinations(origins,2) if conflicts(a,b)>0)
 call('tournament/preview-swap',{'revision':revision,'token':preview['token'],'entryA':invalid[0],'entryB':invalid[1]},400)
 bye=next(m['entryA'] for m in stage if m['status']=='BY')
 target=next(id for m in stage if m['entryB'] is not None for id in [m['entryA'],m['entryB']] if conflicts(bye,id)==0)
 swapped=call('tournament/preview-swap',{'revision':revision,'token':preview['token'],'entryA':bye,'entryB':target})
 check(state()['revision']==revision);check(view()['plans'][0]==saved)
 plan=swapped['view']['plans'][0]
 check(plan['qualificationOrigins']==origins);check(plan['groups']==saved['groups']);check([m for m in plan['matches'] if m['phase']=='G']==saved['matches'])
 check(any(m['status']=='BY' and m['entryA']==target and m['winnerId']==target for m in swapped['matches']))
 check(len(set(id for m in swapped['matches'] for id in [m['entryA'],m['entryB']] if id))==6)
 mutate('tournament/confirm',{'token':swapped['token']});confirmed=view()['plans'][0]
 check([m for m in confirmed['matches'] if m['phase']=='G']==saved['matches']);check(confirmed['qualificationOrigins']==origins)
 call('tournament/preview-swap',{'revision':state()['revision'],'token':swapped['token'],'entryA':bye,'entryB':target},400)
 check(any(m['entryA']==target and m['status']=='BY' for m in confirmed['matches']))
 # A later knockout round cannot expose the old group stage or accept qualifier swaps.
 for m in confirmed['matches']:
  if m['phase']=='K' and m['status']!='BY':
   mutate('tournament/schedule',{'matchId':m['id'],'court':1,'start':package['events'][0]['startDate']+'T15:'+str(confirmed['matches'].index(m)*5).zfill(2)+':00','umpireId':view()['eventPlan']['officials'][0]['id']},method='PUT')
   mutate('tournament/result',{'matchId':m['id'],'matchVersion':m['matchVersion'],'presetId':preset,'games':[{'a':5,'b':0}]},method='PUT')
 later=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True})
 call('tournament/preview-swap',{'revision':later['revision'],'token':later['token'],'entryA':bye,'entryB':target},400)
 print(f'PASS: {checks} qualifier/first-draw preview, BYE, preservation and validation checks.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
