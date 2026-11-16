namespace LiveScore.Api;

public sealed record ResetScheduleRequest(long Revision,string EventId,string[] RoundKeys);
public static class FixtureResets
{
    public static string Key(TournamentMatch m)=>$"{m.ProgramId}:{m.Phase}:{m.Round}";
    public static bool Slotted(TournamentMatch m)=>m.Start.HasValue||m.End.HasValue||m.Court.HasValue;
    // Only current play/results protect ordinary edits; explicit resets clear them.
    public static bool StartedOrScored(TournamentMatch match)=>match.Status is not (MatchStatus.Scheduled or MatchStatus.Bye)||match.Live?.StartedAtUtc is not null||match.ActualStartedAt.HasValue||match.ActualFinishedAt.HasValue||match.Games.Count>0;
    public static string Protection(SetupData s,List<TournamentMatch> matches)=>matches.Any(StartedOrScored)?"A match has started or recorded a result":s.Venue.Bindings.Any(b=>matches.Any(m=>m.Id==b.MatchId))?"Release waiting tablet assignments first":"";
    private static HashSet<string> Allocations(EventPlan plan)=>plan.ScheduleDays.SelectMany(d=>d.MatchIds).Concat(plan.Sessions.SelectMany(w=>w.MatchIds??[])).ToHashSet();
    public static void RequireScheduleReset(SetupData s,string eventId,List<TournamentMatch> matches)
    {
        var allocated=Allocations(TournamentRules.Event(s,eventId));
        TournamentRules.Need(!matches.Any(m=>Slotted(m)||allocated.Contains(m.Id)),"Reset Schedule before resetting this draw or round.");
        var reason=Protection(s,matches);TournamentRules.Need(reason=="",reason);
    }
    public static object List(SetupData s,string eventId)
    {
        var rounds=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==eventId)).SelectMany(p=>p.Matches).Where(m=>m.Status!=MatchStatus.Bye).GroupBy(Key).Select(g=>
        {
            var matches=g.ToList();var first=matches[0];var reason="";
            return new{key=g.Key,name=s.Programs.Single(p=>p.Id==first.ProgramId).Name+" · "+first.RoundLabel,reason};
        }).ToArray();return new{s.Revision,rounds};
    }
    public static void ResetSchedule(SetupData s,ResetScheduleRequest request)
    {
        var plan=TournamentRules.Event(s,request.EventId);
        TournamentRules.Need(request.RoundKeys is {Length:>0}&&request.RoundKeys.Distinct().Count()==request.RoundKeys.Length,"Select distinct eligible rounds.");
        SchedulePlanner.RemoveRounds(s,plan,request.RoundKeys);
        var rounds=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==request.EventId)).SelectMany(p=>p.Matches).Where(m=>m.Status!=MatchStatus.Bye).GroupBy(Key).ToDictionary(g=>g.Key,g=>g.ToList());
        var targets=new List<TournamentMatch>();
        foreach(var key in request.RoundKeys)
        {
            TournamentRules.Need(rounds.TryGetValue(key,out var matches),"A selected round changed. Reload the schedule.");
            targets.AddRange(matches!);
        }
        var ids=targets.Select(m=>m.Id).ToHashSet();
        foreach(var program in s.ProgramPlans.Where(p=>p.Matches.Any(m=>ids.Contains(m.Id))).ToList())
        {
            var selected=program.Matches.Where(m=>request.RoundKeys.Contains(Key(m))).ToList();
            var removed=RemoveDependents(s,program,selected);
            ids.UnionWith(removed);
            foreach(var m in selected.Where(m=>program.Matches.Contains(m))) { ResetResult(s,m);m.Start=null;m.End=null;m.Court=null;m.ReviewAvailable=false;m.UmpireId=null;m.ServiceJudgeId=null;m.LineJudgeIds=[];ids.Add(m.Id); }
            program.RankingDecisions.RemoveAll(d=>selected.Any(m=>m.GroupId==d.GroupId));
            program.OfficialDecisions.RemoveAll(d=>ids.Contains(d.MatchId));
            RefreshPhase(program);
        }
        CleanPlanning(s,plan,ids);
        TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
    }
    public static void ResetResult(SetupData s,TournamentMatch m)
    {
        s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
        m.Live=new(){Version=m.MatchVersion+1};
        m.Status=m.EntryB is null&&!m.PendingB?MatchStatus.Bye:MatchStatus.Scheduled;
        m.WinnerId=m.Status==MatchStatus.Bye?m.EntryA:null;
        m.Games=[];m.ActualFinishedAt=null;m.ResultKind="N";m.OfficialReason="";m.DependencyOverride=false;
        m.CourtExceptionCourt=null;m.CourtExceptionUntil=null;m.CourtExceptionReason="";
    }
    public static HashSet<string> RemoveDependents(SetupData s,ProgramPlan program,List<TournamentMatch> selected)
    {
        var group=selected.Any(m=>m.Phase=="G");
        var first=selected.Where(m=>m.Phase=="K").Select(m=>m.Round).DefaultIfEmpty(int.MaxValue).Min();
        var eventId=s.Programs.SingleOrDefault(p=>p.Id==program.ProgramId)?.EventId;var eventPlan=eventId is null?null:TournamentRules.Event(s,eventId);
        if(eventPlan?.SchedulePlanner is not null)
        {
            var futureKeys=SchedulePlanner.Slots(s,eventPlan.EventId).Where(n=>n.ProgramId==program.ProgramId&&n.Phase=="K"&&(group||n.Round>first)).Select(n=>n.RoundKey).Distinct().ToArray();
            if(futureKeys.Length>0)SchedulePlanner.RemoveRounds(s,eventPlan,futureKeys);
        }
        var removed=program.Matches.Where(m=>m.Phase=="K"&&(group||m.Round>first)).Select(m=>m.Id).ToHashSet();
        s.Venue.Bindings.RemoveAll(b=>removed.Contains(b.MatchId));
        program.Matches.RemoveAll(m=>removed.Contains(m.Id));
        program.OfficialDecisions.RemoveAll(d=>removed.Contains(d.MatchId));
        if(group)program.QualificationOrigins.Clear();
        RefreshPhase(program);
        return removed;
    }
    public static void RefreshPhase(ProgramPlan program)
    {
        program.LastRound=program.Matches.Where(m=>m.Phase=="K").Select(m=>m.Round).DefaultIfEmpty(0).Max();
        program.Phase=program.LastRound>0?"K":program.Matches.Any(m=>m.Phase=="G")?"G":"P";
    }
    public static void CleanPlanning(SetupData s,EventPlan plan,HashSet<string> ids)
    {
        if(ids.Count==0)return;
        var matches=TournamentScheduler.Matches(s).ToDictionary(m=>m.Id);
        PlayingSession Clean(PlayingSession w)
        {
            if(w.MatchIds is null||!w.MatchIds.Any(id=>ids.Contains(id)||!matches.ContainsKey(id)))return w;
            var remaining=w.MatchIds.Where(id=>!ids.Contains(id)&&matches.ContainsKey(id)).ToArray();
            return w with{MatchIds=remaining,ProgramLimits=w.ProgramLimits?.Where(p=>remaining.Any(id=>matches[id].ProgramId==p.Key)).ToDictionary(p=>p.Key,p=>Math.Min(p.Value,remaining.Count(id=>matches[id].ProgramId==p.Key&&SessionAllocation.Pending(s,matches[id]))))};
        }
        plan.ScheduleDays=plan.ScheduleDays.Select(d=>d with{MatchIds=d.MatchIds.Where(id=>!ids.Contains(id)&&matches.ContainsKey(id)).ToArray()}).ToList();
        plan.Sessions=plan.Sessions.Select(Clean).ToList();
        if(plan.PlanningDraft is {} draft)
        {
            var sessions=draft.Sessions.Select(Clean).ToList();
            var dirty=draft.DirtySessions.Concat(draft.Sessions.Select((w,i)=>(w,i)).Where(x=>x.w.MatchIds?.Any(id=>ids.Contains(id)||!matches.ContainsKey(id))==true).Select(x=>x.i)).Distinct().Order().ToArray();
            plan.PlanningDraft=sessions.Any(w=>w.MatchIds?.Length>0)?draft with{Sessions=sessions,DirtySessions=dirty,MoveMatchIds=draft.MoveMatchIds.Where(id=>!ids.Contains(id)&&matches.ContainsKey(id)).ToArray()}:null;
        }
    }
    public static void ResetSeeds(SetupData s,string programId)
    {
        var plan=TournamentRules.Program(s,programId);TournamentRules.Need(plan.Matches.Count==0,"Reset Draw before resetting seeding.");
        plan.Seeds=s.Entries.Where(e=>e.ProgramId==programId&&e.Status=="A").ToDictionary(e=>e.Id,e=>(int?)null);
        plan.SeedsReviewed=false;plan.SeedingMode="";plan.SeedCount=0;TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
    }
}
