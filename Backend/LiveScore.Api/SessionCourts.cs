namespace LiveScore.Api;

public static class SessionCourts
{
    public static bool Allows(PlayingSession session,int court)=>session.Courts is null||session.Courts.Contains(court);
    public static bool Available(SetupData s,EventPlan plan,int court,DateTime now)=>EventOperations.Sessions(s,plan).Any(w=>w.Date.ToDateTime(w.Start)<=now&&now<w.Date.ToDateTime(w.End)&&Allows(w,court));
    public static bool ExceptionApplies(TournamentMatch m,DateTime now)=>m.CourtExceptionCourt==m.Court&&m.CourtExceptionUntil>now&&!string.IsNullOrWhiteSpace(m.CourtExceptionReason);
    public static void Authorize(SetupData s,EventPlan plan,TournamentMatch m,int court,string? reason)
    {
        if(string.IsNullOrWhiteSpace(reason))return;
        TournamentRules.Need(reason.Length<=300,"Court exception reason must be at most 300 characters.");
        var now=LiveRuntime.LocalNow(s);
        var active=EventOperations.Sessions(s,plan).Where(w=>w.Date.ToDateTime(w.Start)<=now&&now<w.Date.ToDateTime(w.End)).ToList();
        TournamentRules.Need(active.Count>0,"A court exception requires an active playing session.");
        m.CourtExceptionCourt=court;m.CourtExceptionUntil=active.Max(w=>w.Date.ToDateTime(w.End));m.CourtExceptionReason=reason.Trim();
    }
}
