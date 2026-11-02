using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TLS.Data;

namespace LiveScore.Api;

internal static class SqlStorage
{
    // Transfer scalar fields without coupling API request types to persistence entities.
    private static T Map<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static SetupData Read(TLSDbContext db)
    {
        var settings = db.SystemSettings.Single(x => x.Id == 1);
        var members = db.EntryMembers.OrderBy(x => x.Position).ToList().ToLookup(x => x.EntryId);
        var data=new SetupData
        {
            IdCounters=db.IdSequences.AsNoTracking().ToDictionary(x=>x.Key,x=>x.Value),
            ImportIdentities=db.ImportIdMappings.AsNoTracking().ToDictionary(x=>x.Fingerprint,x=>new ImportedIdentity(x.EventId,x.Kind,x.SourceId,x.LocalId)),
            Venue=JsonSerializer.Deserialize<LiveVenue>(settings.LiveVenueJson,JsonDefaults.Options)!,
            CourtCount=settings.CourtCount,
            EventPlans=db.EventOperations.AsEnumerable().Select(x=>JsonSerializer.Deserialize<EventPlan>(x.Json,JsonDefaults.Options)!).ToList(),
            ProgramPlans=db.ProgramOperations.AsEnumerable().Select(x=>JsonSerializer.Deserialize<ProgramPlan>(x.Json,JsonDefaults.Options)!).ToList(),
            Revision = settings.Revision, TimeZone = settings.TimeZone, AppName=settings.AppName,LogoLightUrl=settings.LogoLightUrl,LogoDarkUrl=settings.LogoDarkUrl,
            Events = db.Events.OrderBy(x => x.SortOrder).AsEnumerable().Select(Map<SetupEvent>).ToList(),
            Programs = db.Programs.OrderBy(x => x.SortOrder).AsEnumerable().Select(Map<SetupProgram>).ToList(),
            Participants = db.Participants.OrderBy(x => x.SortOrder).AsEnumerable().Select(Map<SetupPerson>).ToList(),
            Entries = db.ProgramEntries.OrderBy(x => x.SortOrder).AsEnumerable().Select(x => new SetupEntry(x.Id, x.EventId, x.ProgramId,
                members[x.Id].Select(m => m.ParticipantId).ToArray(), x.Name, x.Seed, x.Status)).ToList(),
            Imports = db.EventImports.AsEnumerable().Select(x => new ImportInfo(x.EventId,x.Source,x.SourceSystemId,x.SourceEventId,x.SourceTimeZone,x.ExportedAt,x.ImportedAt,x.Stage)).ToList()
        };
        foreach(var operation in db.ProgramOperations.AsNoTracking())
        {
            using var document=JsonDocument.Parse(operation.Json);
            foreach(var item in document.RootElement.GetProperty("matches").EnumerateArray())
            {
                var savedMatch=data.ProgramPlans.SelectMany(p=>p.Matches).Single(m=>m.Id==item.GetProperty("id").GetString());
                if(!item.TryGetProperty("reviewAvailable",out _))
                {var eventId=data.Programs.Single(p=>p.Id==savedMatch.ProgramId).EventId;savedMatch.ReviewAvailable=TournamentRules.Event(data,eventId).ReviewCourts.Contains(savedMatch.Court??0);}
                if(!item.TryGetProperty("rulesAssigned",out _))
                {var id=item.GetProperty("id").GetString();var match=data.ProgramPlans.SelectMany(p=>p.Matches).Single(m=>m.Id==id);match.RulesAssigned=true;}
            }
        }
        var matches=TournamentScheduler.Matches(data).ToDictionary(m=>m.Id);
        foreach(var row in db.MatchLiveRecords.AsNoTracking())
        {
            if(!matches.TryGetValue(row.MatchId,out var match)||match.ProgramId!=row.ProgramId)throw new InvalidDataException("Live match linkage is invalid.");
            var saved=JsonSerializer.Deserialize<SavedLiveMatch>(row.Json,JsonDefaults.Options)!;
            match.Live=saved.State;match.Status=row.Status;match.Games=saved.Games;match.WinnerId=saved.WinnerId;match.ActualFinishedAt=saved.ActualFinishedAt;
        }
        return data;
    }
    public static void Write(TLSDbContext db, SetupData s)
    {
        // Mark removed import metadata before deleting its parent event (identifying FK).
        var importedEvents=s.Imports.Select(x=>x.EventId).ToHashSet();
        db.EventImports.RemoveRange(db.EventImports.Where(x=>!importedEvents.Contains(x.EventId)));
        Sync(db,s.IdCounters.Select(x=>new IdSequence{Key=x.Key,Value=x.Value}),x=>x.Key);
        Sync(db,s.ImportIdentities.Select(x=>new ImportIdMapping{Fingerprint=x.Key,EventId=x.Value.EventId,Kind=x.Value.Kind,SourceId=x.Value.SourceId,LocalId=x.Value.LocalId}),x=>x.Fingerprint);
        var settings = db.SystemSettings.Single(x => x.Id == 1);
        Sync(db,TournamentScheduler.Matches(s).Where(m=>m.Live is not null).Select(m=>new MatchLiveRecord{MatchId=m.Id,ProgramId=m.ProgramId,Status=m.Status,Json=JsonSerializer.Serialize(new SavedLiveMatch(m.Live!,m.Games,m.WinnerId,m.ActualFinishedAt),JsonDefaults.Options)}),m=>m.MatchId);
        var liveMatches=TournamentScheduler.Matches(s).Where(m=>m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review).Select(m=>m.Id).ToHashSet();
        s.Venue.Bindings.RemoveAll(b=>!liveMatches.Contains(b.MatchId));
        settings.LiveVenueJson=JsonSerializer.Serialize(s.Venue,JsonDefaults.Options);
        settings.CourtCount=s.CourtCount;
        Sync(db,s.ProgramPlans.Select(p=>new ProgramOperation{ProgramId=p.ProgramId,Json=ProgramJson(p)}),x=>x.ProgramId);
        Sync(db,s.EventPlans.Select(p=>new EventOperation{EventId=p.EventId,Json=JsonSerializer.Serialize(p,JsonDefaults.Options)}),x=>x.EventId);
        settings.Revision = s.Revision; settings.TimeZone = s.TimeZone; settings.AppName=s.AppName;settings.LogoLightUrl=s.LogoLightUrl;settings.LogoDarkUrl=s.LogoDarkUrl;
        Sync(db, s.Entries.SelectMany(x => x.ParticipantIds.Select((id, i) => new EntryMember { EntryId = x.Id, Position = i, ParticipantId = id })), x => (x.EntryId, x.Position));
        Sync(db, s.Entries.Select((x, i) => { var e = Map<EntryRecord>(x); e.SortOrder = i; return e; }), x => x.Id);
        Sync(db, s.Participants.Select((x, i) => { var e = Map<ParticipantRecord>(x); e.SortOrder = i; return e; }), x => x.Id);
        Sync(db, s.Programs.Select((x, i) => { var e = Map<ProgramRecord>(x); e.SortOrder = i; return e; }), x => x.Id);
        Sync(db, s.Events.Select((x, i) => { var e = Map<EventRecord>(x); e.SortOrder = i; return e; }), x => x.Id);
    }
    private static string ProgramJson(ProgramPlan plan)
    {
        // Rally fields live in their own row; only confirmed results change the fixture document.
        var json=JsonSerializer.SerializeToNode(plan,JsonDefaults.Options)!;
        foreach(var item in json["matches"]!.AsArray())
        {
            foreach(var field in new[]{"paperMode","matchVersion","resultDetails","actualStartedAt","actualDurationMinutes","resultSource","currentGame"})item!.AsObject().Remove(field);
            if(item!["status"]!.GetValue<string>() is MatchStatus.Playing or MatchStatus.Review)
            {item["status"]=MatchStatus.Scheduled;item["games"]=new System.Text.Json.Nodes.JsonArray();item["winnerId"]=null;item["actualFinishedAt"]=null;}
        }
        return json.ToJsonString(JsonDefaults.Options);
    }
    private static void Sync<T, TKey>(TLSDbContext db, IEnumerable<T> values, Func<T, TKey> key) where T : class where TKey : notnull
    {
        var existing = db.Set<T>().ToList().ToDictionary(key);
        foreach (var value in values)
        {
            if (existing.Remove(key(value), out var old)) db.Entry(old).CurrentValues.SetValues(value);
            else db.Add(value);
        }
        db.RemoveRange(existing.Values);
    }
}
