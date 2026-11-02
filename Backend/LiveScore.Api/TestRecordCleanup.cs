using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TLS.Data;

namespace LiveScore.Api;

internal static class TestRecordCleanup
{
    // Explicit local maintenance command, never run during normal startup.
    public static void Run(TLSDbContext db)
    {
        using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        _=db.SystemSettings.FromSqlRaw("SELECT * FROM SystemSettings WITH (XLOCK,HOLDLOCK) WHERE Id=1").Single();
        var s=SqlStorage.Read(db);
        string Masters()=>JsonSerializer.Serialize(new{s.CourtCount,s.TimeZone,s.AppName,s.LogoLightUrl,s.LogoDarkUrl,s.Venue.ClosedCourts,presets=s.Venue.Presets.Select(p=>new{p.Name,p.Status,p.IsDefault,p.Rules}),rankings=db.SbaRankings.AsNoTracking().OrderBy(x=>x.SbaRankingId).ToArray(),clubs=db.BadmintonClubs.AsNoTracking().OrderBy(x=>x.ClubId).ToArray()},JsonDefaults.Options);
        var masters=Masters();
        var before=new{events=s.Events.Count,programs=s.Programs.Count,participants=s.Participants.Count,entries=s.Entries.Count,matches=TournamentScheduler.Matches(s).Count(),imports=s.Imports.Count,bindings=s.Venue.Bindings.Count,audits=db.AuditLogs.Count()};
        s.Events.Clear();s.Programs.Clear();s.Participants.Clear();s.Entries.Clear();s.EventPlans.Clear();s.ProgramPlans.Clear();s.Imports.Clear();s.ImportIdentities.Clear();s.Venue.Bindings.Clear();s.Revision++;
        // Retain counters even on repeated cleanups: previously committed IDs stay retired.
        var current=s.Venue.Presets.Where(p=>Regex.IsMatch(p.Id,"^GP[0-9]+$")).Select(p=>long.Parse(p.Id[2..])).DefaultIfEmpty(0).Max();
        s.IdCounters["GP"]=Math.Max(current,s.IdCounters.GetValueOrDefault("GP"));
        foreach(var preset in s.Venue.Presets.Where(p=>!Regex.IsMatch(p.Id,"^GP[0-9]+$")))preset.Id=BusinessIds.Next(s,"GP",4);
        db.EventImports.RemoveRange(db.EventImports.ToList());
        db.AuditLogs.ExecuteDelete();
        SqlStorage.Write(db,s);
        db.AuditLogs.Add(new AuditLog{Id=1,At=DateTimeOffset.UtcNow.ToString("O"),Action="TEST_RECORDS_CLEARED",BeforeJson=JsonSerializer.Serialize(before),AfterJson=JsonSerializer.Serialize(new{masterDataPreserved=true,presets=s.Venue.Presets.Select(p=>new{p.Id,p.Name}),revision=s.Revision})});
        db.SaveChanges();
        if(masters!=Masters())throw new InvalidOperationException("Master-data verification failed; cleanup rolled back.");
        if(db.Events.Any()||db.Programs.Any()||db.Participants.Any()||db.ProgramEntries.Any()||db.EntryMembers.Any()||db.EventImports.Any()||db.EventOperations.Any()||db.ProgramOperations.Any()||db.MatchLiveRecords.Any()||db.ImportIdMappings.Any())throw new InvalidOperationException("Event cleanup verification failed; cleanup rolled back.");
        tx.Commit();Console.WriteLine(JsonSerializer.Serialize(new{cleared=before,masterDataPreserved=true,presets=s.Venue.Presets.Select(p=>new{p.Id,p.Name}),rankings=db.SbaRankings.Count(),clubs=db.BadmintonClubs.Count()},JsonDefaults.Options));
    }
}
