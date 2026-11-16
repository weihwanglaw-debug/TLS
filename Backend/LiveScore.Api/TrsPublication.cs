using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TLS.Data;

namespace LiveScore.Api;

public sealed class TrsPublication(SetupStore store, IDbContextFactory<TLSDbContext> factory, IHttpClientFactory clients,
    IDataProtectionProvider protection, ILogger<TrsPublication> logger) : BackgroundService
{
    private readonly IDataProtector protector = protection.CreateProtector("TLS.TRS.Publication.v1");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, PublicationFailure> failures = new();
    private readonly Dictionary<string, DateTimeOffset> liveAt = [];
    private readonly Dictionary<string, string> resultSignatures = [];
    private readonly Dictionary<string, (string Signature, DateTimeOffset Next)> resultRetries = [];
    private readonly Dictionary<string, string> liveSignatures = [];
    public PublicationFailure[] Failures(string? eventId) => failures.Values.Where(f => string.IsNullOrEmpty(eventId) || f.EventId == eventId).OrderBy(f => f.At).ToArray();

    public object Settings(SetupData s, string eventId)
    {
        var p = TournamentRules.Event(s, eventId).Publication;
        return new { s.Revision, p.Enabled, p.LiveUrl, p.ResultsUrl, p.IntervalSeconds, hasCredential = p.ProtectedCredential.Length > 0,
            apiKey = p.ProtectedCredential.Length == 0 ? "" : protector.Unprotect(p.ProtectedCredential),
            source = s.Imports.SingleOrDefault(i => i.EventId == eventId)?.Source, failures = Failures(eventId) };
    }

    public object Save(string eventId, TrsPublicationRequest request)
    {
        var s = store.Change(request.Revision, "TRS_PUBLICATION_SETTINGS", s => ApplySettings(s, eventId, request), auditView: s => {
            var p = TournamentRules.Event(s, eventId).Publication;
            return new { eventId, p.Enabled, p.LiveUrl, p.ResultsUrl, p.IntervalSeconds, hasCredential = p.ProtectedCredential.Length > 0 };
        });
        if (!request.Enabled) foreach (var key in failures.Where(f => f.Value.EventId == eventId).Select(f => f.Key)) failures.TryRemove(key, out _);
        return Settings(s, eventId);
    }

    private void ApplySettings(SetupData s, string eventId, TrsPublicationRequest request)
    {
        TournamentRules.Need(request.IntervalSeconds is >= 2 and <= 120, "Choose an interval between 2 and 120 seconds.");
        TournamentRules.Need(request.LiveUrl is not null && request.ResultsUrl is not null && request.ApiKey is not null && request.ApiKey.Length <= 512, "Invalid publishing settings.");
        TournamentRules.Need(request.ApiKey.Trim().All(c => c is >= '!' and <= '~'), "Use a credential without spaces or control characters.");
        if (request.Enabled)
        {
            ValidateUrl(request.LiveUrl); ValidateUrl(request.ResultsUrl.Replace("{programId}", "1"));
        }
        TournamentRules.Need(s.Imports.Any(i => i.EventId == eventId && i.Source == "TRS"), "Publishing requires an event imported from TRS.");
        if (request.Enabled)
        {
            _ = SourceId(s, eventId, eventId, "event");
            foreach (var program in s.Programs.Where(p => p.EventId == eventId)) _ = SourceId(s, eventId, program.Id, "program");
        }
        var old = TournamentRules.Event(s, eventId).Publication;
        var key = string.IsNullOrWhiteSpace(request.ApiKey) ? old.ProtectedCredential : protector.Protect(request.ApiKey.Trim());
        TournamentRules.Need(!request.Enabled || key.Length > 0 && (string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Trim().Length >= 32), "Enter an API credential of at least 32 characters.");
        var plan = TournamentRules.Event(s, eventId);
        plan.Publication = new() { Enabled = request.Enabled, LiveUrl = request.LiveUrl.Trim(), ResultsUrl = request.ResultsUrl.Trim(), IntervalSeconds = request.IntervalSeconds, ProtectedCredential = key };
        TournamentRules.Save(s.EventPlans, plan, p => p.EventId);
    }

    public object SaveEvent(EventSettingsRequest request)
    {
        TournamentRules.Need(request.Plan is not null, "Event setup is required.");
        var eventId = request.Plan.EventId;
        var s = store.Change(request.Revision, "EVENT_SETTINGS", s => {
            var ev = s.Events.SingleOrDefault(e => e.Id == eventId);
            TournamentRules.Need(ev is not null, "Event not found.");
            if (ev.StartDate != request.StartDate || ev.EndDate != request.EndDate)
                EventDates.Update(s, new(request.Revision, eventId, request.StartDate, request.EndDate));
            var plan = TournamentRules.Event(s, eventId);
            plan.MatchMinutes = request.Plan.MatchMinutes;
            plan.RestMinutes = 0;
            plan.Officials = request.Plan.Officials;
            BusinessIds.Officials(s, plan);
            TournamentRules.ValidateEvent(s, plan);
            TournamentRules.Save(s.EventPlans, plan, p => p.EventId);
            if (request.Publishing is not null) ApplySettings(s, eventId, request.Publishing);
        }, auditView: s => {
            var p = TournamentRules.Event(s, eventId);
            var ev = s.Events.Single(e => e.Id == eventId);
            return new { eventId, ev.StartDate, ev.EndDate, p.MatchMinutes, p.RestMinutes, p.Officials,
                p.Publication.Enabled, p.Publication.LiveUrl, p.Publication.ResultsUrl, p.Publication.IntervalSeconds,
                hasCredential = p.Publication.ProtectedCredential.Length > 0 };
        });
        if (!TournamentRules.Event(s, eventId).Publication.Enabled)
            foreach (var key in failures.Where(f => f.Value.EventId == eventId).Select(f => f.Key)) failures.TryRemove(key, out _);
        return TournamentEndpoints.View(s, eventId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var enabled = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM EventOperations WHERE JSON_VALUE(Json, '$.publication.enabled')='true'").SingleAsync(stoppingToken);
                if (enabled > 0) await Publish(null, false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "TRS publication cycle failed"); }
            try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task Publish(string? eventId, bool force, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var s = store.Read();
            foreach (var ev in s.Events.Where(e => eventId is null || e.Id == eventId))
            {
                var settings = TournamentRules.Event(s, ev.Id).Publication;
                if (!settings.Enabled) { foreach(var program in s.Programs.Where(p=>p.EventId==ev.Id)){resultSignatures.Remove(program.Id);resultRetries.Remove(program.Id);} foreach (var key in failures.Where(f => f.Value.EventId == ev.Id).Select(f => f.Key)) failures.TryRemove(key, out _); continue; }
                var live = Snapshot(s, ev.Id, null, true);
                var signature = Signature(live);
                var now = DateTimeOffset.UtcNow;
                if (force || !liveAt.TryGetValue(ev.Id, out var previous) || (now - previous).TotalSeconds >= settings.IntervalSeconds
                    || live.Matches.Length == 0 && liveSignatures.GetValueOrDefault(ev.Id) != signature)
                {
                    liveAt[ev.Id] = now; liveSignatures[ev.Id] = signature;
                    await Send(s, ev.Id, settings, live, true, ct);
                }
                foreach (var program in s.Programs.Where(p => p.EventId == ev.Id))
                {
                    var result = Snapshot(s, ev.Id, program.Id, false);
                    var fingerprint = DeliverySignature(settings,result);
                    if (!force && resultSignatures.GetValueOrDefault(program.Id) == fingerprint) continue;
                    if (!force && resultRetries.TryGetValue(program.Id,out var retry) && retry.Signature==fingerprint && retry.Next>DateTimeOffset.UtcNow) continue;
                    if(await Send(s, ev.Id, settings, result, false, ct)){resultSignatures[program.Id]=fingerprint;resultRetries.Remove(program.Id);}
                    else resultRetries[program.Id]=(fingerprint,DateTimeOffset.UtcNow.AddSeconds(Math.Max(5,settings.IntervalSeconds)));
                }
            }
        }
        finally { gate.Release(); }
    }

    public async Task<PublicationFailure[]> ResendResults(string eventId, string? programId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var s = store.Read();
            var settings = TournamentRules.Event(s, eventId).Publication;
            TournamentRules.Need(settings.Enabled && !string.IsNullOrWhiteSpace(settings.ResultsUrl) && settings.ProtectedCredential.Length > 0,
                "Configure and enable Score Publishing before resending results.");
            var programs = s.Programs.Where(p => p.EventId == eventId && (programId is null || p.Id == programId)).ToArray();
            TournamentRules.Need(programs.Length > 0, "Choose a program belonging to this event.");
            var results = programs.Select(p => (Program: p, Snapshot: Snapshot(s, eventId, p.Id, false))).ToArray();
            TournamentRules.Need(results.Length > 0, "No confirmed results are available to send.");
            var errors = new List<PublicationFailure>();
            foreach (var row in results)
            {
                var fingerprint=DeliverySignature(settings,row.Snapshot);
                if(await Send(s, eventId, settings, row.Snapshot, false, ct)){resultSignatures[row.Program.Id]=fingerprint;resultRetries.Remove(row.Program.Id);}
                else resultRetries[row.Program.Id]=(fingerprint,DateTimeOffset.UtcNow.AddSeconds(Math.Max(5,settings.IntervalSeconds)));
                if (failures.TryGetValue(eventId + ":results:" + row.Snapshot.ProgramId, out var failure)) errors.Add(failure);
            }
            return errors.ToArray();
        }
        finally { gate.Release(); }
    }

    private async Task<bool> Send(SetupData s, string eventId, TrsPublicationSettings settings, TrsScoreSnapshot snapshot, bool live, CancellationToken ct)
    {
        var kind = live ? "live" : "results";
        var failureKey = eventId + ":" + kind + (live ? "" : ":" + snapshot.ProgramId);
        try
        {
            var url = live ? settings.LiveUrl : settings.ResultsUrl.Replace("{programId}", snapshot.ProgramId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ValidateUrl(url);
            snapshot = snapshot with { Version = ReserveVersion(eventId, live ? "live" : "result:" + snapshot.ProgramId), GeneratedAt = DateTimeOffset.UtcNow };
            using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(snapshot) };
            request.Headers.Add("X-TLS-Key", protector.Unprotect(settings.ProtectedCredential));
            using var response = await clients.CreateClient("trs-publication").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"TRS returned HTTP {(int)response.StatusCode}.");
            failures.TryRemove(failureKey, out _);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or RuleException)
        {
            if (ct.IsCancellationRequested) throw;
            failures[failureKey] = new(eventId, kind, ex is HttpRequestException ? ex.Message : "Unable to publish. Check the API URL, credential and connection.", DateTimeOffset.UtcNow);
            return false;
        }
    }

    private long ReserveVersion(string eventId, string stream)
    {
        using var db = factory.CreateDbContext(); using var tx = db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        _ = db.SystemSettings.FromSqlRaw("SELECT * FROM SystemSettings WITH (XLOCK,HOLDLOCK) WHERE Id=1").Single();
        var key = "publish:" + eventId + ":" + stream;
        var row = db.IdSequences.SingleOrDefault(x => x.Key == key);
        if (row is null) { row = new() { Key = key }; db.IdSequences.Add(row); }
        row.Value = Math.Max(checked(row.Value + 1), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
        db.SaveChanges(); tx.Commit(); return row.Value;
    }

    private static string DeliverySignature(TrsPublicationSettings settings,TrsScoreSnapshot snapshot)=>LiveAccess.Hash(settings.ResultsUrl+"|"+settings.ProtectedCredential+"|"+Signature(snapshot));
    private static string Signature(TrsScoreSnapshot snapshot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot with { GeneratedAt = default, Version = 0 }))));
    private static void ValidateUrl(string url) => TournamentRules.Need(Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && url.Length <= 2000, "Enter a valid HTTP or HTTPS publishing URL.");

    public static int SourceId(SetupData s, string eventId, string localId, string kind)
    {
        var id = s.ImportIdentities.Values.SingleOrDefault(x => x.EventId == eventId && x.LocalId == localId && x.Kind == kind)?.SourceId;
        if (kind == "event") id ??= s.Imports.SingleOrDefault(i => i.EventId == eventId)?.SourceEventId;
        var raw = id?.Split(':').Last();
        TournamentRules.Need(int.TryParse(raw, out var value) && value > 0, "Original TRS identity is unavailable. Re-import the event before publishing.");
        return value;
    }

    public static TrsScoreSnapshot Snapshot(SetupData s, string eventId, string? programId, bool live)
    {
        var ev = s.Events.Single(e => e.Id == eventId);
        var settings = TournamentRules.Event(s, eventId).Publication;
        var programs = s.Programs.Where(p => p.EventId == eventId && (programId is null || p.Id == programId)).ToDictionary(p => p.Id);
        SpectatorEntry Entry(string? id)
        {
            var entry = s.Entries.SingleOrDefault(e => e.Id == id);
            var players = entry?.ParticipantIds.Select(pid => s.Participants.Single(p => p.Id == pid).Name).ToArray() ?? [];
            var sourceId = s.ImportIdentities.Values.SingleOrDefault(x => x.EventId == eventId && x.LocalId == id && x.Kind == "entry")?.SourceId ?? id ?? "BYE";
            return new(sourceId, !string.IsNullOrWhiteSpace(entry?.Name) ? entry.Name : players.Length > 0 ? string.Join(" / ", players) : "BYE", players, entry?.Seed);
        }
        var matches = TournamentScheduler.Matches(s).Where(m => programs.ContainsKey(m.ProgramId) && (live ? MatchStatus.Occupied(m.Status) : MatchStatus.Finished(m.Status)))
            .OrderBy(m => m.Phase == "G" ? 0 : 1).ThenBy(m => m.Round).ThenBy(m => m.Number).Select(m => {
                var plan = s.ProgramPlans.Single(p => p.ProgramId == m.ProgramId);
                var row = LiveRuntime.Row(s, m);
                var date = m.ActualStartedAt ?? m.Start;
                var finish = m.ActualFinishedAt ?? m.End;
                return new SpectatorMatch(m.Id, SourceId(s, eventId, m.ProgramId, "program"), programs[m.ProgramId].Name,
                    m.Phase, m.Round, m.Phase == "G" ? "Group Stage" : m.RoundLabel,
                    plan.Groups.FirstOrDefault(g => g.Id == m.GroupId)?.Name ?? m.GroupId ?? "", Entry(m.EntryA), Entry(m.EntryB),
                    m.Games, m.WinnerId == m.EntryA ? "A" : m.WinnerId is not null && m.WinnerId == m.EntryB ? "B" : null,
                    m.Status == MatchStatus.Bye ? "BY" : m.Status == MatchStatus.Walkover ? "WO" : m.ResultKind,
                    "", date?.ToString("yyyy-MM-dd") ?? "", date?.ToString("HH:mm") ?? "", finish?.ToString("HH:mm") ?? "",
                    m.Court?.ToString() ?? "", m.Status == MatchStatus.Walkover ? "W" : m.Status, live ? row.ScoreA : null, live ? row.ScoreB : null, live ? m.Live?.Serving?.ServerSide : null);
            }).ToArray();
        return new(1, SourceId(s, eventId, eventId, "event"), ev.Name, programId is null ? null : SourceId(s, eventId, programId, "program"),
            programId is null ? "" : programs[programId].Name, 0, DateTimeOffset.UtcNow, s.TimeZone, settings.IntervalSeconds, matches);
    }

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/trs/publishing");
        group.AddEndpointFilter(async (c, next) => LiveAccess.LocalAdmin(c.HttpContext) ? await next(c) : Results.StatusCode(403));
        group.MapGet("/{eventId}", (string eventId, SetupStore store, TrsPublication publisher) => publisher.Settings(store.Read(), eventId));
        group.MapPut("/{eventId}", (string eventId, TrsPublicationRequest request, TrsPublication publisher) => publisher.Save(eventId, request));
        group.MapPost("/{eventId}/resend", async (string eventId, string? programId, TrsPublication publisher, CancellationToken ct) => new { failures = await publisher.ResendResults(eventId, programId, ct) });
        group.MapGet("/status/all", (string? eventId, TrsPublication publisher) => publisher.Failures(eventId));
    }
}
