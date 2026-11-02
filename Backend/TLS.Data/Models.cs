namespace TLS.Data;

public sealed class EventRecord
{
    public int SortOrder { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Venue { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int? MaxProgramsPerParticipant { get; set; }
    public string Status { get; set; } = "";
}
public sealed class ProgramRecord
{
    public int SortOrder { get; set; }
    public string Id { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sport { get; set; } = "";
    public string EntryType { get; set; } = "";
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public string Gender { get; set; } = "";
    public int MinCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public string CapacityUnit { get; set; } = "";
    public string Status { get; set; } = "";
}
public sealed class ParticipantRecord
{
    public int SortOrder { get; set; }
    public string Id { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly Dob { get; set; }
    public string Gender { get; set; } = "";
    public string Email { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Club { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string SbaId { get; set; } = "";
}
public sealed class EntryRecord
{
    public int SortOrder { get; set; }
    public string Id { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ProgramId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Seed { get; set; }
    public string Status { get; set; } = "";
}

public sealed class EntryMember { public string EntryId { get; set; } = ""; public int Position { get; set; } public string ParticipantId { get; set; } = ""; }
public sealed class SystemSetting { public string LiveVenueJson {get;set;}="{}"; public int CourtCount { get; set; } = 4; public string AppName { get; set; } = "TLS"; public string LogoLightUrl { get; set; } = ""; public string LogoDarkUrl { get; set; } = ""; public int Id { get; set; } = 1; public long Revision { get; set; } public string TimeZone { get; set; } = "Asia/Singapore"; }
public sealed class AuditLog { public long Id { get; set; } public string At { get; set; } = ""; public string Action { get; set; } = ""; public string BeforeJson { get; set; } = ""; public string AfterJson { get; set; } = ""; }
public sealed class EventImport
{
    public string EventId { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceSystemId { get; set; } = "";
    public string SourceEventId { get; set; } = "";
    public string SourceTimeZone { get; set; } = "";
    public DateTimeOffset ExportedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public string Stage { get; set; } = "P";
    public string PackageJson { get; set; } = "";
}

public sealed class EventOperation { public string EventId { get; set; } = ""; public string Json { get; set; } = ""; }
public sealed class ProgramOperation { public string ProgramId { get; set; } = ""; public string Json { get; set; } = ""; }

public sealed class MatchLiveRecord { public string MatchId {get;set;}="";public string ProgramId {get;set;}="";public string Status {get;set;}="SC";public string Json {get;set;}="";}

public sealed class IdSequence { public string Key {get;set;}="";public long Value {get;set;} }
public sealed class ImportIdMapping { public string Fingerprint {get;set;}="";public string EventId {get;set;}="";public string Kind {get;set;}="";public string SourceId {get;set;}="";public string LocalId {get;set;}=""; }
