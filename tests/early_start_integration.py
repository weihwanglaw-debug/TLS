"""Early start confirmation, unchanged timetable and console scoresheet on a disposable database."""
import copy,json,socket,subprocess,time,uuid,urllib.request,urllib.error
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime,timedelta,timezone
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5216/api/';checks=0;process=None;latencies=[]
socket.setdefaulttimeout(1)
def call(path,body=None,status=200,token=None,method=None,measure=False):
 global checks
 request=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 started=time.perf_counter()
 try:
  with urllib.request.urlopen(request,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 if measure:latencies.append((time.perf_counter()-started)*1000)
 assert code==status,(path,code,status,raw[:1500]);checks+=1;return json.loads(raw) if raw else None
def check(value):
 global checks
 assert value;checks+=1
def state():return call('setup/')['data']
def rev():return state()['revision']
def view():return call('tournament/'+eid)
def matches():return [m for p in view()['plans'] for m in p['matches']]
def latest(m):return next(x for x in matches() if x['id']==m['id'])
def now():return datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0)
def draw(pid):
 p=call('tournament/preview',{'revision':rev(),'eventId':eid,'programIds':[pid],'courts':[],'drawOnly':True,**({'settings':{'format':'KO','groupCount':1,'advancePerGroup':1}} if not any(m['programId']==pid for m in matches()) else {})})
 return call('tournament/confirm',{'revision':p['revision'],'token':p['token']})
def official(m,kind,winner,games,handling='U',expect=200):
 m=latest(m);body={'revision':rev(),'matchId':m['id'],'matchVersion':m['matchVersion'],'games':games,'outcome':kind,'officialWinner':winner,'reason':'Practical test referee correction'}
 impact=call('tournament/result-impact',body)
 result=call('tournament/result',{**body,'impactToken':impact['token'],'downstreamHandling':handling},expect,method='PUT')
 return impact,result
def pairing(m,court,official_id,exception=None):
 body={'revision':rev(),'matchId':m['id'],'court':court,'umpireId':official_id,'presetId':preset,'courtOverrideReason':exception}
 return call('dashboard/pair',body)
def join(m,court,official_id,device):
 invitation=pairing(m,court,official_id)
 return call('tablet/pair',{'code':invitation['code'],'deviceId':device,'deviceName':device})['token']
def command(token,action,side=None,status=200,**extra):
 m=call('tablet/state',token=token)['match'];body={'version':m['version'],'operationId':str(uuid.uuid4()),'action':action,**extra}
 if side:body['side']=side
 return call('tablet/command',body,status,token)
def serving(m,left='A'):
 return {'serverSide':'A','serverId':next(p['id'] for p in m['players'] if p['side']=='A'),'receiverId':next(p['id'] for p in m['players'] if p['side']=='B'),'leftSide':left}
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5216))!=0
db=TestDatabase()
try:
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(120):
  try:state();break
  except urllib.error.URLError:time.sleep(.1)
 package=test_inputs.package();package['events'][0].update(startDate=now().date().isoformat(),endDate=(now().date()+timedelta(days=1)).isoformat(),maxProgramsPerParticipant=None)
 person=package['participants'][0];program=package['programs'][0]
 package['participants']=[{**person,'id':'P'+str(i),'name':'Court Test Player '+str(i),'email':str(i)+'@example.com'} for i in range(16)]
 package['programs']=[{**program,'id':'PR'+str(i),'name':'Court Program '+str(i)} for i in range(4)]
 package['entries']=[{'id':'E'+str(i),'eventId':'EV001','programId':'PR'+str(i//4),'participantIds':['P'+str(i)],'status':'A'} for i in range(16)]
 imported=call('setup/preview?kind=trs&revision='+str(rev()),package);s=call('setup/confirm',{'revision':imported['revision'],'token':imported['token']})['data'];eid=s['events'][0]['id']
 rules={'name':'21-point test','gamesToWin':2,'pointsToWin':21,'winBy':2,'pointCap':30,'intervalAt':11,'intervalSeconds':0,'betweenGamesSeconds':0,'decidingEndsAt':11}
 preset=call('presets/',{'revision':rev(),'preset':{'id':'','name':'21-point test','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]['id']
 plan=view()['eventPlan'];plan.update(matchMinutes=5,restMinutes=0,officials=[{'id':'temporary-'+str(i),'name':'Umpire '+str(i)} for i in range(4)])
 call('tournament/event',{'revision':rev(),'plan':plan},method='PUT');officials=view()['eventPlan']['officials']
 for p in s['programs']:
  seed=call('seeding/'+p['id']);call('seeding/'+p['id'],{'revision':rev(),'mode':'N','seedCount':0,'seeds':{e['id']:None for e in seed['entries']}},method='PUT');draw(p['id'])
 start=now()+timedelta(minutes=30);end=start+timedelta(minutes=60)
 window={'date':start.date().isoformat(),'start':start.time().isoformat(),'end':end.time().isoformat(),'courts':[1,2,3,4],'matchIds':[m['id'] for m in matches()],'programLimits':{p['id']:2 for p in s['programs']}}
 call('scheduling/draft',{'revision':rev(),'eventId':eid,'sessions':[window],'sessionIndex':0,'dirtySessions':[],'moveMatchIds':[]})
 check(view()['eventPlan']['planningDraft']['sessions']==[window]);check(all(m['start'] is None for m in matches()))
 p=call('scheduling/preview',{'revision':rev(),'eventId':eid,'sessions':[window],'matchMinutes':5,'restMinutes':0,'days':[{'date':window['date'],'courts':window['courts'],'matchIds':window['matchIds']}]})
 check(len(p['matches'])==8);call('scheduling/confirm',{'revision':p['revision'],'token':p['token']});check(view()['eventPlan']['planningDraft'] is None)
 # Dashboard and assignment must agree during a different active court allocation.
 saved_plan=copy.deepcopy(view()['eventPlan']);restricted=copy.deepcopy(saved_plan)
 restricted['dailySessions']=[]
 restricted['sessions'].insert(0,{'date':now().date().isoformat(),'start':(now()-timedelta(minutes=5)).time().isoformat(),'end':(now()+timedelta(minutes=10)).time().isoformat(),'courts':[2],'matchIds':[]})
 call('tournament/event',{'revision':rev(),'plan':restricted},method='PUT')
 queued=next(m for m in matches() if m['court']==1)
 dashboard=call('dashboard/?eventId='+eid)
 check(next(q for q in dashboard['queue'] if q['match']['id']==queued['id'])['eligibleCourts']==[2])
 before_revision=rev()
 rejection=call('dashboard/pair',{'revision':before_revision,'matchId':queued['id'],'court':1,'umpireId':officials[0]['id'],'presetId':preset},status=400)
 check('Schedule' in rejection['message']);check(rev()==before_revision)
 invitation=pairing(queued,2,officials[0]['id'])
 check(invitation['match']['court']==2)
 # Restoring the planned allocation permits Court 1 without moving other matches.
 call('tournament/event',{'revision':rev(),'plan':saved_plan},method='PUT')
 invitation=pairing(queued,1,officials[0]['id'])
 check(invitation['match']['court']==1)
 check(next(q for q in call('dashboard/?eventId='+eid)['queue'] if q['match']['id']==queued['id'])['eligibleCourts']==[1,2,3,4])
 m=next(m for m in matches() if m['court']==1);before={m['id']:(m['start'],m['end']) for m in matches()}
 token=join(m,1,officials[0]['id'],'early-start-tablet')
 tablet=call('tablet/state',token=token)['match'];check(29<=tablet['earlyStartMinutes']<=30)
 setup=serving(tablet)
 rejected=command(token,'START',status=400,serving=setup);check('earlier' in rejected['message'])
 check(latest(m)['status']=='SC');check(call('dashboard/scoresheet/'+m['id'])['startedAt'] is None)
 body={'version':tablet['version'],'operationId':str(uuid.uuid4()),'action':'START','serving':setup,'confirmEarlyStart':True}
 started=call('tablet/command',body,token=token)['match'];check(started['status']=='IP');check(started['startedAt'] is not None)
 # Spectator display settings persist through the shared master settings API.
 display_config={'columns':4,'rows':4,'pageSeconds':7,'autoRotate':True,'eventId':eid,'courts':[1]}
 call('setup/settings',{'revision':rev(),'timeZone':state()['timeZone'],'liveDisplay':display_config},method='PUT')
 check(state()['liveDisplay']==display_config)
 screen=call('display');check(screen['config']==display_config);check(len(screen['matches'])==1)
 shown=screen['matches'][0];check(shown['id']==m['id']);check(shown['nameA']==started['nameA']);check(shown['scoreA']==started['scoreA'])
 check(not any(key in json.dumps(screen).lower() for key in ['token','codehash','deviceip','umpirename','mobile','email']))
 for patch in [{'columns':0},{'rows':7},{'pageSeconds':2},{'courts':[1,1]},{'eventId':'missing-event'}]:
  revision=rev();call('setup/settings',{'revision':revision,'timeZone':state()['timeZone'],'liveDisplay':{**display_config,**patch}},method='PUT',status=400);check(rev()==revision)
 call('setup/settings',{'revision':rev(),'timeZone':state()['timeZone'],'liveDisplay':{**display_config,'courts':[2]}},method='PUT')
 check(call('display')['matches']==[])
 check(started['plannedStart']==before[m['id']][0]);check(before=={m['id']:(m['start'],m['end']) for m in matches()})
 repeated=call('tablet/command',body,token=token)['match'];check(repeated['startedAt']==started['startedAt']);check(repeated['version']==started['version'])
 sheet=call('dashboard/scoresheet/'+m['id']);check(sheet['startedAt']==started['startedAt']);check(sheet['players']==started['players'])
 other=next(x for x in matches() if x['id']!=m['id'] and x['court']==1)
 call('dashboard/pair',{'revision':rev(),'matchId':other['id'],'court':1,'umpireId':officials[0]['id'],'presetId':preset},status=400)
 call('dashboard/scoresheet/not-found',status=404)
 print(f'PASS: {checks} early-start and console scoresheet checks; planned schedule unchanged.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
