"""Event-scoped dashboard checks against disposable SQL only."""
import json,os,socket,subprocess,tempfile,time,urllib.request,urllib.error,uuid
from datetime import datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs

base='http://localhost:5191/api/';checks=0;process=None
def call(path,body=None,method=None,status=200,token=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as error:code=error.code;raw=error.read()
 assert code==status,(path,code,status,raw[:1000]);checks+=1
 return json.loads(raw)
def dash(eid=None):return call('dashboard/'+('?eventId='+eid if eid else ''))
def mutate(path,body,method='PUT',status=200):return call(path,{'revision':dash()['revision'],**body},method,status)
def check(value):
 global checks
 assert value;checks+=1
def rollover(eid,first,last):
 # Simulate passage of calendar time in the disposable database only; never relax TRS transfer checks.
 script=r"""
$ErrorActionPreference='Stop'
if($env:TLS_TEST_NAME -notmatch '^TLS_Test_[a-f0-9]{32}$'){throw 'Unsafe test database'}
$cb=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($env:TLS_TEST_BASE);$cb['Initial Catalog']=$env:TLS_TEST_NAME
$cn=New-Object System.Data.SqlClient.SqlConnection($cb.ConnectionString);$cn.Open()
try{$cmd=$cn.CreateCommand();$cmd.CommandText='UPDATE Events SET StartDate=@first,EndDate=@last WHERE Id=@id';[void]$cmd.Parameters.AddWithValue('@id',$env:TLS_TEST_EVENT);[void]$cmd.Parameters.AddWithValue('@first',$env:TLS_TEST_FIRST);[void]$cmd.Parameters.AddWithValue('@last',$env:TLS_TEST_LAST);if($cmd.ExecuteNonQuery() -ne 1){throw 'Test event missing'}}finally{$cn.Dispose()}
"""
 result=subprocess.run(['powershell','-NoProfile','-Command',script],env={**db.control,'TLS_TEST_EVENT':eid,'TLS_TEST_FIRST':str(first),'TLS_TEST_LAST':str(last)},capture_output=True,text=True)
 if result.returncode:raise RuntimeError('Disposable event rollover failed; credentials withheld.')
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5191))!=0
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-dashboard-events-') as folder:
  with open(Path(folder)/'server.log','w') as log:
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5]},stdout=log,stderr=log)
   try:
    for _ in range(150):
     try:dash();break
     except urllib.error.URLError:time.sleep(.1)
    now=datetime.now(timezone(timedelta(hours=8))).replace(tzinfo=None,microsecond=0);today=now.date()
    def import_event(name,first,last):
     package=test_inputs.package();package['sourceSystemId']='dashboard-'+name
     package['events'][0].update(name=name,startDate=str(first),endDate=str(last))
     for i in [3,4]:package['entries'].append({'id':'EX'+str(i),'eventId':'EV001','programId':'PR001','participantIds':['P00'+str(i)],'status':'A'})
     preview=call('setup/preview?kind=trs&revision='+str(dash()['revision']),package)
     saved=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data']
     eid=next(e['id'] for e in saved['events'] if e['name']==name)
     return eid,[p['id'] for p in saved['programs'] if p['eventId']==eid]
    eid,pids=import_event('Ongoing day two',today-timedelta(days=1),today+timedelta(days=1))
    future,_=import_event('Upcoming',today+timedelta(days=2),today+timedelta(days=3))
    past,_=import_event('Past unfinished',today+timedelta(days=5),today+timedelta(days=6))
    rollover(past,today-timedelta(days=3),today-timedelta(days=2))
    check(dash(future)['eventContext']['calendar']=='U')
    check(dash(past)['eventContext']['calendar']=='C')
    ctx=dash(eid)['eventContext'];check(ctx['day']==2 and ctx['missingFixtures']==2 and ctx['missingSessions'] and not ctx['missingCourts'] and ctx['missingOfficials'] and ctx['missingPresets'])
    call('dashboard/?eventId=missing-event',status=400)
    plan={'eventId':eid,'courts':[1,2],'courtsConfigured':True,'dailySessions':[{'start':'00:00:00','end':'23:59:59'}],'officials':[{'id':'one','name':'Official One'},{'id':'two','name':'Official Two'}],'matchMinutes':10,'restMinutes':0}
    mutate('tournament/event',{'plan':plan});plan['officials']=call('tournament/'+eid)['eventPlan']['officials']
    check(dash(eid)['eventContext']['sessionState']=='I')
    check([c['number'] for c in dash(eid)['courts']]==[1,2,3,4])
    for court in [1,2,3,4]:mutate('dashboard/court',{'court':court,'closed':True},'POST')
    check(dash(eid)['eventContext']['allCourtsClosed'])
    for court in [1,2,3,4]:mutate('dashboard/court',{'court':court,'closed':False},'POST')
    # No fixtures yet: daily windows can safely be changed through the real API.
    if now.hour>=1 and now.hour<=22:
     before=(now-timedelta(minutes=20)).strftime('%H:%M:%S');before_end=(now-timedelta(minutes=10)).strftime('%H:%M:%S')
     after=(now+timedelta(minutes=10)).strftime('%H:%M:%S');after_end=(now+timedelta(minutes=20)).strftime('%H:%M:%S')
     for windows,expected in [([{'start':after,'end':after_end}],'B'),([{'start':before,'end':before_end},{'start':after,'end':after_end}],'G'),([{'start':before,'end':before_end}],'F')]:
      mutate('tournament/event',{'plan':{**plan,'dailySessions':windows}})
      check(dash(eid)['eventContext']['sessionState']==expected)
    mutate('tournament/event',{'plan':plan});plan['officials']=call('tournament/'+eid)['eventPlan']['officials']
    rules={'name':'Short tests','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3,'cardsEnabled':True,'challengesEnabled':False,'countShuttles':True}
    preset=mutate('presets/',{'preset':{'id':'','name':'Dashboard tests','rules':rules,'status':'A','isDefault':True}})['presets'][0]['id']
    def generate(event,program):
     existing=next(p for p in call('tournament/'+event)['plans'] if p['programId']==program)
     options={} if existing['matches'] else {'settings':{'format':'KO','groupCount':1,'advancePerGroup':1},'seeds':{e['id']:None for e in call('setup/')['data']['entries'] if e['programId']==program and e['status']=='A'}}
     draft=mutate('tournament/preview',{'eventId':event,'programIds':[program],'courts':[1,2],'earliestStart':str(today)+'T00:00:00',**options},'POST')
     mutate('tournament/confirm',{'token':draft['token']},'POST')
    generate(eid,pids[0]);check(dash(eid)['eventContext']['missingFixtures']==1)
    own=call('tournament/'+eid)
    for match in own['plans'][0]['matches']:
     if match['status']=='SC':mutate('tournament/result',{'matchId':match['id'],'matchVersion':match['matchVersion'],'games':[{'a':5,'b':0}],'presetId':preset,'startedAt':match['start'],'finishedAt':match['end']})
    progress=next(p for p in dash(eid)['programs'] if p['id']==pids[0]);check(progress['ready'] and progress['remaining']==0)
    check(next(p for p in dash(eid)['programs'] if p['id']==pids[1])['phase']=='P')
    generate(eid,pids[0]);generate(eid,pids[1])
    queued=dash(eid)['queue'];check(queued and all(q['match']['eventId']==eid for q in queued))
    match=queued[0]['match']
    assigned=mutate('dashboard/pair',{'matchId':match['id'],'court':1,'umpireId':plan['officials'][0]['id'],'presetId':preset,'reason':''},'POST')
    # Another event uses the same venue courts. It must see occupancy, not foreign match/device data.
    other,otherp=import_event('Other ongoing',today+timedelta(days=7),today+timedelta(days=8))
    # Legacy overlaps are retained, but new imports cannot create one.
    rollover(other,today,today+timedelta(days=1))
    mutate('tournament/event',{'plan':{**plan,'eventId':other}})
    generate(other,otherp[0])
    otherdash=dash(other);busy=next(c for c in otherdash['courts'] if c['number']==1)
    check(busy['outsideEvent'] and busy['otherEventName']=='Ongoing day two' and busy['match'] is None and busy['device']['state']=='N')
    check(all(q['match']['eventId']==other for q in otherdash['queue']))
    check(all(p['eventId']==other for p in otherdash['programs']))
    othermatch=otherdash['queue'][0]['match']
    mutate('dashboard/pair',{'matchId':othermatch['id'],'court':1,'umpireId':plan['officials'][0]['id'],'presetId':preset,'reason':'Cross-event override must fail','replaceWaiting':True},'POST',400)
    # Merely switching views must not change revision or revoke the existing access code.
    revision=dash()['revision'];dash(eid);dash(other);dash(future);dash(past);check(dash()['revision']==revision)
    grant=call('tablet/pair',{'code':assigned['code'],'deviceId':'dashboard-event-test-device','deviceName':'Dashboard event test'})
    view=grant['match'];m=view['match'];a=next(p['id'] for p in m['players'] if p['side']=='A');b=next(p['id'] for p in m['players'] if p['side']=='B')
    call('tablet/command',{'version':m['version'],'operationId':str(uuid.uuid4()),'action':'START','serving':{'serverSide':'A','serverId':a,'receiverId':b,'leftSide':'A'}},token=grant['token'])
    check(next(c for c in dash(other)['courts'] if c['number']==1)['outsideEvent'])
    check(next(c for c in dash(other)['courts'] if c['number']==1)['match'] is None)
    # A match already playing can finish after the event/session window. No new match can start.
    rollover(eid,today-timedelta(days=2),today-timedelta(days=1))
    check(dash(eid)['eventContext']['calendar']=='C' and dash(eid)['eventContext']['unfinished']>0)
    waiting=next(q['match'] for q in dash(eid)['queue'])
    expiredCode=mutate('dashboard/pair',{'matchId':waiting['id'],'court':2,'umpireId':plan['officials'][1]['id'],'presetId':preset,'reason':''},'POST')
    expiredGrant=call('tablet/pair',{'code':expiredCode['code'],'deviceId':'expired-window-test-device','deviceName':'Expired window test'})
    expiredMatch=expiredGrant['match']['match'];server=next(p['id'] for p in expiredMatch['players'] if p['side']=='A');receiver=next(p['id'] for p in expiredMatch['players'] if p['side']=='B')
    blocked=call('tablet/command',{'version':expiredMatch['version'],'operationId':str(uuid.uuid4()),'action':'START','serving':{'serverSide':'A','serverId':server,'receiverId':receiver,'leftSide':'A'}},status=400,token=expiredGrant['token'])
    check('event dates' in blocked['message'])
    current=call('tablet/state',token=grant['token'])
    for _ in range(5):current=call('tablet/command',{'version':current['match']['version'],'operationId':str(uuid.uuid4()),'action':'POINT','side':'A'},token=grant['token'])
    current=call('tablet/command',{'version':current['match']['version'],'operationId':str(uuid.uuid4()),'action':'SUBMIT'},token=grant['token'])
    check(dash(eid)['eventContext']['awaitingConfirmation']==1)
    mutate('dashboard/confirm',{'matchId':current['match']['id'],'version':current['match']['version'],'reason':''},'POST')
    check(not any(c['match'] and c['match']['id']==current['match']['id'] for c in dash(eid)['courts']))
    for pp in call('tournament/'+eid)['plans']:
     for pending in pp['matches']:
      if pending['status']=='SC':mutate('tournament/result',{'matchId':pending['id'],'matchVersion':pending['matchVersion'],'games':[{'a':5,'b':0}],'presetId':preset})
    check(dash(eid)['eventContext']['completed'] and dash(eid)['eventContext']['unfinished']==0)
    print(f'{checks} event-dashboard checks passed; disposable SQL database removed on exit.')
   finally:
    if process:process.terminate();process.wait(timeout=10);process=None

finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
