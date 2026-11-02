"""SQL Server test isolation. Credentials stay in captured subprocess input/output."""
import atexit,json,os,re,subprocess,uuid
from pathlib import Path

root=Path(__file__).resolve().parents[1]
project=root/'Backend/LiveScore.Api'
dll=project/'bin/Debug/net8.0/LiveScore.Api.dll'

class TestDatabase:
    __test__=False
    def __init__(self):
        self.name='TLS_Test_'+uuid.uuid4().hex
        self.closed=False
        base=os.environ.get('ConnectionStrings__TLSConnection')
        if not base:
            config=json.loads((project/'appsettings.Local.json').read_text(encoding='utf-8-sig'))
            base=config['ConnectionStrings']['TLSConnection']
        self.control={**os.environ,'TLS_TEST_BASE':base,'TLS_TEST_NAME':self.name}
        result=self._sql('connection')
        self.env={**os.environ,'ConnectionStrings__TLSConnection':result.strip()}
        self.env.pop('LiveScore__Database',None)
        atexit.register(self.close)
        self.command('--initialize-sql','true')
    def _sql(self,mode):
        script=r'''
$ErrorActionPreference='Stop'
$name=$env:TLS_TEST_NAME
if($name -notmatch '^TLS_Test_[a-f0-9]{32}$'){throw 'Unsafe test database name'}
$cb=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($env:TLS_TEST_BASE)
$cb['Initial Catalog']=$name
if($env:TLS_TEST_MODE -eq 'connection'){Write-Output $cb.ConnectionString;exit}
if($env:TLS_TEST_MODE -eq 'drop'){$cb['Initial Catalog']='master'}
$cn=New-Object System.Data.SqlClient.SqlConnection($cb.ConnectionString)
$cn.Open()
try {
 $cmd=$cn.CreateCommand()
 if($env:TLS_TEST_MODE -eq 'drop'){$cmd.CommandText="IF DB_ID('$name') IS NOT NULL BEGIN ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name]; END";[void]$cmd.ExecuteNonQuery()}
 elseif($env:TLS_TEST_MODE -eq 'expire-pairing'){$cmd.CommandText="UPDATE SystemSettings SET LiveVenueJson=JSON_MODIFY(LiveVenueJson, '$.bindings[0].codeExpires', '2000-01-01T00:00:00Z') WHERE Id=1";[void]$cmd.ExecuteNonQuery()}
 elseif($env:TLS_TEST_MODE -eq 'legacy-preset-id'){$cmd.CommandText="UPDATE SystemSettings SET LiveVenueJson=JSON_MODIFY(LiveVenueJson, '$.presets[0].id', 'legacy-preset-guid') WHERE Id=1";[void]$cmd.ExecuteNonQuery()}
 elseif($env:TLS_TEST_MODE -eq 'audit'){$cmd.CommandText='SELECT COUNT(*) FROM AuditLogs';Write-Output $cmd.ExecuteScalar()}
 elseif($env:TLS_TEST_MODE -eq 'program-json'){$cmd.CommandText='SELECT ProgramId,Json FROM ProgramOperations ORDER BY ProgramId FOR JSON PATH';Write-Output $cmd.ExecuteScalar()}
 elseif($env:TLS_TEST_MODE -eq 'tables'){$cmd.CommandText='SELECT COUNT(*) FROM sys.tables';Write-Output $cmd.ExecuteScalar()}
} finally {$cn.Dispose()}
'''
        result=subprocess.run(['powershell','-NoProfile','-Command',script],env={**self.control,'TLS_TEST_MODE':mode},capture_output=True,text=True)
        if result.returncode:raise RuntimeError('SQL test database control failed (credentials withheld).')
        return result.stdout
    def command(self,*args,ok=True):
        result=subprocess.run(['dotnet',str(dll),*args],cwd=project,env=self.env,capture_output=True,text=True)
        if ok and result.returncode:raise RuntimeError(result.stdout+result.stderr)
        if not ok:assert result.returncode!=0,'Command should reject unsafe migration'
        return result
    def close(self):
        if not self.closed:
            self._sql('drop');self.closed=True


