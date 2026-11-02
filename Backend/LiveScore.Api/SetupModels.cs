using System.Text.Json;
namespace LiveScore.Api;
public sealed class SetupData
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string,long> IdCounters {get;set;}=[];
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string,ImportedIdentity> ImportIdentities {get;set;}=[];
    public LiveVenue Venue {get;set;}=new();
    public int CourtCount { get; set; } = 4;
    public List<EventPlan> EventPlans { get; set; } = [];
    public List<ProgramPlan> ProgramPlans { get; set; } = [];
    public long Revision { get; set; }
    public string TimeZone { get; set; } = "Asia/Singapore";
    public string AppName { get; set; } = "TLS";
    public string LogoLightUrl { get; set; } = "";
    public string LogoDarkUrl { get; set; } = "";
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SetupStore.Zone(TimeZone)).DateTime);
    public List<SetupEvent> Events { get; set; } = [];
    public List<SetupProgram> Programs { get; set; } = [];
    public List<SetupPerson> Participants { get; set; } = [];
    public List<SetupEntry> Entries { get; set; } = [];
    public List<ImportInfo> Imports { get; set; } = [];
}
public sealed record SetupEvent(string Id, string Name, string Venue, DateOnly StartDate, DateOnly EndDate,
    int? MaxProgramsPerParticipant = null, string Status = "D");
public sealed record SetupProgram(string Id, string EventId, string Name, string Sport, string EntryType,
    int MinPlayers, int MaxPlayers, int MinAge, int MaxAge, string Gender,
    int MinCapacity, int MaxCapacity, string CapacityUnit, string Status = "A");
public sealed record SetupPerson(string Id, string EventId, string Name, DateOnly Dob, string Gender,
    string Email, string Mobile, string Club = "", string Nationality = "", string SbaId = "");
public sealed record SetupEntry(string Id, string EventId, string ProgramId, string[] ParticipantIds,
    string Name = "", int? Seed = null, string Status = "A");

public sealed class SetupPackage
{
    public int SchemaVersion { get; set; }
    public string Source { get; set; } = "";
    public string SourceSystemId { get; set; } = "";
    public string SourceTimeZone { get; set; } = "";
    public DateTimeOffset ExportedAt { get; set; }
    public bool? RegistrationClosed { get; set; }
    public bool? HasFixtures { get; set; }
    public List<SetupEvent> Events { get; set; } = [];
    public List<SetupProgram> Programs { get; set; } = [];
    public List<SetupPerson> Participants { get; set; } = [];
    public List<SetupEntry> Entries { get; set; } = [];
}
public sealed record ImportInfo(string EventId,string Source,string SourceSystemId,string SourceEventId,string SourceTimeZone,DateTimeOffset ExportedAt,DateTimeOffset ImportedAt,string Stage);
public sealed record SetupSettings(long Revision,string TimeZone,string? AppName=null,string? LogoLightUrl=null,string? LogoDarkUrl=null,int? CourtCount=null);
public sealed record SetupConfirm(long Revision,string Token);
public sealed class RuleException(string message,int status=400):Exception(message) { public int Status { get; } = status; }
public static class JsonDefaults { public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }; }
