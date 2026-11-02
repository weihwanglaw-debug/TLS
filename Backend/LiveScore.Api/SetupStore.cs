using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TLS.Data;
namespace LiveScore.Api;
public sealed class SetupStore(IDbContextFactory<TLSDbContext> factory)
{
    public SetupData Read()
    {
        using var db=factory.CreateDbContext(); using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        var data=SqlStorage.Read(db); tx.Commit(); return data;
    }
    public SetupData Change(long revision,string action,Action<SetupData> edit,Action<TLSDbContext>? persist=null,Func<SetupData,object>? auditView=null)
    {
        using var db=factory.CreateDbContext(); using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        _=db.SystemSettings.FromSqlRaw("SELECT * FROM SystemSettings WITH (XLOCK,HOLDLOCK) WHERE Id=1").Single();
        var data=SqlStorage.Read(db); CheckRevision(data,revision);
        var before=JsonSerializer.Serialize(auditView is null?data:auditView(data),JsonDefaults.Options);
        edit(data); data.Revision++;
        SqlStorage.Write(db,data); persist?.Invoke(db);
        db.AuditLogs.Add(new AuditLog { Id=(db.AuditLogs.Max(x=>(long?)x.Id)??0)+1,At=DateTimeOffset.UtcNow.ToString("O"),Action=action,
            BeforeJson=before,AfterJson=JsonSerializer.Serialize(auditView is null?data:auditView(data),JsonDefaults.Options) });
        db.SaveChanges(); tx.Commit(); return data;
    }
    // Live score versions are per match. They do not invalidate unrelated fixture previews.
    public SetupData ChangeRealtime(string action,Func<SetupData,object?> edit)
    {
        using var db=factory.CreateDbContext();using var tx=db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        _=db.SystemSettings.FromSqlRaw("SELECT * FROM SystemSettings WITH (XLOCK,HOLDLOCK) WHERE Id=1").Single();
        var data=SqlStorage.Read(db);var audit=edit(data);
        if(audit is not null)
        {
            SqlStorage.Write(db,data);
            db.AuditLogs.Add(new AuditLog{Id=(db.AuditLogs.Max(x=>(long?)x.Id)??0)+1,At=DateTimeOffset.UtcNow.ToString("O"),Action=action,BeforeJson="{}",AfterJson=JsonSerializer.Serialize(audit,JsonDefaults.Options)});
            db.SaveChanges();
        }
        tx.Commit();return data;
    }
    public static void CheckRevision(SetupData data,long revision)
    { if(data.Revision!=revision)throw new RuleException("Data changed in another window. Refresh and preview again.",409); }
    public static TimeZoneInfo Zone(string value)
    {
        if (Regex.IsMatch(value ?? "", @"^[+-]\d{2}:\d{2}$"))
        {
            var raw = TimeSpan.ParseExact(value![1..], @"hh\:mm", CultureInfo.InvariantCulture);
            if (value[0] == '-') raw = -raw;
            if (raw.Duration() > TimeSpan.FromHours(14)) throw new RuleException("Invalid time-zone offset.");
            return TimeZoneInfo.CreateCustomTimeZone(value, raw, value, value);
        }
        try { return TimeZoneInfo.FindSystemTimeZoneById(value ?? ""); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new RuleException("Choose a valid master time zone, such as Asia/Singapore or +08:00."); }
    }
    public static string PersonKey(SetupPerson p) => p.EventId + "|" + (p.Name ?? "").Trim().ToUpperInvariant() + "|" + p.Dob.ToString("yyyy-MM-dd");
    public static List<string> Validate(SetupData s)
    {
        var errors = new List<string>();
        void Need(bool ok, string text) { if (!ok && errors.Count < 100) errors.Add(text); }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone(s.TimeZone)).DateTime);
        void Ids<T>(List<T> rows, Func<T,string> id, string kind)
        {
            Need(rows.Count <= 10000, $"Too many {kind} records (maximum 10000).");
            Need(rows.All(x => !string.IsNullOrWhiteSpace(id(x)) && id(x).Length <= 160), $"{kind}: IDs must contain 1-160 characters.");
            Need(rows.Select(id).Distinct(StringComparer.Ordinal).Count() == rows.Count, $"Duplicate {kind} IDs.");
        }
        Ids(s.Events,x=>x.Id,"event"); Ids(s.Programs,x=>x.Id,"program"); Ids(s.Participants,x=>x.Id,"participant"); Ids(s.Entries,x=>x.Id,"entry");
        if(errors.Count>0)return errors;
        var events = s.Events.DistinctBy(x=>x.Id).ToDictionary(x=>x.Id);
        var programs = s.Programs.DistinctBy(x=>x.Id).ToDictionary(x=>x.Id);
        var people = s.Participants.DistinctBy(x=>x.Id).ToDictionary(x=>x.Id);
        bool Text(string? x,int max=200) => !string.IsNullOrWhiteSpace(x) && x.Length <= max;
        foreach(var e in s.Events)
        {
            Need(Text(e.Name) && e.Venue is not null && e.Venue.Length <= 200, $"Event {e.Id}: name is required; name and optional venue must be at most 200 characters.");
            Need(e.StartDate.Year >= 1900 && e.EndDate >= e.StartDate, $"Event {e.Name}: end date must not precede start date.");
            Need(e.MaxProgramsPerParticipant is null or > 0, $"Event {e.Name}: program limit must be positive or blank.");
            Need(e.Status is "D" or "A" or "C" or "X", $"Event {e.Name}: invalid status.");
        }
        foreach(var p in s.Programs)
        {
            Need(events.ContainsKey(p.EventId), $"Program {p.Id}: event does not exist.");
            Need(Text(p.Name) && Text(p.Sport,60), $"Program {p.Id}: name and sport are required.");
            Need(p.EntryType is "S" or "D" or "T", $"Program {p.Name}: invalid entry type.");
            Need(p.MinPlayers >= 1 && p.MaxPlayers >= p.MinPlayers && p.MaxPlayers <= 100, $"Program {p.Name}: invalid roster size.");
            Need(p.EntryType != "S" || p.MinPlayers == 1 && p.MaxPlayers == 1, $"Program {p.Name}: individual entries require one player.");
            Need(p.EntryType != "D" || p.MinPlayers == 2 && p.MaxPlayers == 2, $"Program {p.Name}: pairs require two players.");
            Need(p.MinAge >= 0 && p.MaxAge >= p.MinAge && p.MaxAge <= 150, $"Program {p.Name}: invalid age range.");
            Need(p.Gender is "O" or "M" or "F" or "X", $"Program {p.Name}: invalid gender requirement.");
            Need(p.Gender != "X" || p.MinPlayers == 2 && p.MaxPlayers == 2, $"Program {p.Name}: mixed requires a two-person roster.");
            Need(p.MinCapacity >= 1 && p.MaxCapacity >= p.MinCapacity && p.MaxCapacity <= 10000, $"Program {p.Name}: invalid capacity.");
            Need(p.CapacityUnit is "E" or "P", $"Program {p.Name}: count entries or players.");
            Need(p.Status is "A" or "X", $"Program {p.Name}: invalid status.");
        }
        foreach(var p in s.Participants)
        {
            Need(events.ContainsKey(p.EventId), $"Participant {p.Id}: event does not exist.");
            Need(Text(p.Name), $"Participant {p.Id}: name is required.");
            Need(p.Dob.Year >= 1900 && p.Dob <= today, $"Participant {p.Name}: DOB must be a valid past or current date.");
            Need(p.Gender is "M" or "F", $"Participant {p.Name}: gender is required.");
            Need(Text(p.Email,254) && MailAddress.TryCreate(p.Email, out var email) && email.Address == p.Email, $"Participant {p.Name}: enter a valid email.");
            Need(Text(p.Mobile,40) && Regex.IsMatch(p.Mobile, @"^\+?[0-9()\s-]+$") && p.Mobile.Count(char.IsDigit) is >= 7 and <= 15, $"Participant {p.Name}: enter a valid mobile number including country code where needed.");
            Need((p.Club?.Length ?? 0) <= 200 && (p.Nationality?.Length ?? 0) <= 80 && (p.SbaId?.Length ?? 0) <= 40, $"Participant {p.Name}: club, nationality or SBA ID is too long.");
        }
        foreach(var duplicate in s.Participants.GroupBy(PersonKey).Where(x=>x.Count()>1))
            Need(false,$"{duplicate.First().Name}: matching name and DOB already exist. Use one participant ID and reference it in each program entry.");
        var seen = new HashSet<string>();
        var counts = new Dictionary<string,int>();
        var personPrograms = new Dictionary<string,HashSet<string>>();
        foreach(var entry in s.Entries)
        {
            Need(entry.Status is "A" or "X", $"Entry {entry.Id}: invalid status.");
            Need(entry.Seed is null or > 0, $"Entry {entry.Id}: seed must be positive or blank.");
            if (!programs.TryGetValue(entry.ProgramId,out var program)) { Need(false,$"Entry {entry.Id}: program does not exist."); continue; }
            Need(entry.EventId == program.EventId, $"Entry {entry.Id}: program belongs to another event.");
            var ids = entry.ParticipantIds ?? [];
            Need(ids.Length > 0 && ids.Length == ids.Distinct().Count(), $"Entry {entry.Id}: select distinct participants.");
            var roster = ids.Where(people.ContainsKey).Select(x=>people[x]).ToList();
            Need(roster.Count == ids.Length && roster.All(x=>x.EventId == entry.EventId), $"Entry {entry.Id}: participants must belong to this event.");
            if (entry.Status != "A") continue;
            Need(program.Status == "A", $"Entry {entry.Id}: program is withdrawn.");
            Need(roster.Count >= program.MinPlayers && roster.Count <= program.MaxPlayers, $"Entry {entry.Id}: {program.Name} requires {program.MinPlayers}-{program.MaxPlayers} players.");
            foreach(var person in roster)
            {
                var age = today.Year - person.Dob.Year; if (person.Dob > today.AddYears(-age)) age--;
                Need(age >= program.MinAge && age <= program.MaxAge, $"{person.Name}: age {age} is outside {program.Name}'s {program.MinAge}-{program.MaxAge} range.");
                Need(program.Gender is "O" or "X" || person.Gender == program.Gender, $"{person.Name}: gender does not meet {program.Name}'s requirement.");
                Need(seen.Add(entry.ProgramId + "|" + PersonKey(person)), $"{person.Name} is already assigned to {program.Name}.");
                var key = PersonKey(person);
                if (!personPrograms.TryGetValue(key,out var set)) personPrograms[key] = set = [];
                set.Add(program.Id);
                if(events.TryGetValue(entry.EventId,out var ev) && ev.MaxProgramsPerParticipant is int max)
                    Need(set.Count <= max, $"{person.Name}: maximum {max} programs per participant exceeded.");
            }
            Need(program.Gender != "X" || roster.Count(x=>x.Gender=="M")==1 && roster.Count(x=>x.Gender=="F")==1, $"Entry {entry.Id}: mixed pairs require one male and one female.");
            counts[program.Id] = counts.GetValueOrDefault(program.Id) + (program.CapacityUnit == "P" ? roster.Count : 1);
        }
        foreach(var p in s.Programs)
        {
            Need(counts.GetValueOrDefault(p.Id) <= p.MaxCapacity, $"{p.Name}: maximum capacity {p.MaxCapacity} exceeded.");
            var seeds = s.Entries.Where(e=>e.ProgramId==p.Id && e.Status=="A" && e.Seed!=null).Select(e=>e.Seed);
            Need(seeds.Distinct().Count()==seeds.Count(), $"{p.Name}: duplicate seed numbers.");
        }
        return errors;
    }
}
