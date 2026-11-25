using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TLS.Data;
namespace LiveScore.Api;
internal static class ScheduleStorageUpgrade
{
    public static void Run(TLSDbContext db,string[] args)
    {
        var index=Array.IndexOf(args,"--backup-file");
        if(index<0||index+1>=args.Length)throw new InvalidOperationException("Provide --backup-file with a new JSON backup path before removing saved schedule drafts.");
        var path=Path.GetFullPath(args[index+1]);
        using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        _=db.SystemSettings.FromSqlRaw("SELECT * FROM SystemSettings WITH (XLOCK,HOLDLOCK) WHERE Id=1").Single();
        var rows=db.EventOperations.AsNoTracking().ToList();
        using(var backup=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(backup,rows);
        db.Database.ExecuteSqlRaw(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Sql","20261117_remove_schedule_drafts.sql")));
        tx.Commit();Console.WriteLine($"Scheduling storage upgraded. EventOperations backup: {path}");
    }
}
