using Microsoft.EntityFrameworkCore;
using TLS.Data;
using System.Text.Json;

namespace LiveScore.Api;

public sealed record SbaPlayer(string SbaId,string Name,string Club,string Dob);
public sealed record SbaRankingRow(int Id,string RankingType,SbaPlayer Player1,SbaPlayer? Player2,int AccumulatedScore,int Ranking,int Tournaments,int? YearOfBirth=null,DateTimeOffset? UpdatedAt=null);
public sealed record RankingRequest(long Revision,string ProgramId,string RankingType);

public sealed class TrsIntegration(IConfiguration configuration,IWebHostEnvironment environment,IHttpClientFactory clients)
{
    public async Task<SetupPackage[]> Events(CancellationToken cancellation)
    {
        var api = configuration["Trs:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(api)) return await Read<SetupPackage>("EventFile", cancellation);
        TournamentRules.Need(Uri.TryCreate(api, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "Configure a valid TRS API base URL.");
        var token = configuration["Trs:AccessToken"];
        TournamentRules.Need(!string.IsNullOrWhiteSpace(token), "Configure the TRS access token before loading events.");
        using var client = clients.CreateClient("trs-publication");
        async Task<T> Get<T>(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api.TrimEnd('/') + "/api/tls-export/" + path);
            request.Headers.Authorization = new("Bearer", token);
            using var response = await client.SendAsync(request, cancellation);
            TournamentRules.Need(response.IsSuccessStatusCode, $"TRS event transfer returned HTTP {(int)response.StatusCode}. Check the connection and access token.");
            var data = await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, cancellation);
            TournamentRules.Need(data is not null, "TRS returned an empty event transfer.");
            return data!;
        }
        var events = await Get<TrsEventSummary[]>("events");
        var packages = new List<SetupPackage>();
        foreach (var ev in events) packages.Add(await Get<SetupPackage>("events/" + ev.Id));
        return packages.ToArray();
    }
    public async Task<T[]> Read<T>(string setting,CancellationToken cancellation)
    {
        var file=configuration["Trs:"+setting];
        if(string.IsNullOrWhiteSpace(file))return [];
        var path=Path.IsPathRooted(file)?file:Path.Combine(environment.ContentRootPath,file);
        TournamentRules.Need(File.Exists(path),"The configured TRS transfer file is unavailable.");
        TournamentRules.Need(new FileInfo(path).Length<=5_000_000,"TRS transfer file exceeds 5 MB.");
        await using var stream=File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T[]>(stream,JsonDefaults.Options,cancellation)??[];
    }
    public static void Map(WebApplication app)
    {
        var group=app.MapGroup("/api/trs");
        group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.AddEndpointFilter(async(c,next)=>
        {
            try{return await next(c);}
            catch(Exception ex)when(ex is JsonException or HttpRequestException or TaskCanceledException or IOException)
            {return Results.BadRequest(new{message="TRS data is unavailable or invalid. Check the configured connection or transfer file. Existing TLS records remain available."});}
        });
        group.MapGet("/events",async(TrsIntegration integration,CancellationToken cancellation)=>
        {
            var packages=await integration.Events(cancellation);
            TournamentRules.Need(packages.All(p=>p is not null&&p.SchemaVersion==2&&p.Source=="TRS"&&p.Events is {Count:1}&&p.Programs is not null&&p.Participants is not null&&p.Entries is not null&&p.SourceTimeZone is not null),"The TRS event file must contain complete event transfers using schema version 2.");
            foreach(var p in packages)_=SetupStore.Zone(p.SourceTimeZone);
            return packages;
        });
        group.MapGet("/rankings",(IDbContextFactory<TLSDbContext> factory)=>{using var db=factory.CreateDbContext();return db.SbaRankings.Select(r=>r.RankingType).Distinct().ToList().SelectMany(name=>SeedingEndpoints.Rankings(db,name)).ToArray();});
        group.MapPost("/ranking-snapshot",(RankingRequest request,IDbContextFactory<TLSDbContext> factory,SetupStore store)=>
        {
            var program=SeedingEndpoints.Program(store.Read(),request.ProgramId);
            TournamentRules.Need(program.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&program.EntryType is "S" or "D","Choose a badminton singles or doubles program.");
            TournamentRules.Need(request.RankingType==program.Name,"Program Name must exactly match the local SBA category.");
            using var source=factory.CreateDbContext();var rows=SeedingEndpoints.Rankings(source,program.Name);var fingerprint=SeedingEndpoints.Fingerprint(rows);
            TournamentRules.Need(rows.Count>0,"No rankings were found for this category. You can still assign seeds manually.");
            TournamentRules.Need(rows.All(r=>r.Player1 is not null&&!string.IsNullOrWhiteSpace(r.Player1.SbaId)&&r.Ranking>0&&r.AccumulatedScore>=0&&(r.Player2 is null||!string.IsNullOrWhiteSpace(r.Player2.SbaId))),"The selected SBA ranking records are incomplete.");
            var s=store.Change(request.Revision,"SBA_RANKING_SNAPSHOT",s=>
            {
                var program=s.Programs.SingleOrDefault(p=>p.Id==request.ProgramId);TournamentRules.Need(program is not null&&program.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase),"Choose a badminton program.");
                var plan=TournamentRules.Program(s,request.ProgramId);TournamentRules.Need(plan.Matches.Count==0,"Rankings cannot change after fixture generation.");
                plan.RankingType=request.RankingType;plan.RankingSnapshot=rows;plan.RankingFetchedAt=DateTimeOffset.UtcNow;TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
            },db=>{if(!(SeedingEndpoints.Fingerprint(SeedingEndpoints.Rankings(db,program.Name))==fingerprint))throw new RuleException("SBA rankings changed. Refresh and review again.",409);});
            return new{revision=s.Revision,rankings=rows};
        });
    }
}
public sealed record TrsEventSummary(int Id,string Name,DateOnly StartDate,string Venue);
