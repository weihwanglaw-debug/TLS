"""TRS-adapted formats/results and JSON integration against disposable local SQL."""
import copy,json,socket,subprocess,tempfile,time,urllib.error,urllib.request
from datetime import date,datetime,timedelta,timezone
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
import test_inputs
from sba_inputs import workbook

base='http://localhost:5199/api/';checks=0;process=None
def call(path,body=None,status=200,method=None,headers=None):
 global checks
 request=urllib.request.Request(base+path,data=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers=headers or {'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(request,timeout=30) as response:code=response.status;raw=response.read()
 except urllib.error.HTTPError as error:code=error.code;raw=error.read()
 assert code==status,(path,code,status,raw[:1800]);checks+=1;return json.loads(raw)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5199))!=0
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-fixture-result-') as folder:
  folder=Path(folder);events=folder/'trs-events.json'
  events.write_text('[]');log=open(folder/'server.log','w')
  def start():
   global process
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5199','Trs__EventFile':str(events)},stdout=log,stderr=log)
   for _ in range(100):
    try:return call('setup/')['data']
    except urllib.error.URLError:time.sleep(.1)
   raise RuntimeError('Fixture/result test server did not start')
  def stop():
   global process
   if process:process.terminate();process.wait(timeout=10);process=None
  try:
   start();assert call('trs/events')==[] and call('trs/rankings')==[]
   for index,format in enumerate(['KO','RR','GK']):
    package=test_inputs.package();event=package['events'][0];day=(date.today()+timedelta(days=10+index*3)).isoformat();end=(date.today()+timedelta(days=11+index*3)).isoformat()
    package['sourceSystemId']='formats-'+format;package['exportedAt']=datetime.now(timezone.utc).isoformat();event.update(name='Format test '+format,startDate=day,endDate=end,maxProgramsPerParticipant=None)
    package['programs']=[package['programs'][0]];program=package['programs'][0];program.update(maxCapacity=32,name="Men's Singles")
    package['participants']=[{**package['participants'][0],'id':'p'+str(i),'name':'Test '+str(i),'email':str(i)+'@example.com','sbaId':'SBA'+str(i)} for i in range(6)]
    package['entries']=[{'id':'e'+str(i),'eventId':event['id'],'programId':program['id'],'participantIds':['p'+str(i)],'seed':i+1 if i<2 else None,'status':'A'} for i in range(6)]
    events.write_text(json.dumps([package]));assert call('trs/events')[0]['events'][0]['name']==event['name']
    revision=call('setup/')['data']['revision'];preview=call('setup/preview?kind=trs&revision='+str(revision),package);saved=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data'];eventId=saved['events'][-1]['id'];pid=next(p['id'] for p in saved['programs'] if p['eventId']==eventId)
    def view():return call('tournament/'+eventId)
    def mutate(path,body,status=200,method='PUT'):return call('tournament/'+path,{'revision':view()['revision'],**({'presetId':presetId} if path=='result' else {}),**body},status,method)
    plan={'eventId':eventId,'sessions':[{'date':day,'start':'10:00:00','end':'22:00:00'},{'date':end,'start':'10:00:00','end':'22:00:00'}],'officials':[{'id':eventId+'-O001','name':'One'},{'id':eventId+'-O002','name':'Two'}],'matchMinutes':5,'restMinutes':0,'reviewCourts':[1,2]}
    mutate('event',{'plan':plan});rules={'name':'Five points','gamesToWin':1,'pointsToWin':5,'winBy':2,'pointCap':7,'intervalAt':3,'decidingEndsAt':3,'challengesEnabled':True,'challengesPerGame':2}
    mutate('draw-settings',{'eventId':eventId,'programIds':[pid],'groupCount':2,'advancePerGroup':2,'format':'H'},400)
    mutate('draw-settings',{'eventId':eventId,'programIds':[pid],'groupCount':2,'advancePerGroup':2,'format':format})
    presetId=call('presets/',{'revision':view()['revision'],'preset':{'id':'','name':'Five points '+format,'status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][-1]['id']
    seeds={e['id']:e['seed'] for e in saved['entries'] if e['programId']==pid};mutate('seeds',{'programId':pid,'seeds':seeds})
    rankrows=[['Rank','Player','ID','Year','Points','Tournaments','Club','DOB']]+[[str(i+1),'Test '+str(i),'SBA'+str(i),'',str(100-i),'1','','1990-01-10'] for i in range(6)]
    boundary='TLS-FORMAT-RANK';raw=('--'+boundary+'\r\nContent-Disposition: form-data; name="file"; filename="ranking.xlsx"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+workbook(rows=[("Men's Singles",rankrows)])+('\r\n--'+boundary+'--\r\n').encode()
    call('sba/import',raw,headers={'Content-Type':'multipart/form-data; boundary='+boundary})
    snapshot=call('trs/ranking-snapshot',{'revision':view()['revision'],'programId':pid,'rankingType':"Men's Singles"});assert len(snapshot['rankings'])==6 and view()['plans'][0]['seeds']==seeds
    generation={'eventId':eventId,'programIds':[pid],'courts':[1,2],'earliestStart':day+'T10:00:00','matchMinutes':5}
    def generate():return mutate('preview',generation,method='POST')
    draw=generate();expected={'KO':4,'RR':15,'GK':6}[format];assert len(draw['matches'])==expected and view()['plans'][0]['matches']==[]
    original=draw['matches'][0]['entryA'];other=next(e for e in seeds if e!=original)
    draw=mutate('preview-swap',{'token':draw['token'],'entryA':original,'entryB':other},method='POST');assert draw['matches'][0]['entryA']==other
    mutate('confirm',{'token':draw['token']},method='POST');assert len(view()['plans'][0]['matches'])==expected
    if format=='KO':
     latest=view()['plans'][0];mutate('reset-schedule',{'eventId':eventId,'roundKeys':[pid+':K:'+str(latest['lastRound'])]},method='POST');mutate('reset-latest-round',{'programId':pid},method='POST');assert view()['plans'][0]['phase']=='P' and not view()['plans'][0]['matches'];assert next(i for i in call('setup/')['data']['imports'] if i['eventId']==eventId)['stage']=='P'
     draw=generate();mutate('confirm',{'token':draw['token']},method='POST')
    match=next(m for m in view()['plans'][0]['matches'] if m['status']=='SC');entry=next(e for e in saved['entries'] if e['id']==match['entryA']);player=entry['participantIds'][0]
    details={'shuttlesUsed':5,'remark':'Paper summary','cards':[{'participantId':player,'type':'Y','game':1,'remark':''}],'challenges':[{'side':'A','game':1,'outcome':'U'}]}
    mutate('result',{'matchId':match['id'],'games':[{'a':5,'b':0}],'details':{**details,'shuttlesUsed':-1}},400)
    mutate('result',{'matchId':match['id'],'games':[{'a':5,'b':0}],'details':details,'startedAt':match['start'],'finishedAt':match['end']})
    recorded=next(m for m in view()['plans'][0]['matches'] if m['id']==match['id']);assert recorded['actualDurationMinutes']==5 and recorded['resultSource']=='M' and recorded['resultDetails']==details
    mutate('result',{'matchId':match['id'],'matchVersion':recorded['matchVersion']-1,'games':[{'a':5,'b':1}]},409)
    mutate('result',{'matchId':match['id'],'matchVersion':recorded['matchVersion'],'games':[{'a':5,'b':1}]},400)
    mutate('result',{'matchId':match['id'],'matchVersion':recorded['matchVersion'],'games':[{'a':5,'b':1}],'reason':'Correct paper entry'})
    recorded=next(m for m in view()['plans'][0]['matches'] if m['id']==match['id']);assert recorded['resultDetails']==details
    mutate('clear-result',{'matchId':match['id'],'matchVersion':recorded['matchVersion'],'reason':''},400,'POST')
    mutate('clear-result',{'matchId':match['id'],'matchVersion':recorded['matchVersion'],'reason':'Replay required'},method='POST')
    assert next(m for m in view()['plans'][0]['matches'] if m['id']==match['id'])['status']=='SC'
    for m in view()['plans'][0]['matches']:
     if m['status']=='BY':continue
     mutate('result',{'matchId':m['id'],'matchVersion':m['matchVersion'],'games':[{'a':5,'b':0}]})
    if format=='RR':assert view()['plans'][0]['phase']=='C';mutate('preview',generation,400,'POST')
    else:
     draw=generate();mutate('confirm',{'token':draw['token']},method='POST');latest=view()['plans'][0];mutate('reset-schedule',{'eventId':eventId,'roundKeys':[pid+':K:'+str(latest['lastRound'])]},method='POST');mutate('reset-latest-round',{'programId':pid},method='POST')
    before=view();stop();start();assert view()==before
   print(f'PASS: {checks} fixture/result HTTP assertions; formats, preview swaps, reset, corrections, duration, details, ranking snapshots and SQL restart persistence.')
  except Exception:
   log.flush();print((folder/'server.log').read_text());raise
  finally:stop();log.close()
finally:db.close()

