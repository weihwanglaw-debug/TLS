namespace LiveScore.Api;

public static class MatchStatus
{
    public const string Scheduled="SC", Playing="IP", Review="RC", Completed="C", Walkover="WO", Bye="BY";
    public static bool Occupied(string status)=>status is Playing or Review;
    public static bool Finished(string status)=>status is Completed or Walkover or Bye;
}
public sealed class LiveVenue
{
    public LiveDisplaySettings Display {get;set;}=new();
    public List<GamePreset> Presets {get;set;}=[];
    public List<int> ClosedCourts {get;set;}=[];
    public List<TabletBinding> Bindings {get;set;}=[];
}
public sealed class TabletBinding
{
    public string Id {get;set;}="";
    public int Court {get;set;}
    public string MatchId {get;set;}="";
    public string CodeHash {get;set;}="";
    public DateTimeOffset CodeExpires {get;set;}
    public string TokenHash {get;set;}="";
    public DateTimeOffset? TokenExpires {get;set;}
    public string DeviceId {get;set;}="";
    public string DeviceName {get;set;}="";
    public string IpAddress {get;set;}="";
}
public sealed class LiveMatchState
{
    public ServingState? Serving {get;set;}
    public DateTimeOffset? IntervalUntil {get;set;}
    public List<RallyRecord> Rallies {get;set;}=[];
    public bool RallyHistoryComplete {get;set;}
    public List<ResumePoint> Resumptions {get;set;}=[];
    public ResultDetails Details {get;set;}=new();
    public string ResultSource {get;set;}="";
    public List<ResultCorrection> Corrections {get;set;}=[];
    public long Version {get;set;}
    public int A {get;set;}
    public int B {get;set;}
    public DateTimeOffset? StartedAtUtc {get;set;}
    public DateTime? StartedLocal {get;set;}
    public string TimeZone {get;set;}="Asia/Singapore";
    public DateTimeOffset? SubmittedAtUtc {get;set;}
    public bool AwaitingNextGame {get;set;}
    public bool PaperMode {get;set;}
    public string DeviceName {get;set;}="";
    public string DeviceIp {get;set;}="";
    public List<ScoreSnapshot> History {get;set;}=[];
    public List<LiveReceipt> Receipts {get;set;}=[];
}
public sealed record ScoreSnapshot(int A,int B,bool AwaitingNextGame,List<GameScore> Games,ServingState? Serving=null,DateTimeOffset? IntervalUntil=null,int? RallyCount=null);
public sealed record LiveReceipt(Guid OperationId,string Hash,long Version);
public sealed record PairCourtRequest(long Revision,string MatchId,int Court,string UmpireId,bool ReplaceWaiting=false,string Reason="",string? PresetId=null,bool? ReviewAvailable=null,string? CourtOverrideReason=null,string? ServiceJudgeId=null,string[]? LineJudgeIds=null);
public sealed record PairTabletRequest(string Code,string DeviceId,string DeviceName);
public sealed record RenewPairingRequest(long Revision,string MatchId,long Version);
public sealed record TabletCommand(long Version,Guid OperationId,string Action,string? Side=null,string? ParticipantId=null,string? CardType=null,string? Outcome=null,string Remark="",ServingSetup? Serving=null,bool ConfirmEarlyStart=false);
public sealed record CourtControlRequest(long Revision,int Court,bool Closed);
public sealed record CancelCourtRequest(long Revision,int Court);
public sealed record MatchControlRequest(long Revision,string MatchId,long Version,string Reason="");
public sealed record ResumePoint(int Game,int A,int B,int RallyIndex,DateTimeOffset At);
public sealed record ResumeTabletRequest(long Revision,string MatchId,long Version,List<GameScore> Games,int ScoreA,int ScoreB,ServingSetup Serving);

public static class MatchResults
{
    public static bool GameWon(int a,int b,BadmintonRules r)
    {
        var hi=Math.Max(a,b);var lo=Math.Min(a,b);
        return lo>=0&&hi<=r.PointCap&&hi>lo&&((hi==r.PointsToWin&&lo<=hi-r.WinBy)||(r.WinBy>1&&hi>r.PointsToWin&&hi<r.PointCap&&hi-lo==r.WinBy)||(hi==r.PointCap&&lo>=hi-r.WinBy&&(hi==r.PointsToWin||r.WinBy>1)));
    }
    public static bool Won(TournamentMatch m)=>m.Games.Count(g=>g.A>g.B)==m.Rules.GamesToWin||m.Games.Count(g=>g.B>g.A)==m.Rules.GamesToWin;
    public static string Winner(TournamentMatch m,List<GameScore> games)
    {
        TournamentRules.Need(games is {Count:>0}&&games.Count<=m.Rules.GamesToWin*2-1,"Enter complete game scores within the match format.");
        int a=0,b=0;
        foreach(var game in games)
        {
            TournamentRules.Need(game is not null&&a<m.Rules.GamesToWin&&b<m.Rules.GamesToWin,"No game may follow the match-winning game.");
            TournamentRules.Need(GameWon(game!.A,game.B,m.Rules),"A game score does not satisfy the saved winning-point, margin and cap rules.");
            if(game.A>game.B)a++;else b++;
        }
        TournamentRules.Need(a==m.Rules.GamesToWin||b==m.Rules.GamesToWin,"The scores must determine a match winner.");
        return a>b?m.EntryA:m.EntryB!;
    }
}

public sealed record SavedLiveMatch(LiveMatchState State,List<GameScore> Games,string? WinnerId,DateTime? ActualFinishedAt);

public sealed class LiveDisplaySettings
{
    public int Columns {get;set;}=3;
    public int Rows {get;set;}=2;
    public int PageSeconds {get;set;}=10;
    public bool AutoRotate {get;set;}=true;
    public string EventId {get;set;}="";
    public int[] Courts {get;set;}=[];
}
