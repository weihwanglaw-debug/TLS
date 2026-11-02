namespace LiveScore.Api;

public sealed record ScheduleSlotSwap(long Revision,string EventId,string MatchA,string MatchB,string? Token=null);
public static class ScheduleSlotSwaps
{
    public static void Apply(SetupData s,EventPlan plan,string first,string second)
    {
        TournamentRules.Need(first!=second,"Choose two different matches.");
        var a=LiveRuntime.Find(s,first);var b=LiveRuntime.Find(s,second);
        foreach(var m in new[]{a,b})
        {
            TournamentRules.Need(s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==plan.EventId&&p.Status=="A"),"Choose matches from this event.");
            TournamentRules.Need(m.Status==MatchStatus.Scheduled&&m.ActualStartedAt is null&&m.Games.Count==0&&(m.Live?.Corrections.Count??0)==0&&!s.Venue.Bindings.Any(x=>x.MatchId==m.Id),"Only unstarted, uncalled matches without recorded results can swap slots.");
            TournamentRules.Need(m.Start.HasValue&&m.End.HasValue&&m.Court.HasValue&&m.DurationMinutes>0,"Both matches need saved or previewed slots.");
        }
        var courtA=a.Court;var courtB=b.Court;
        (a.Start,b.Start)=(b.Start,a.Start);(a.Court,b.Court)=(b.Court,a.Court);
        a.End=a.Start!.Value.AddMinutes(a.DurationMinutes);b.End=b.Start!.Value.AddMinutes(b.DurationMinutes);
        a.ReviewAvailable=courtA==a.Court&&a.ReviewAvailable;b.ReviewAvailable=courtB==b.Court&&b.ReviewAvailable;
        TournamentRules.Need(!s.Venue.ClosedCourts.Contains(a.Court!.Value)&&!s.Venue.ClosedCourts.Contains(b.Court!.Value),"Reopen the court before swapping slots.");
        // An explicit swap may cross the automatic program allocation of a session.
        plan.Sessions=plan.Sessions.Select(w=>
        {
            if(w.MatchIds is null)return w;
            var ids=w.MatchIds.Where(id=>id!=a.Id&&id!=b.Id).ToList();
            foreach(var m in new[]{a,b})if(m.Start>=w.Date.ToDateTime(w.Start)&&m.End<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(m.Court!.Value)??true))ids.Add(m.Id);
            return w with{MatchIds=ids.Distinct().ToArray()};
        }).ToList();
        plan.ScheduleDays=plan.ScheduleDays.Select(d=>d with{MatchIds=d.MatchIds.Where(id=>id!=a.Id&&id!=b.Id).Concat(new[]{a,b}.Where(m=>DateOnly.FromDateTime(m.Start!.Value)==d.Date).Select(m=>m.Id)).Distinct().ToArray()}).ToList();
        TournamentRules.ValidateEvent(s,plan);TournamentScheduler.ValidateOverride(s,a);TournamentScheduler.ValidateOverride(s,b);
    }
}
