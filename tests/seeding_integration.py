"""Independent seeding with local SBA rankings, using disposable SQL only."""
import json,os,socket,subprocess,tempfile,time,urllib.request,urllib.error
from pathlib import Path
from sql_test_database import TestDatabase,project,dll
from sba_inputs import workbook
import test_inputs
base='http://localhost:5201/';checks=0

def call(path,body=None,method=None,expect=200,headers=None):
 global checks
 if body is not None and not isinstance(body,bytes):body=json.dumps(body).encode();headers={'Content-Type':'application/json',**(headers or {})}
 req=urllib.request.Request(base+'api/'+path,data=body,method=method or ('POST' if body is not None else 'GET'),headers=headers or {})
 try:
  with urllib.request.urlopen(req,timeout=30) as r:status=r.status;raw=r.read()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read()
 assert status==expect,f'{path}: expected {expect}, got {status}: {raw[:200]!r}'
 checks+=1;return json.loads(raw) if raw else None

def check(value):
 global checks
 assert value;checks+=1

def upload(points=900):
 rows=[("Men's Singles",[['Rank','Player','ID','Year','Points','Tournaments','Club','DOB'],['2','Ranking One','SBA001','','500','2','Ranking Club',''],['1','Ranking Two','SBA002','',str(points),'2','Another Club','']]),("Men's Doubles",[['Rank','P1','P2','ID1','ID2','Year','Points','Tournaments','Club1','Club2','DOB1','DOB2'],['1','Partner Two','Partner One','SBA002','SBA001','','800','3','Pair Club','Pair Club','','']])]
 boundary='TLS-SEED-TEST';body=('--'+boundary+'\r\nContent-Disposition: form-data; name="file"; filename="ranking.xlsx"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()+workbook(rows=rows)+('\r\n--'+boundary+'--\r\n').encode()
 return call('sba/import',body,headers={'Content-Type':'multipart/form-data; boundary='+boundary})

def state():return call('setup/')['data']
def get(pid):return call('seeding/'+pid)
def save(pid,seeds,mode='M',expect=200,**extra):return call('seeding/'+pid,{'revision':state()['revision'],'seeds':seeds,'mode':mode,**extra},'PUT',expect)

with socket.socket() as p:assert p.connect_ex(('127.0.0.1',5201))!=0
process=None;db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-seed-'):
  process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base.rstrip('/')},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
  for _ in range(100):
   try:initial=state();break
   except urllib.error.URLError:time.sleep(.1)
  package=test_inputs.package();package['events'][0]['maxProgramsPerParticipant']=None
  package['programs'][0]['name']="Men's Singles";package['programs'][1]['name']="Men's Doubles"
  third={**package['programs'][0],'id':'PR003','name':"men's singles"};package['programs'].append(third);package['programs'].append({**third,'id':'PR004','name':"Men's Singles "})
  package['programs'].append({**third,'id':'PR005','name':'School Teams','sport':'Basketball','entryType':'T','minPlayers':2,'maxPlayers':4,'minCapacity':1})
  package['entries'].append({'id':'TEAM001','eventId':'EV001','programId':'PR005','participantIds':['P001','P002'],'name':'North School','seed':None,'status':'A'})
  for i,p in enumerate(package['participants']):p['sbaId']=['SBA001','SBA002','SBA003',''][i]
  package['entries'] += [{**e,'id':'E3'+str(i),'programId':'PR003'} for i,e in enumerate(package['entries'][:2])]
  package['entries'] += [{**e,'id':'E4'+str(i),'programId':'PR004'} for i,e in enumerate(package['entries'][:2])]
  preview=call('setup/preview?kind=trs&revision='+str(initial['revision']),package);data=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data']
  eid=data['events'][0]['id'];pids=[p['id'] for p in data['programs']];single,pair,custom,spaced,team=pids
  t=get(team);check(not t['sbaAvailable']);check(t['entries'][0]['name']=='North School');teamseeds={t['entries'][0]['id']:1};check(save(team,teamseeds,seedCount=1)['seedCount']==1)
  call('seeding/'+team+'/suggest',{'revision':state()['revision'],'seedCount':1,'rankingFingerprint':t['rankingFingerprint']},expect=400)
  check(save(team,{k:None for k in teamseeds},'N',seedCount=0)['mode']=='N')
  original={k:data[k] for k in ['participants','entries','programs','imports']}
  a=get(single);check(not a['reviewed']);check(list(a['seeds'].values())==[1,2]);check(not a['categoryAvailable'])
  call('seeding/'+single,headers={'Host':'foreign.example'},expect=403)
  upload();a=get(single);check(a['categoryAvailable']);check(not get(custom)['categoryAvailable']);check(not get(spaced)['categoryAvailable']);check(get(pair)['entries'][0]['ranking']['player1']['sbaId']=='SBA002');check(get(pair)['entries'][1]['ranking'] is None)
  ids=list(a['seeds']);seeds=a['seeds'];suggest=call('seeding/'+single+'/suggest',{'revision':a['revision'],'seedCount':1,'rankingFingerprint':a['rankingFingerprint']})
  check(suggest['seeds'][ids[1]]==1 and suggest['seeds'][ids[0]] is None);check(get(single)['seeds']==seeds and not get(single)['reviewed'])
  call('seeding/'+custom+'/suggest',{'revision':a['revision'],'seedCount':1,'rankingFingerprint':a['rankingFingerprint']},expect=400)
  call('trs/ranking-snapshot',{'revision':a['revision'],'programId':custom,'rankingType':"Men's Singles"},expect=400)
  save(single,{ids[0]:1,ids[1]:1},expect=400);save(single,{ids[0]:1},expect=400);save(single,{**seeds,'foreign':None},expect=400);save(single,{ids[0]:0,ids[1]:None},expect=400);save(single,{ids[0]:1.5,ids[1]:None},expect=400);save(single,seeds,mode='X',expect=400);save(single,seeds,mode='N',expect=400)
  manual={ids[0]:None,ids[1]:1};saved=save(single,manual);check(saved['reviewed'] and saved['mode']=='M');check(saved['seeds']==manual)
  call('seeding/'+single,{'revision':a['revision'],'seeds':manual,'mode':'M'},'PUT',409)
  save(single,manual,seedCount=0,expect=400);save(single,seeds,seedCount=1,expect=400);save(single,manual,seedCount=3,expect=400)
  check(save(single,manual,seedCount=2)['seedCount']==2);check(get(single)['seedCount']==2)
  a=get(single);upload(950);save(single,suggest['seeds'],mode='S',rankingFingerprint=a['rankingFingerprint'],expect=409);check(get(single)['seeds']==manual)
  a=get(single);suggest=call('seeding/'+single+'/suggest',{'revision':a['revision'],'seedCount':2,'rankingFingerprint':a['rankingFingerprint']});saved=save(single,suggest['seeds'],mode='S',rankingFingerprint=suggest['rankingFingerprint']);check(saved['reviewed'] and saved['mode']=='S')
  savedplan=call('tournament/'+eid)['plans'][0];check(savedplan['rankingType']=="Men's Singles" and len(savedplan['rankingSnapshot'])==2)
  upload(990);now=call('tournament/'+eid)['plans'][0];check(now['rankingSnapshot']==savedplan['rankingSnapshot'] and now['seeds']==savedplan['seeds'])
  pairdata=get(pair);suggestpair=call('seeding/'+pair+'/suggest',{'revision':pairdata['revision'],'seedCount':2,'rankingFingerprint':pairdata['rankingFingerprint']});check(suggestpair['matched']==1 and list(suggestpair['seeds'].values())==[1,None])
  noSeeds={e['id']:None for e in get(custom)['entries']};check(save(custom,noSeeds,'N')['reviewed']);check(save(custom,noSeeds,'N')['mode']=='N');check(get(custom)['seedCount']==0)
  plan={'eventId':eid,'dailySessions':[{'start':'10:00:00','end':'18:00:00'}],'sessions':[],'courts':[1,2],'courtsConfigured':True,'progression':'I','officials':[{'id':'u1','name':'Official One'},{'id':'u2','name':'Official Two'}],'matchMinutes':30,'restMinutes':10,'reviewCourts':[]}
  call('tournament/event',{'revision':state()['revision'],'plan':plan},'PUT')
  call('presets/',{'revision':state()['revision'],'preset':{'id':'','name':'Audit standard','status':'A','isDefault':True,'rules':{'name':'Audit standard','gamesToWin':2,'pointsToWin':21,'winBy':2,'pointCap':30,'intervalAt':11,'decidingEndsAt':11}}},'PUT')
  draft=call('tournament/preview',{'revision':state()['revision'],'eventId':eid,'programIds':[single],'courts':[1,2],'earliestStart':data['events'][0]['startDate']+'T10:00:00','settings':{'format':'KO','groupCount':1,'advancePerGroup':1}})
  check(call('tournament/'+eid)['plans'][0]['seeds']==savedplan['seeds']);call('tournament/confirm',{'revision':draft['revision'],'token':draft['token']})
  check(get(single)['locked']);save(single,manual,expect=400)
  a=get(single);call('seeding/'+single+'/suggest',{'revision':a['revision'],'seedCount':1,'rankingFingerprint':a['rankingFingerprint']},expect=400)
  check(get(single)['entries'][1]['ranking']['accumulatedScore']==950)
  keys=[r['key'] for r in call('tournament/'+eid+'/reset-schedule')['rounds'] if not r['reason']]
  call('tournament/reset-schedule',{'revision':state()['revision'],'eventId':eid,'roundKeys':keys})
  call('tournament/reset',{'revision':state()['revision'],'programId':single},'POST');check(not get(single)['locked'])
  save(single,{id:None for id in ids},'N');check(call('tournament/'+eid)['plans'][0]['rankingSnapshot']==[])
  final=state();check({k:final[k] for k in original}==original)
  process.terminate();process.wait(timeout=10);process=None
 print(f'{checks} seeding checks passed: exact categories, singles/pairs, review, stale rankings, locks and unchanged imported data.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
