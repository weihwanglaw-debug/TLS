"""Real React screen flow against an isolated SQL API; no production test rows."""
import json,os,socket,subprocess,tempfile,time,urllib.request,urllib.error
from datetime import date,timedelta
from pathlib import Path
from sql_test_database import TestDatabase,project,dll,root
import test_inputs
base='http://localhost:5195/'
def call(path,body=None,method=None):
 req=urllib.request.Request(base+'api/'+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
 with urllib.request.urlopen(req,timeout=30) as response:return json.load(response)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5195))!=0
db=TestDatabase();process=None
try:
 with tempfile.TemporaryDirectory(prefix='tls-ui-workflow-') as folder:
  with open(Path(folder)/'server.log','w') as log:
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base.rstrip('/')},stdout=log,stderr=log)
   for _ in range(100):
    try:data=call('setup/')['data'];break
    except urllib.error.URLError:time.sleep(.1)
   package=test_inputs.package();first=date.today().isoformat()
   package['events'][0].update(name='UI disposable test',startDate=first,endDate=first)
   preview=call('setup/preview?kind=trs&revision='+str(data['revision']),package)
   data=call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})['data']
   eventId=data['events'][0]['id'];programId=data['programs'][0]['id']
   plan={'eventId':eventId,'dailySessions':[{'start':'00:00:00','end':'12:00:00'},{'start':'12:00:00','end':'23:59:00'}],'sessions':[],'courts':[1,2,3,4],'courtsConfigured':True,'progression':'I','officials':[{'id':str(n),'name':'Official '+str(n)} for n in range(4)],'matchMinutes':30,'restMinutes':10,'reviewCourts':[]}
   call('tournament/event',{'revision':data['revision'],'plan':plan},'PUT')
   result=subprocess.run(['npm.cmd','exec','vitest','run','--','--config','vitest.config.ts'],cwd=root/'Frontend',env={**os.environ,'TLS_UI_TEST_URL':base,'TLS_UI_EVENT':eventId,'TLS_UI_PROGRAM':programId})
   process.terminate();process.wait(timeout=10);process=None
   if result.returncode:raise RuntimeError('React screen workflow checks failed.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
