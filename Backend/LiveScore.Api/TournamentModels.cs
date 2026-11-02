namespace LiveScore.Api;

public sealed class EventPlan
{
    public PlanningDraft? PlanningDraft { get; set; }
    public string EventId { get; set; } = "";
    public List<DailySession> DailySessions { get; set; } = [];
    public int[] Courts { get; set; } = [];
    public bool CourtsConfigured { get; set; }
    public string Progression { get; set; } = "I";
    public List<PlayingSession> Sessions { get; set; } = [];
    public List<ScheduleDay> ScheduleDays { get; set; } = [];
    public List<Official> Officials { get; set; } = [];
    // Retained solely to read review availability from older saved match assignments.
    public int[] ReviewCourts { get; set; } = [];
    public int MatchMinutes { get; set; } = 40;
    public int RestMinutes { get; set; } = 120;
}
public sealed record PlayingSession(DateOnly Date,TimeOnly Start,TimeOnly End,int[]? Courts=null,string[]? MatchIds=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Dictionary<string,int>? ProgramLimits=null);
public sealed record ScheduleDay(DateOnly Date,int[] Courts,string[] MatchIds);
public sealed record Official(string Id,string Name);
public sealed class BadmintonRules
{
    public string Name { get; set; } = "21-point games";
    public int GamesToWin { get; set; } = 2;
    public int PointsToWin { get; set; } = 21;
    public int WinBy { get; set; } = 2;
    public int PointCap { get; set; } = 30;
    public int IntervalAt { get; set; } = 11;
    public int IntervalSeconds { get; set; } = 60;
    public int BetweenGamesSeconds { get; set; } = 120;
    public int DecidingEndsAt { get; set; } = 11;
    public bool CardsEnabled { get; set; } = true;
    public bool ChallengesEnabled { get; set; }
    public int ChallengesPerGame { get; set; } = 2;
    public bool CountShuttles { get; set; } = true;
}
public sealed class ProgramPlan
{
    public string ProgramId { get; set; } = "";
    public string Format { get; set; } = "GK";
    public string RankingType { get; set; } = "";
    public DateTimeOffset? RankingFetchedAt { get; set; }
    public List<SbaRankingRow> RankingSnapshot { get; set; } = [];
    public BadmintonRules? Rules { get; set; }
    public Dictionary<string,int?> Seeds { get; set; } = [];
    public string SeedingMode { get; set; } = "";
    public bool SeedsReviewed { get; set; }
    public int SeedCount { get; set; }
    public int GroupCount { get; set; } = 1;
    public int AdvancePerGroup { get; set; } = 2;
    public string Phase { get; set; } = "P";
    public int LastRound { get; set; }
    public List<DrawGroup> Groups { get; set; } = [];
    public Dictionary<string,string> QualificationOrigins { get; set; } = [];
    public List<OfficialDecision> OfficialDecisions { get; set; } = [];
    public List<RankingDecision> RankingDecisions { get; set; } = [];
    public List<TournamentMatch> Matches { get; set; } = [];
}
public sealed record DrawGroup(string Id,string Name,string[] EntryIds);
public sealed record GameScore(int A,int B);
public sealed class TournamentMatch
{
    [System.Text.Json.Serialization.JsonIgnore]
    public LiveMatchState? Live {get;set;}
    public bool PaperMode=>Live?.PaperMode==true;
    public long MatchVersion=>Live?.Version??0;
    public ResultDetails ResultDetails=>Live?.Details??new();
    public DateTime? ActualStartedAt=>Live?.StartedLocal;
    public double? ActualDurationMinutes=>ActualStartedAt.HasValue&&ActualFinishedAt.HasValue?Math.Round((ActualFinishedAt.Value-ActualStartedAt.Value).TotalMinutes,2):null;
    public string ResultSource=>Live?.ResultSource??"";
    public string? SourceMatchA { get; set; }
    public string? SourceMatchB { get; set; }
    public bool PendingA { get; set; }
    public bool PendingB { get; set; }
    public bool DependencyOverride { get; set; }
    public string ResultKind { get; set; } = "N";
    public string OfficialReason { get; set; } = "";
    public GameScore? CurrentGame => Status==MatchStatus.Playing && Live is {StartedAtUtc:not null,AwaitingNextGame:false} l && !MatchResults.Won(this)?new(l.A,l.B):null;
    public bool PresetLocked { get; set; }
    public bool RulesAssigned { get; set; }
    public string? PresetId { get; set; }
    public string Id { get; set; } = "";
    public string ProgramId { get; set; } = "";
    public int Number { get; set; }
    public string Phase { get; set; } = "G";
    public string? GroupId { get; set; }
    public int Round { get; set; }
    public string RoundLabel { get; set; } = "";
    public string EntryA { get; set; } = "";
    public string? EntryB { get; set; }
    public string Status { get; set; } = "SC";
    public string? WinnerId { get; set; }
    public int? Court { get; set; }
    public int? CourtExceptionCourt { get; set; }
    public DateTime? CourtExceptionUntil { get; set; }
    public string CourtExceptionReason { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public DateTime? ActualFinishedAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? UmpireId { get; set; }
    public string? ServiceJudgeId { get; set; }
    public string[] LineJudgeIds { get; set; } = [];
    public BadmintonRules Rules { get; set; } = new();
    public bool ReviewAvailable { get; set; }
    public List<GameScore> Games { get; set; } = [];
}
public sealed record PlanRequest(long Revision,EventPlan Plan);
public sealed record DrawSettingsRequest(long Revision,string EventId,string[] ProgramIds,int GroupCount,int AdvancePerGroup,string Format="GK");
public sealed record DrawConfiguration(string Format,int GroupCount,int AdvancePerGroup);
public sealed record SeedsRequest(long Revision,string ProgramId,Dictionary<string,int?> Seeds);
public sealed record GenerateRequest(long Revision,string EventId,string[] ProgramIds,int[] Courts,DateTime EarliestStart,int? MatchMinutes=null,DrawConfiguration? Settings=null,Dictionary<string,int?>? Seeds=null,bool DrawOnly=false);
public sealed record GenerationConfirm(long Revision,string Token);
public sealed record SwapPreviewRequest(long Revision,string Token,string EntryA,string EntryB);
public sealed record ResultRequest(long Revision,string MatchId,List<GameScore> Games,string? WalkoverWinner=null,DateTime? FinishedAt=null,DateTime? StartedAt=null,ResultDetails? Details=null,long? MatchVersion=null,string Reason="",string? PresetId=null,string? Outcome=null,string? OfficialWinner=null,string? ImpactToken=null,string? DownstreamHandling=null);
public sealed record ScheduleRequest(long Revision,string MatchId,int Court,DateTime Start,string UmpireId,string? ServiceJudgeId=null,string[]? LineJudgeIds=null,bool? ReviewAvailable=null);
public sealed record ResetRequest(long Revision,string ProgramId);
public sealed record ClearResultRequest(long Revision,string MatchId,long MatchVersion,string Reason);
public sealed record Standing(string EntryId,int Played,int Wins,int Points,int GamesFor,int GamesAgainst,int PointsFor,int PointsAgainst);

public sealed record RankingDecision(string GroupId,string Fingerprint,string[] EntryIds,string Reason,DateTimeOffset At);
public sealed record RankingDecisionRequest(long Revision,string ProgramId,string GroupId,string[] EntryIds,string Reason);

public sealed record OfficialDecision(DateTimeOffset At,string MatchId,string Reason,string Handling,string Before,string After);
