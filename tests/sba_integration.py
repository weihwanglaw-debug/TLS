"""SBA module checks against disposable SQL only; no runtime sample rankings."""
import io,json,os,socket,subprocess,tempfile,time,urllib.request,urllib.error,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from sql_test_database import TestDatabase,project,dll,root
import test_inputs
base='http://localhost:5196/';checks=0

def check(ok):
 global checks
 assert ok
 checks+=1

def call(path,body=None,method=None,headers=None,expect=200):
 if body is not None and not isinstance(body,bytes):body=json.dumps(body).encode();headers={'Content-Type':'application/json',**(headers or {})}
 req=urllib.request.Request(base+'api/'+path,data=body,method=method or ('POST' if body is not None else 'GET'),headers=headers or {})
 try:
  with urllib.request.urlopen(req,timeout=45) as response:status=response.status;raw=response.read()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read()
 assert status==expect, f'{path}: expected {expect}, got {status}: {raw[:250]!r}'
 check(True)
 return json.loads(raw) if raw else None

from sba_inputs import workbook

def upload(raw=None,name='rankings.xlsx',expect=200):
 boundary='TLS-SBA-INTEGRATION';data=workbook() if raw is None else raw
 body=('--'+boundary+'\r\nContent-Disposition: form-data; name="file"; filename="'+name+'"\r\nContent-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\r\n\r\n').encode()+data+('\r\n--'+boundary+'--\r\n').encode()
 return call('sba/import',body,headers={'Content-Type':'multipart/form-data; boundary='+boundary},expect=expect)

def sql(db,query):
 script="""$ErrorActionPreference='Stop';$cb=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($env:TLS_TEST_BASE);$cb['Initial Catalog']=$env:TLS_TEST_NAME;if($env:TLS_TEST_NAME -notmatch '^TLS_Test_[a-f0-9]{32}$'){throw 'Unsafe test DB'};$cn=New-Object System.Data.SqlClient.SqlConnection($cb.ConnectionString);$cn.Open();try{$c=$cn.CreateCommand();$c.CommandText=$env:TLS_SBA_QUERY;Write-Output $c.ExecuteScalar()}finally{$cn.Dispose()}"""
 result=subprocess.run(['powershell','-NoProfile','-Command',script],env={**db.control,'TLS_SBA_QUERY':query},capture_output=True,text=True)
 if result.returncode:raise RuntimeError('Disposable SQL check failed (connection withheld).')
 return result.stdout.strip()

with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5196))!=0
process=None;db=TestDatabase()
try:
 # Verify the additive script also upgrades an existing schema, and is repeatable.
 sql(db,'DROP TABLE SbaRankings; DROP TABLE BadmintonClub; SELECT 1')
 db.command('--upgrade-sba');db.command('--upgrade-sba')
 with tempfile.TemporaryDirectory(prefix='tls-sba-') as folder:
  with open(Path(folder)/'server.log','w') as log:
   process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':base.rstrip('/')},stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
   for _ in range(100):
    try:initial=call('setup/')['data'];break
    except urllib.error.URLError:time.sleep(.1)
   check(len(call('sba/types'))==35);check(call('sba/rankings')==[])
   call('sba/types',headers={'Host':'foreign.example'},expect=403)
   upload(b'',expect=400);upload(b'not a workbook',expect=400);upload(name='ranking.csv',expect=400)
   preview=call('setup/preview?kind=trs&revision='+str(initial['revision']),test_inputs.package());call('setup/confirm',{'revision':preview['revision'],'token':preview['token']})
   before=call('setup/')['data'];summary=upload()
   check(summary['importedRows']==3);check(summary['addedClubs']==3);check(summary['skippedSheets']==['Notes'])
   ranks=call('sba/rankings');check(len(ranks)==3);check(ranks[0]['updatedAt'] is not None)
   check(len(call('sba/rankings?type=Men%27s%20Singles'))==2)
   call('sba/rankings?type=unknown',expect=400)
   check(call('sba/members/SBA003')['name']=='Partner Two')
   check(len(call('sba/members?name=Ranking'))==1)
   check(len(call('sba/admin/members?query=SBA001'))==1)
   check(call('setup/')['data']==before)
   check(upload()['addedClubs']==0)
   stable=call('sba/rankings');audit=sql(db,'SELECT COUNT(*) FROM AuditLogs')
   upload(workbook(invalid=True),expect=400);upload(workbook(duplicate=True),expect=400)
   check(call('sba/rankings')==stable);check(sql(db,'SELECT COUNT(*) FROM AuditLogs')==audit)
   duplicate_row=['1','Duplicate Player','DUP001','','100','1','Uncommitted Club','']
   upload(workbook(rows=[("Men's Singles",[['Rank','Player','ID','Year','Points','Tournaments','Club','DOB'],duplicate_row,duplicate_row])]),expect=400)
   check(sql(db,"SELECT COUNT(*) FROM BadmintonClub WHERE Name='Uncommitted Club'")=='0');check(call('sba/rankings')==stable)
   unknown=workbook(rows=[('Notes',[['Unknown']])]);upload(unknown,expect=400);check(call('sba/rankings')==stable)
   one=workbook(rows=[("Women's Singles",[['Rank','Player','ID','Year','Points','Tournaments','Club','DOB'],['1','New List Player','SBA010','','700','2','New Club','']])])
   check(upload(one)['importedRows']==1);check(len(call('sba/rankings'))==1);check(sql(db,'SELECT COUNT(*) FROM BadmintonClub')=='4')
   # Kestrel's 2MB default must be overridden for this TRS-compatible 20MB upload endpoint.
   check(upload(workbook(padding=2_200_000))['importedRows']==3)
   with ThreadPoolExecutor(max_workers=2) as executor:
    responses=list(executor.map(lambda _:upload(),range(2)))
   check(all(r['importedRows']==3 for r in responses));check(call('setup/')['data']==before)
   check(sql(db,"SELECT COUNT(*) FROM AuditLogs WHERE Action='SBA_RANKING_IMPORT'")=='6')
   process.terminate();process.wait(timeout=10);process=None
 print(f'{checks} SBA integration checks passed; event participant records and club text unchanged.')
finally:
 if process:process.terminate();process.wait(timeout=10)
 db.close()
