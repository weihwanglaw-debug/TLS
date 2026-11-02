namespace LiveScore.Api;

public static class SessionCourts
{
    public static bool Allows(PlayingSession session,int court)=>session.Courts is null||session.Courts.Contains(court);
    public static bool Available(SetupData s,EventPlan plan,int court,DateTime now)=>EventOperations.Sessions(s,plan).Any(w=>w.Date.ToDateTime(w.Start)<=now&&now<w.Date.ToDateTime(w.End)&&Allows(w,court));
    public static bool ExceptionApplies(TournamentMatch m,DateTime now)=>m.CourtExceptionCourt==m.Court&&m.CourtExceptionUntil>now&&!string.IsNullOrWhiteSpace(m.CourtExceptionReason);
    public const string UnavailableMessage="This court is unavailable for this match's playing session. Review session times and courts in Schedule.";
    public static int[] EligibleCourts(SetupData s,EventPlan plan,TournamentMatch m,DateTime now)
    {
        if(!m.Start.HasValue)return [];
        var sessions=EventOperations.Sessions(s,plan).ToList();
        var active=sessions.Where(w=>w.Date.ToDateTime(w.Start)<=now&&now<w.Date.ToDateTime(w.End)).ToList();
        // Future-day assignments are preparation only; START still checks the playing date.
        var allocated=m.Start.Value.Date>now.Date||active.Count==0&&m.Start>now
            ?sessions.Where(w=>w.Date.ToDateTime(w.Start)<=m.Start&&m.Start<w.Date.ToDateTime(w.End)).ToList()
            :active;
        return EventOperations.Courts(s,plan).Where(c=>!s.Venue.ClosedCourts.Contains(c)&&
            (allocated.Any(w=>Allows(w,c))||active.Count>0&&m.CourtExceptionCourt==c&&m.CourtExceptionUntil>now&&!string.IsNullOrWhiteSpace(m.CourtExceptionReason))).ToArray();
    }
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
