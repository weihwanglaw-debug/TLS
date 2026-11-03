namespace LiveScore.Api;

public sealed class TrsPublicationSettings
{
    public bool Enabled { get; set; }
    public string LiveUrl { get; set; } = "";
    public string ResultsUrl { get; set; } = "";
    public int IntervalSeconds { get; set; } = 5;
    public string ProtectedCredential { get; set; } = "";
}
public sealed record TrsPublicationRequest(long Revision, bool Enabled, string LiveUrl, string ResultsUrl, int IntervalSeconds, string ApiKey = "");
public sealed record PublicationFailure(string EventId, string Kind, string Message, DateTimeOffset At);
public sealed record SpectatorEntry(string Id, string Name, string[] Players, int? Seed);
public sealed record SpectatorMatch(string Id, int ProgramId, string ProgramName, string Phase, int Round, string RoundLabel,
    string Group, SpectatorEntry Team1, SpectatorEntry Team2, List<GameScore> Games, string? Winner, string Outcome, string Remark,
    string MatchDate, string StartTime, string EndTime, string Court, string Status, int? ScoreA, int? ScoreB, string? ServerSide);
public sealed record TrsScoreSnapshot(int SchemaVersion, int EventId, string EventName, int? ProgramId, string ProgramName,
    long Version, DateTimeOffset GeneratedAt, string TimeZone, int RefreshSeconds, SpectatorMatch[] Matches);
