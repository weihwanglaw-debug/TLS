using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TLS.Data;
namespace LiveScore.Api;

public sealed record SeedSuggestionRequest(long Revision,int SeedCount,string RankingFingerprint);
public sealed record SeedReviewRequest(long Revision,Dictionary<string,int?> Seeds,string Mode,string? RankingFingerprint=null,int? SeedCount=null);
public static class SeedingEndpoints
{
    internal static SetupProgram Program(SetupData s,string id)
    {
        var p=s.Programs.SingleOrDefault(x=>x.Id==id);
        TournamentRules.Need(p is not null&&p.Status=="A"&&p.EntryType is "S" or "D" or "T","Choose an active singles, doubles or team program.");return p!;
    }
    internal static List<SbaRankingRow> Rankings(TLSDbContext db,string name)=>db.SbaRankings.AsNoTracking().Where(r=>r.RankingType==name).OrderBy(r=>r.SbaRankingId).AsEnumerable().Where(r=>string.Equals(r.RankingType,name,StringComparison.Ordinal)).Select(r=>new SbaRankingRow(r.SbaRankingId,r.RankingType,new(r.Player1SbaId,r.Player1Name,r.Player1Club??"",r.Player1DateOfBirth?.ToString("yyyy-MM-dd")??""),r.Player2SbaId is null?null:new(r.Player2SbaId,r.Player2Name??"",r.Player2Club??"",r.Player2DateOfBirth?.ToString("yyyy-MM-dd")??""),r.AccumulatedScore,r.Ranking,r.Tournaments,r.YearOfBirth,new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt,DateTimeKind.Utc)))).ToList();
    internal static string Fingerprint(List<SbaRankingRow> rows)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows,JsonDefaults.Options))));
    private static SbaRankingRow? Match(SetupData s,SetupProgram p,SetupEntry entry,List<SbaRankingRow> rows)
    {
        if(!p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)||p.EntryType is not ("S" or "D"))return null;
        var ids=entry.ParticipantIds.Select(id=>s.Participants.Single(x=>x.Id==id).SbaId.Trim().ToUpperInvariant()).ToArray();
        var size=p.EntryType=="S"?1:2;
        if(ids.Length!=size||ids.Any(string.IsNullOrWhiteSpace)||ids.Distinct().Count()!=size)return null;
        return rows.FirstOrDefault(r=>size==1?r.Player2 is null&&r.Player1.SbaId.Trim().ToUpperInvariant()==ids[0]:r.Player2 is not null&&new[]{r.Player1.SbaId.Trim().ToUpperInvariant(),r.Player2.SbaId.Trim().ToUpperInvariant()}.ToHashSet().SetEquals(ids));
    }
    private static object View(SetupData s,SetupProgram p,List<SbaRankingRow> rows)
    {
        var plan=TournamentRules.Program(s,p.Id);var locked=plan.Matches.Count>0;
        var entries=s.Entries.Where(e=>e.ProgramId==p.Id&&e.Status=="A").ToList();
        var seeds=entries.ToDictionary(e=>e.Id,e=>plan.Seeds.GetValueOrDefault(e.Id));
        var displayRows=locked?plan.RankingSnapshot:rows;
        var sbaAvailable=rows.Count>0&&entries.Any(e=>Match(s,p,e,rows) is not null);
        return new{seedCount=Math.Max(plan.SeedCount,seeds.Values.Where(v=>v.HasValue).Select(v=>v!.Value).DefaultIfEmpty(0).Max()),sbaAvailable,revision=s.Revision,program=p,eventName=s.Events.Single(e=>e.Id==p.EventId).Name,locked,reviewed=plan.SeedsReviewed,mode=plan.SeedingMode.Length>0?plan.SeedingMode:seeds.Values.Any(x=>x.HasValue)?"M":"N",seeds,importedSeeds=entries.ToDictionary(e=>e.Id,e=>e.Seed),categoryAvailable=rows.Count>0,rankingFingerprint=Fingerprint(rows),rankingFetchedAt=plan.RankingFetchedAt,
            entries=entries.Select(e=>new{e.Id,e.Name,players=e.ParticipantIds.Select(id=>s.Participants.Single(x=>x.Id==id)),ranking=Match(s,p,e,displayRows)})};
    }
    public static void Map(WebApplication app)
    {
        var group=app.MapGroup("/api/seeding");
        group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.MapGet("/{programId}",(string programId,IDbContextFactory<TLSDbContext> factory)=>
        {
            using var db=factory.CreateDbContext();using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            var s=SqlStorage.Read(db);var p=Program(s,programId);var rows=Rankings(db,p.Name);var view=View(s,p,rows);tx.Commit();return view;
        });
        group.MapPost("/{programId}/suggest",(string programId,SeedSuggestionRequest request,IDbContextFactory<TLSDbContext> factory)=>
        {
            using var db=factory.CreateDbContext();using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            var s=SqlStorage.Read(db);SetupStore.CheckRevision(s,request.Revision);var p=Program(s,programId);var plan=TournamentRules.Program(s,p.Id);
            TournamentRules.Need(plan.Matches.Count==0,"Seeding is locked because a draw exists.");
            var rows=Rankings(db,p.Name);TournamentRules.Need(rows.Count>0,"Program Name does not exactly match a category with local SBA rankings.");
            TournamentRules.Need(s.Entries.Any(e=>e.ProgramId==p.Id&&e.Status=="A"&&Match(s,p,e,rows) is not null),"SBA assistance requires eligible badminton entries with matching SBA IDs.");
            var fingerprint=Fingerprint(rows);if(!(fingerprint==request.RankingFingerprint))throw new RuleException("SBA rankings changed. Reload seeding and review the new rankings.",409);
            var entries=s.Entries.Where(e=>e.ProgramId==p.Id&&e.Status=="A").ToList();
            TournamentRules.Need(request.SeedCount>=1&&request.SeedCount<=entries.Count,"Choose a seed count between 1 and the entry count.");
            var matched=entries.Select(e=>new{entry=e,row=Match(s,p,e,rows)}).Where(x=>x.row is not null).OrderByDescending(x=>x.row!.AccumulatedScore).ThenBy(x=>x.row!.Ranking).ThenBy(x=>x.entry.Id,StringComparer.Ordinal).Take(request.SeedCount).ToList();
            var seeds=entries.ToDictionary(e=>e.Id,e=>(int?)null);for(var i=0;i<matched.Count;i++)seeds[matched[i].entry.Id]=i+1;
            tx.Commit();return new{seeds,rankingFingerprint=fingerprint,matched=matched.Count,requested=request.SeedCount};
        });
        group.MapPut("/{programId}",(string programId,SeedReviewRequest request,SetupStore store,IDbContextFactory<TLSDbContext> factory)=>
        {
            TournamentRules.Need(request.Mode is "M" or "N" or "S","Choose manual seeding, SBA assistance or no seeding.");
            var current=store.Read();var program=Program(current,programId);
            using var source=factory.CreateDbContext();var rows=request.Mode=="S"?Rankings(source,program.Name):[];
            if(request.Mode=="S")if(!(rows.Count>0&&Fingerprint(rows)==request.RankingFingerprint))throw new RuleException("SBA rankings changed or no exact category is available. Reload seeding and review again.",409);
            if(request.Mode=="N")TournamentRules.Need(request.Seeds is not null&&request.Seeds.Values.All(v=>!v.HasValue),"No seeding requires every entry to be unseeded.");
            if(request.Mode=="S")TournamentRules.Need(current.Entries.Any(e=>e.ProgramId==programId&&e.Status=="A"&&Match(current,program,e,rows) is not null),"SBA assistance requires eligible badminton entries with matching SBA IDs.");
            var saved=store.Change(request.Revision,"PROGRAM_SEEDS",s=>
            {
                _=Program(s,programId);TournamentRules.SetSeeds(s,new(request.Revision,programId,request.Seeds));var plan=TournamentRules.Program(s,programId);
                var count=request.Mode=="N"?0:request.SeedCount??plan.SeedCount;
                TournamentRules.Need(request.Mode=="N"||(count>=1&&count<=s.Entries.Count(e=>e.ProgramId==programId&&e.Status=="A")),"Choose a seed count between 1 and the entry count.");
                TournamentRules.Need(plan.Seeds.Values.All(v=>!v.HasValue||v<=count),"Seed numbers cannot exceed Number of Seeds.");
                plan.SeedCount=count;plan.SeedingMode=request.Mode;plan.RankingType=request.Mode=="S"?program.Name:"";plan.RankingSnapshot=rows;plan.RankingFetchedAt=request.Mode=="S"?DateTimeOffset.UtcNow:null;
            },db=>
            {
                if(request.Mode=="S")if(!(Fingerprint(Rankings(db,program.Name))==request.RankingFingerprint))throw new RuleException("SBA rankings changed. Reload seeding and review again.",409);
            });
            using var fresh=factory.CreateDbContext();return View(saved,program,Rankings(fresh,program.Name));
        });
    }
}
