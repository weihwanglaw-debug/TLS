"""Default draw presets, per-match protection and tablet revocation in disposable SQL."""
import copy,json,socket,subprocess,tempfile,time,urllib.error,urllib.request
from datetime import date
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs
base='http://localhost:5203/api/';checks=0;process=None

def call(path,body=None,status=200,method=None,token=None):
 global checks
 req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
 except urllib.error.HTTPError as e:code=e.code;raw=e.read()
 assert code==status,(path,code,status,raw[:1500]);checks+=1;return json.loads(raw)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5203))!=0

db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-round-presets-') as folder:
  log=open(Path(folder)/'server.log','w')
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5203'},stdout=log,stderr=log)
   for _ in range(150):
    try:return call('setup/')['data']
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Test server not ready')
  def stop():
   global process
   if process:process.terminate();process.wait(timeout=10);process=None
  try:
   start();package=test_inputs.package();day=date.today().isoformat();package['events'][0].update(startDate=day,endDate=day)
   package['programs'][1].update(entryType='S',minPlayers=1,maxPlayers=1,gender='O',name='Bye Singles')
   template=package['participants'][0];package['participants']=[{**template,'id':'P'+str(i),'name':'Round Player '+str(i),'email':str(i)+'@example.com'} for i in range(11)]
   package['entries']=[{'id':'E'+str(i),'eventId':'EV001','programId':'PR001' if i<8 else 'PR002','participantIds':['P'+str(i)],'seed':None,'status':'A'} for i in range(11)]
   data=call('setup/')['data'];preview=call('setup/preview?kind=trs&revision='+str(data['revision']),package);saved=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eid=saved['events'][0]['id'];pids=[p['id'] for p in saved['programs']]
   def view():return call('tournament/'+eid)
   def request(path,body,status=200,method='POST'):return call(path,{'revision':view()['revision'],**body},status,method)
   def matches(pid):return next(p['matches'] for p in view()['plans'] if p['programId']==pid)
   plan={'eventId':eid,'sessions':[{'date':day,'start':'00:00:00','end':'23:59:00'}],'courts':[1,2,3,4],'courtsConfigured':True,'officials':[{'id':'o1','name':'Umpire One'},{'id':'o2','name':'Umpire Two'}],'matchMinutes':5,'restMinutes':0}
   request('tournament/event',{'plan':plan},method='PUT')
   rules={'name':'Five','gamesToWin':1,'pointsToWin':5,'winBy':2,'pointCap':7,'intervalAt':3,'decidingEndsAt':3}
   missing={'eventId':eid,'programIds':[pids[0]],'courts':[],'drawOnly':True};request('tournament/preview',missing,400)
   assert not matches(pids[0])
   preset=request('presets/',{'preset':{'id':'','name':'Five','status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][0]
   def draw(pid):
    entries=[e for e in saved['entries'] if e['programId']==pid];request('seeding/'+pid,{'mode':'N','seedCount':0,'seeds':{e['id']:None for e in entries}},method='PUT')
    p=request('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[1,2,3,4],'earliestStart':day+'T00:00:00','settings':{'format':'KO','groupCount':1,'advancePerGroup':1}})
    assert all(m['rulesAssigned'] and m['presetId']==preset['id'] for m in p['matches'] if m['status']!='BY')
    request('tournament/confirm',{'token':p['token']})
   draw(pids[0]);draw(pids[1]);assert any(m['status']=='BY' for m in matches(pids[1]))
   changed={**preset,'rules':{**rules,'pointsToWin':9,'pointCap':11}}
   request('presets/',{'preset':changed},method='PUT');assert all(m['rules']['pointsToWin']==5 for m in matches(pids[0]))
   other=request('presets/',{'preset':{'id':'','name':'Seven','status':'A','isDefault':True,'rules':{**rules,'pointsToWin':7,'pointCap':9}}},method='PUT')['presets'][-1]
   assert sum(p['isDefault'] for p in call('presets/')['presets'])==1
   assert all(m['presetId']==preset['id'] for m in matches(pids[0]))
   def assign(m,presetId,confirm=False,status=200):
    fresh=next(x for x in matches(m['programId']) if x['id']==m['id']);return request('presets/assign',{'matchId':m['id'],'matchVersion':fresh['matchVersion'],'presetId':presetId,'confirmRoundChange':confirm},status)
   ms=matches(pids[0]);codes=[];token=None
   for i,m in enumerate(ms[:2]):
    response=request('dashboard/pair',{'matchId':m['id'],'court':m['court'],'umpireId':view()['eventPlan']['officials'][i]['id'],'presetId':preset['id']});codes.append(response['code'])
    if i==0:token=call('tablet/pair',{'code':response['code'],'deviceId':'round-device-1','deviceName':'Round Test Tablet'})['token']
   assign(ms[0],other['id'],status=400);assign(ms[0],other['id'],True)
   call('tablet/state',token=token,status=401)
   secondToken=call('tablet/pair',{'code':codes[1],'deviceId':'round-device-2','deviceName':'Second Tablet'})['token']
   assert matches(pids[0])[0]['rules']['pointsToWin']==7 and matches(pids[0])[1]['rules']['pointsToWin']==5
   other['rules']['pointsToWin']=11;other['rules']['pointCap']=13;request('presets/',{'preset':other},method='PUT');assign(ms[0],other['id']);assert matches(pids[0])[0]['rules']['pointsToWin']==7
   m=matches(pids[0])[0];paired=request('dashboard/pair',{'matchId':m['id'],'court':m['court'],'umpireId':view()['eventPlan']['officials'][0]['id'],'presetId':other['id']});token=call('tablet/pair',{'code':paired['code'],'deviceId':'round-device-3','deviceName':'Start Tablet'})['token'];state=call('tablet/state',token=token)['match']
   import uuid
   serving={'serverSide':'A','serverId':next(p['id'] for p in state['players'] if p['side']=='A'),'receiverId':next(p['id'] for p in state['players'] if p['side']=='B'),'leftSide':'A'}
   call('tablet/command',{'version':state['version'],'operationId':str(uuid.uuid4()),'action':'START','serving':serving},token=token)
   assert matches(pids[0])[0]['presetLocked'] and not matches(pids[0])[1]['presetLocked'];assign(ms[0],preset['id'],True,400)
   # A started sibling does not prevent changing this unstarted match.
   assign(ms[1],other['id'],True);call('tablet/state',token=secondToken,status=401)
   assert matches(pids[0])[1]['rules']['pointsToWin']==11
   m=next(m for m in matches(pids[1]) if m['status']=='SC')
   request('tournament/result',{'matchId':m['id'],'matchVersion':m['matchVersion'],'games':[],'walkoverWinner':m['entryA']},method='PUT')
   recorded=next(x for x in matches(pids[1]) if x['id']==m['id']);assert recorded['presetLocked']
   request('tournament/clear-result',{'matchId':m['id'],'matchVersion':recorded['matchVersion'],'reason':'Correct erroneous walkover'})
   assign(m,other['id'],True,400);request('tournament/reset',{'programId':pids[1]},400)
   before=matches(pids[1]);stop();start();assert matches(pids[1])==before;assign(m,other['id'],True,400)
   bad=copy.deepcopy(other);bad['status']='X';request('presets/',{'preset':bad},400,'PUT')
   assert len(call('setup/')['data']['participants'])==11
   print(f'PASS: {checks} preset HTTP checks; required defaults, frozen match rules, independent siblings, starts, clear protection, restart and targeted tablet revocation.')
  finally:stop();log.close()
finally:db.close()
