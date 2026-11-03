"""TRS API transfer/publication contract using disposable TLS SQL and a local receiver."""
import copy, json, socket, subprocess, tempfile, threading, time, urllib.request, urllib.error
from datetime import date, datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from sql_test_database import TestDatabase, project, dll
import test_inputs

socket.setdefaulttimeout(1)
base = 'http://localhost:5241/api/'
checks = 0
received = []
fail = False
key = 'integration-test-key-' + 'x' * 32
package = test_inputs.package()
package['sourceSystemId'] = 'TRS-integration-test'
package['events'][0].update(id='event:71', startDate=(date.today()+timedelta(days=1)).isoformat(), endDate=(date.today()+timedelta(days=2)).isoformat())
program = package['programs'][0]
program.update(id='program:72', eventId='event:71', minCapacity=2)
package['programs'] = [program]
person = package['participants'][0]
package['participants'] = [{**person, 'id':'participant:'+str(80+i), 'eventId':'event:71', 'name':'Player '+str(i), 'email':str(i)+'@example.com'} for i in range(2)]
package['entries'] = [{'id':'entry:'+str(90+i), 'eventId':'event:71', 'programId':'program:72', 'participantIds':['participant:'+str(80+i)], 'status':'A'} for i in range(2)]

class Receiver(BaseHTTPRequestHandler):
 def log_message(self, *args): pass
 def do_GET(self):
  if self.headers.get('Authorization') != 'Bearer export-test': self.send_error(401); return
  body = [{'id':71,'name':package['events'][0]['name'],'startDate':package['events'][0]['startDate'],'venue':package['events'][0]['venue']}] if self.path.endswith('/events') else package
  data=json.dumps(body).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(data)
 def do_PUT(self):
  if self.headers.get('Transfer-Encoding') == 'chunked':
   chunks=[]
   while True:
    size=int(self.rfile.readline().strip().split(b';')[0],16)
    if size==0: self.rfile.readline(); break
    chunks.append(self.rfile.read(size));self.rfile.read(2)
   raw=b''.join(chunks)
  else: raw=self.rfile.read(int(self.headers['Content-Length']))
  body=json.loads(raw)
  if self.headers.get('X-TLS-Key') != key: self.send_error(401); return
  if fail: self.send_error(503); return
  received.append((self.path,body));self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(b'{"accepted":true}')

def check(condition):
 global checks
 assert condition
 checks += 1

def call(path, body=None, method=None, status=200):
 global checks
 request=urllib.request.Request(base+path, data=json.dumps(body).encode() if body is not None else None, method=method or ('POST' if body is not None else 'GET'), headers={'Content-Type':'application/json'})
 try:
  with urllib.request.urlopen(request,timeout=30) as response: code=response.status;data=response.read()
 except urllib.error.HTTPError as e: code=e.code;data=e.read()
 assert code==status,(path,code,data[:1000]);checks+=1
 return json.loads(data)

def state(): return call('setup/')['data']
def mutate(path, body, method='PUT'): return call(path,{'revision':state()['revision'],**body},method)

for port in [5240,5241]:
 with socket.socket() as probe: assert probe.connect_ex(('127.0.0.1',port))!=0, 'Test port occupied'
server=ThreadingHTTPServer(('127.0.0.1',5240),Receiver)
threading.Thread(target=server.serve_forever,daemon=True).start()
db=TestDatabase();process=None;log=tempfile.TemporaryFile(mode='w+t',encoding='utf-8')
def start():
 global process
 process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base[:-5],'Trs__ApiBaseUrl':'http://localhost:5240','Trs__AccessToken':'export-test'},stdout=log,stderr=subprocess.STDOUT)
 for _ in range(120):
  if process.poll() is not None: raise RuntimeError('Test server exited')
  try: state();return
  except urllib.error.URLError: time.sleep(.1)
 raise RuntimeError('Test server startup timed out')
try:
 start()
 packages=call('trs/events');check(packages[0]['schemaVersion']==2)
 preview=call('setup/preview?kind=trs&revision='+str(state()['revision']),packages[0]);s=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data']
 eid=s['events'][0]['id'];pid=s['programs'][0]['id'];check(eid!='event:71' and pid!='program:72')
 rules={'name':'Short','gamesToWin':1,'pointsToWin':5,'winBy':1,'pointCap':5,'intervalAt':3,'decidingEndsAt':3}
 preset=mutate('presets/',{'preset':{'id':'','name':'Integration','status':'A','isDefault':True,'rules':rules}})['presets'][0]['id']
 v=call('tournament/'+eid);plan=v['eventPlan'];plan.update(dailySessions=[{'start':'09:00:00','end':'18:00:00'}],matchMinutes=5,restMinutes=0,officials=[{'id':'temp','name':'Umpire'}])
 mutate('tournament/event',{'plan':plan})
 entries=call('seeding/'+pid)['entries'];mutate('seeding/'+pid,{'mode':'N','seedCount':0,'seeds':{e['id']:None for e in entries}})
 preview=mutate('tournament/preview',{'eventId':eid,'programIds':[pid],'courts':[1],'earliestStart':package['events'][0]['startDate']+'T10:00:00','settings':{'format':'KO','groupCount':1,'advancePerGroup':1}},'POST')
 mutate('tournament/confirm',{'token':preview['token']},'POST')
 match=call('tournament/'+eid)['plans'][0]['matches'][0]
 mutate('tournament/result',{'matchId':match['id'],'matchVersion':match['matchVersion'],'presetId':preset,'games':[{'a':5,'b':0}]})
 settings={'enabled':True,'liveUrl':'http://localhost:5240/api/tls/events/71/live','resultsUrl':'http://localhost:5240/api/tls/events/71/programs/{programId}/results','intervalSeconds':120,'apiKey':key}
 saved=mutate('trs/publishing/'+eid,settings);check(saved['hasCredential'] and key not in json.dumps(saved))
 response=call('trs/publishing/'+eid+'/resend',{},'POST');check(not response['failures'])
 results=[body for path,body in received if path.endswith('/results')];check(bool(results))
 result=results[-1];check(result['eventId']==71 and result['programId']==72)
 check(result['matches'][0]['programId']==72 and result['matches'][0]['team1']['id'].startswith('entry:'))
 check(result['matches'][0]['games']==[{'a':5,'b':0}] and result['matches'][0]['winner'] in ['A','B'])
 check(result['matches'][0]['roundLabel'] and result['matches'][0]['matchDate'] and result['version']>0)
 check(all(field not in json.dumps(result) for field in ['email','mobile','protectedCredential','tokenHash']))
 check(any(path.endswith('/live') and body['matches']==[] for path,body in received))
 fail=True;response=call('trs/publishing/'+eid+'/resend',{},'POST');check({f['kind'] for f in response['failures']}=={'live','results'})
 fail=False;response=call('trs/publishing/'+eid+'/resend',{},'POST');check(not response['failures'])
 before=max(body['version'] for path,body in received if path.endswith('/results'))
 process.terminate();process.wait(timeout=10);start();call('trs/publishing/'+eid+'/resend',{},'POST')
 check(max(body['version'] for path,body in received if path.endswith('/results'))>before)
 current=call('trs/publishing/'+eid);check(current['enabled'] and current['hasCredential'])
 check(key not in json.dumps(state()))
 print(f'PASS: {checks} TRS transfer, original identity, publication, failure/recovery and restart checks.')
except Exception:
 log.seek(0);print(log.read()[-2500:]);raise
finally:
 if process: process.terminate();process.wait(timeout=10)
 server.shutdown();server.server_close();db.close();log.close()
