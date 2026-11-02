namespace LiveScore.Api;

public sealed record ResetScheduleRequest(long Revision,string EventId,string[] RoundKeys);
public static class FixtureResets
{
    public static string Key(TournamentMatch m)=>$"{m.ProgramId}:{m.Phase}:{m.Round}";
    public static bool Slotted(TournamentMatch m)=>m.Start.HasValue||m.End.HasValue||m.Court.HasValue;
    public static string Protection(SetupData s,List<TournamentMatch> matches)=>matches.Any(GamePresets.Locked)?"A match has started or recorded a result":s.Venue.Bindings.Any(b=>matches.Any(m=>m.Id==b.MatchId))?"Release waiting tablet assignments first":"";
    private static HashSet<string> Allocations(EventPlan plan)=>plan.ScheduleDays.SelectMany(d=>d.MatchIds).Concat(plan.Sessions.SelectMany(w=>w.MatchIds??[])).ToHashSet();
    public static void RequireScheduleReset(SetupData s,string eventId,List<TournamentMatch> matches)
    {
        var allocated=Allocations(TournamentRules.Event(s,eventId));
        TournamentRules.Need(!matches.Any(m=>Slotted(m)||allocated.Contains(m.Id)),"Reset Schedule before resetting this draw or round.");
        var reason=Protection(s,matches);TournamentRules.Need(reason=="",reason);
    }
    public static object List(SetupData s,string eventId)
    {
        var allocated=Allocations(TournamentRules.Event(s,eventId));
        var rounds=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==eventId)).SelectMany(p=>p.Matches).Where(m=>m.Status!=MatchStatus.Bye).GroupBy(Key).Select(g=>
        {
            var matches=g.ToList();var first=matches[0];var reason=Protection(s,matches);
            if(reason==""&&!matches.Any(m=>Slotted(m)||allocated.Contains(m.Id)))reason="No saved schedule";
            return new{key=g.Key,name=s.Programs.Single(p=>p.Id==first.ProgramId).Name+" · "+first.RoundLabel,reason};
        }).ToArray();return new{s.Revision,rounds};
    }
    public static void ResetSchedule(SetupData s,ResetScheduleRequest request)
    {
        var plan=TournamentRules.Event(s,request.EventId);var allocated=Allocations(plan);
        TournamentRules.Need(request.RoundKeys is {Length:>0}&&request.RoundKeys.Distinct().Count()==request.RoundKeys.Length,"Select distinct eligible rounds.");
        var rounds=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==request.EventId)).SelectMany(p=>p.Matches).Where(m=>m.Status!=MatchStatus.Bye).GroupBy(Key).ToDictionary(g=>g.Key,g=>g.ToList());
        var targets=new List<TournamentMatch>();
        foreach(var key in request.RoundKeys)
        {
            TournamentRules.Need(rounds.TryGetValue(key,out var matches),"A selected round changed. Reload the schedule.");
            var reason=Protection(s,matches!);TournamentRules.Need(reason=="",reason);
            TournamentRules.Need(matches!.Any(m=>Slotted(m)||allocated.Contains(m.Id)),"This round has no saved schedule.");targets.AddRange(matches);
        }
        var ids=targets.Select(m=>m.Id).Concat(s.ProgramPlans.SelectMany(p=>p.Matches).Where(m=>m.Status==MatchStatus.Bye&&request.RoundKeys.Contains(Key(m))).Select(m=>m.Id)).ToHashSet();
        foreach(var m in targets){m.Start=null;m.End=null;m.Court=null;m.ReviewAvailable=false;if(m.Live is not null)m.Live.Version++;}
        plan.ScheduleDays=plan.ScheduleDays.Select(d=>d with{MatchIds=d.MatchIds.Where(id=>!ids.Contains(id)).ToArray()}).ToList();
        plan.Sessions=plan.Sessions.Select(w=>w.MatchIds is null?w:w with{MatchIds=w.MatchIds.Where(id=>!ids.Contains(id)).ToArray()}).ToList();
        TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
    }
    public static void ResetSeeds(SetupData s,string programId)
    {
        var plan=TournamentRules.Program(s,programId);TournamentRules.Need(plan.Matches.Count==0,"Reset Draw before resetting seeding.");
        plan.Seeds=s.Entries.Where(e=>e.ProgramId==programId&&e.Status=="A").ToDictionary(e=>e.Id,e=>(int?)null);
        plan.SeedsReviewed=false;plan.SeedingMode="";plan.SeedCount=0;TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
    }
}
