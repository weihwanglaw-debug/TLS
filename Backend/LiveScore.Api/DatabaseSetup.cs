using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TLS.Data;
namespace LiveScore.Api;
internal static class DatabaseSetup
{
    public static void Run(TLSDbContext db,string[] args)
    {
        var catalog=new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
        if(catalog!="TLS" && !System.Text.RegularExpressions.Regex.IsMatch(catalog,"^TLS_Test_[a-f0-9]{32}$"))throw new InvalidOperationException("Setup requires TLS or an isolated test database.");
        if(args.Contains("--schema-script")){Console.WriteLine(db.Database.GenerateCreateScript());return;}
        if(args.Contains("--initialize-sql"))db.Database.EnsureCreated();
        if(args.Contains("--upgrade-imports"))db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261012_import_only.sql")));
        if(args.Contains("--upgrade-tournament"))db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261012_tournament.sql")));
        if(args.Contains("--upgrade-live"))db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261013_live.sql")).Replace("{","{{").Replace("}","}}"));
        if(args.Contains("--upgrade-sba"))db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261021_sba_rankings.sql")));
        if(args.Contains("--upgrade-business-ids"))db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261028_business_ids.sql")));
        if(args.Contains("--clear-test-records")){TestRecordCleanup.Run(db);return;}
        _=db.SystemSettings.Single(x=>x.Id==1); _=db.EventImports.Count();
        Console.WriteLine("TLS import schema ready.");
    }
}
