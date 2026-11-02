"""Two event progression scenarios and preset/daily-hours safety in disposable SQL."""
import copy,json,socket,subprocess,tempfile,time,urllib.error,urllib.request
from datetime import date,datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs

base='http://localhost:5196/api/';checks=0;process=None
def call(path,body=None,status=200,method=None):
 global checks
 request=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(request,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as error:code=error.code;raw=error.read()
 assert code==status,(path,code,status,raw[:1800]);checks+=1;return json.loads(raw)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5196))!=0
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-event-workflow-') as folder:
  folder=Path(folder);log=open(folder/'server.log','w')
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5196'},stdout=log,stderr=log)
   for _ in range(100):
    try:return call('setup/')['data']
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Workflow server not ready')
  def stop():
   global process
   if process:process.terminate();process.wait(timeout=10);process=None
  try:
   start();assert call('presets/')['presets']==[];call('tournament/rules',{},404,'PUT')
   for scenario,mode,counts in [('Company','I',[8,16]),('Formal','A',[8,8])]:
    package=test_inputs.package();event=package['events'][0];first=(date.today()+timedelta(days=10 if mode=='I' else 20)).isoformat();last=(date.today()+timedelta(days=11 if mode=='I' else 21)).isoformat()
    event.update(name=scenario+' progression test',startDate=first,endDate=last,maxProgramsPerParticipant=None);package['sourceSystemId']='workflow-'+mode
    program=package['programs'][0];person=package['participants'][0]
    package['programs']=[{**program,'id':'P'+str(i),'name':'Program '+str(i+1),'maxCapacity':32} for i in range(2)]
    package['participants']=[{**person,'id':f'p{i}-{n}','name':f'Test player {i}-{n}','email':f'{i}-{n}@example.com'} for i,count in enumerate(counts) for n in range(count)]
    package['entries']=[{'id':f'e{i}-{n}','eventId':event['id'],'programId':'P'+str(i),'participantIds':[f'p{i}-{n}'],'seed':n+1,'status':'A'} for i,count in enumerate(counts) for n in range(count)]
    preview=call('setup/preview?kind=trs&revision='+str(call('setup/')['data']['revision']),package);data=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eventId=next(e['id'] for e in data['events'] if e['name']==event['name']);pids=[p['id'] for p in data['programs'] if p['eventId']==eventId]
    def view():return call('tournament/'+eventId)
    def plan(pid):return next(p for p in view()['plans'] if p['programId']==pid)
    def mutate(path,body,status=200,method='PUT'):return call('tournament/'+path,{'revision':view()['revision'],**body},status,method)
    setup={'eventId':eventId,'dailySessions':[{'start':'10:00:00','end':'12:00:00'},{'start':'14:00:00','end':'18:00:00'}],'sessions':[],'courts':[1,2,3,4],'courtsConfigured':True,'progression':mode,'officials':[{'id':scenario+str(n),'name':'Official '+str(n)} for n in range(4)],'matchMinutes':30,'restMinutes':10,'reviewCourts':[]}
    invalid=copy.deepcopy(setup);invalid['dailySessions'][1]['start']='11:00:00';mutate('event',{'plan':invalid},400)
    invalid=copy.deepcopy(setup);invalid['officials'].append(invalid['officials'][0]);mutate('event',{'plan':invalid},400)
    invalid=copy.deepcopy(setup);invalid['courts']=[];mutate('event',{'plan':invalid});assert view()['eventCourts']==[1,2,3,4]
    mutate('event',{'plan':setup});setup['officials']=view()['eventPlan']['officials'];assert view()['dailySessions']==setup['dailySessions'] and view()['eventCourts']==[1,2,3,4]
    rules={'name':scenario+' five points','gamesToWin':1,'pointsToWin':5,'winBy':2,'pointCap':7,'intervalAt':3,'decidingEndsAt':3}
    presetBody={'id':'','name':rules['name'],'status':'A','isDefault':True,'rules':rules}
    preset=call('presets/',{'revision':view()['revision'],'preset':presetBody},method='PUT')['presets'][-1]
    call('presets/',{'revision':view()['revision'],'preset':presetBody},400,'PUT')
    def generate(pid,courts,initial=False,duration=30,status=200):
     draft={'settings':{'format':'KO','groupCount':1,'advancePerGroup':1},'seeds':{e['id']:e['seed'] for e in data['entries'] if e['programId']==pid}} if initial else {}
     return mutate('preview',{'eventId':eventId,'programIds':[pid],'courts':courts,'earliestStart':first+'T10:00:00','matchMinutes':duration,**draft},status,'POST')
    a=generate(pids[0],[1,2,3,4],True);assert plan(pids[0])['matches']==[] and not plan(pids[0])['seedsReviewed']
    # Back/cancel from preview must not persist draft settings/seeds or fixtures.
    assert not view()['plans'][0]['matches'];mutate('confirm',{'token':a['token']},method='POST')
    b=generate(pids[1],[1,2,3,4],True);mutate('confirm',{'token':b['token']},method='POST')
    for match in [*plan(pids[0])['matches'],*plan(pids[1])['matches']]:
     if match['status']=='BY':continue
     assert match['court'] in [1,2,3,4] and match['rulesAssigned']
     assert first<=match['start'][:10]<=last
     assert any(w['start']<=match['start'][11:] and match['end'][11:]<=w['end'] and match['start'][:10]==match['end'][:10] for w in setup['dailySessions'])
    firstMatch=plan(pids[0])['matches'][0]
    mutate('result',{'matchId':firstMatch['id'],'matchVersion':firstMatch['matchVersion'],'games':[{'a':1,'b':0}]},400)
    call('dashboard/pair',{'revision':view()['revision'],'matchId':firstMatch['id'],'court':firstMatch['court'],'umpireId':firstMatch['umpireId']},400)
    call('presets/assign',{'revision':view()['revision'],'matchId':firstMatch['id'],'presetId':preset['id'],'matchVersion':firstMatch['matchVersion']})
    frozen=plan(pids[0])['matches'][0];assert frozen['rules']['pointsToWin']==5
    changed=copy.deepcopy(preset);changed['rules'].update(pointsToWin=7,pointCap=9)
    call('presets/',{'revision':view()['revision'],'preset':changed},method='PUT');assert plan(pids[0])['matches'][0]['rules']['pointsToWin']==5
    changed['status']='X';changed['isDefault']=False;call('presets/',{'revision':view()['revision'],'preset':changed},method='PUT');assert plan(pids[0])['matches'][0]['rules']['pointsToWin']==5
    other=plan(pids[0])['matches'][1];mutate('result',{'matchId':other['id'],'matchVersion':other['matchVersion'],'presetId':preset['id'],'games':[{'a':5,'b':0}]}) # Existing frozen round rules remain valid after master deactivation.
    activeBody={**presetBody,'name':scenario+' manual','rules':{**rules,'name':scenario+' manual'}};active=call('presets/',{'revision':view()['revision'],'preset':activeBody},method='PUT')['presets'][-1]
    def finish(pid):
     for match in plan(pid)['matches']:
      if match['status']!='SC':continue
      mutate('result',{'matchId':match['id'],'matchVersion':match['matchVersion'],'games':[{'a':5,'b':0}],'startedAt':match['start'],'finishedAt':match['end'],**({} if match['rulesAssigned'] else {'presetId':active['id']})})
    finish(pids[0]);assert view()['progressionLocked'];invalid={**setup,'progression':'A' if mode=='I' else 'I'};mutate('event',{'plan':invalid})
    invalid=copy.deepcopy(setup);invalid['courts']=[1];beforeCourts=[m['court'] for p in view()['plans'] for m in p['matches']];mutate('event',{'plan':invalid});assert view()['eventCourts']==[1,2,3,4] and [m['court'] for p in view()['plans'] for m in p['matches']]==beforeCourts # Legacy court pools cannot restrict master courts or move saved assignments.
    # Both legacy codes now advance independently; program 2 is unfinished.
    semis=generate(pids[0],[1,2],duration=20);assert len(semis['matches'])==2;mutate('confirm',{'token':semis['token']},method='POST');finish(pids[0])
    final=generate(pids[0],[1],duration=25);assert len(final['matches'])==1 and final['matches'][0]['roundLabel']=='Final';mutate('confirm',{'token':final['token']},method='POST')
    assert plan(pids[1])['lastRound']==1 and sum(m['status']=='SC' for m in plan(pids[1])['matches'])==counts[1]//2
    assert plan(pids[0])['lastRound']==3
    before=view();stop();start();assert view()==before
   print(f'PASS: {checks} event workflow HTTP checks; two programs/four courts, legacy progression codes advance independently, repeating sessions, preview isolation, preset snapshots and restart persistence.')
  except Exception:
   log.flush();print((folder/'server.log').read_text());raise
  finally:stop();log.close()
finally:db.close()
