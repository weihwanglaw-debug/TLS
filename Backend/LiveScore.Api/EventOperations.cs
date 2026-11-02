namespace LiveScore.Api;

public static class EventOperations
{
    public static int[] Courts(SetupData s,EventPlan p)=>Enumerable.Range(1,s.CourtCount).ToArray();
    public static List<PlayingSession> Sessions(SetupData s,EventPlan p)
    {
        if(p.DailySessions.Count==0)return p.Sessions;
        var ev=s.Events.Single(e=>e.Id==p.EventId);var result=new List<PlayingSession>();
        TournamentRules.Need((long)(ev.EndDate.DayNumber-ev.StartDate.DayNumber+1)*p.DailySessions.Count<=10000,"The event has too many daily playing sessions.");
        for(var day=ev.StartDate.DayNumber;day<=ev.EndDate.DayNumber;day++)foreach(var window in p.DailySessions)result.Add(new(DateOnly.FromDayNumber(day),window.Start,window.End));
        return result;
    }
    public static DailySession[] LegacyDaily(SetupData s,EventPlan p)
    {
        if(p.DailySessions.Count>0)return p.DailySessions.ToArray();
        var ev=s.Events.Single(e=>e.Id==p.EventId);var days=p.Sessions.GroupBy(w=>w.Date).ToList();
        if(days.Count!=ev.EndDate.DayNumber-ev.StartDate.DayNumber+1)return [];
        var first=days[0].OrderBy(w=>w.Start).Select(w=>new DailySession(w.Start,w.End)).ToArray();
        return days.All(d=>d.OrderBy(w=>w.Start).Select(w=>new DailySession(w.Start,w.End)).SequenceEqual(first))?first:[];
    }
    public static IEnumerable<ProgramPlan> Programs(SetupData s,string eventId)=>s.Programs.Where(p=>p.EventId==eventId&&p.Status=="A").Select(p=>TournamentRules.Program(s,p.Id));
    public static int Stage(ProgramPlan p)=>p.Phase=="P"?0:p.Phase=="G"?1:p.LastRound+(p.Format=="GK"?1:0);
    public static List<TournamentMatch> Current(ProgramPlan p)=>p.Matches.Where(m=>p.Phase=="G"||p.Format=="RR"?m.Phase=="G":m.Phase=="K"&&m.Round==p.LastRound).ToList();
    // Retained API helpers: progression is always specific to the program now.
    public static string? Waiting(SetupData s,ProgramPlan p)=>null;
    public static DateTime PrerequisiteEnd(SetupData s,TournamentMatch match)=>TournamentScheduler.PrerequisiteEnd(s.ProgramPlans.Single(p=>p.ProgramId==match.ProgramId),match);
    public static void CheckStart(SetupData s,TournamentMatch m)
    {
        var eventPlan=TournamentRules.Event(s,s.Programs.Single(p=>p.Id==m.ProgramId).EventId);
        TournamentRules.Need(m.Start.HasValue&&m.End.HasValue&&Courts(s,eventPlan).Contains(m.Court??0),"Schedule the match date, time and court before starting.");
    }
    public static bool Started(SetupData s,string eventId)=>Programs(s,eventId).SelectMany(p=>p.Matches).Any(m=>m.Status is not (MatchStatus.Scheduled or MatchStatus.Bye)||(m.Live?.Corrections.Count??0)>0);
}

public sealed record DailySession(TimeOnly Start,TimeOnly End);
