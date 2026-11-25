namespace LiveScore.Api;

public sealed record ResetScheduleRequest(long Revision,string EventId,DateOnly FromDate);
public sealed record ResetDay(DateOnly Date,int Bookings);
public sealed record ResetBlocker(string MatchId,string Name,DateOnly Date,int? Court,string Reason);
public sealed record ResetSchedulePreview(long Revision,string EventId,DateOnly StartDate,DateOnly EndDate,DateOnly FromDate,int BookingCount,ResetDay[] Days,ResetBlocker[] Blockers);
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
    private sealed record ResetRow(string SlotId,string? MatchId,DateOnly Date,int? Court,string Name,string Reason);
    private static List<ResetRow> Range(SetupData s,string eventId,DateOnly fromDate)
    {
        var ev=s.Events.SingleOrDefault(e=>e.Id==eventId);TournamentRules.Need(ev is not null,"Event not found.");
        TournamentRules.Need(fromDate>=ev!.StartDate&&fromDate<=ev.EndDate,"Choose a date within the event.");
        var programs=s.Programs.Where(p=>p.EventId==eventId).ToDictionary(p=>p.Id);var matches=s.ProgramPlans.Where(p=>programs.ContainsKey(p.ProgramId)).SelectMany(p=>p.Matches).ToDictionary(m=>m.Id);
        string Reason(TournamentMatch m)=>m.Status==MatchStatus.Walkover?"Walkover result recorded.":m.Status==MatchStatus.Completed?"Completed result recorded.":m.Status==MatchStatus.Review?"Awaiting result confirmation.":StartedOrScored(m)||m.WinnerId is not null||m.Live is {A:>0} or {B:>0}?"Match has started or contains a result.":s.Venue.Bindings.Any(b=>b.MatchId==m.Id)?"Match has been called.":"";
        var nodes=SchedulePlanner.Slots(s,eventId);var rows=new List<ResetRow>();
        foreach(var booking in SchedulePlanner.Current(s,eventId,nodes).Where(b=>DateOnly.FromDateTime(b.Start)>=fromDate))
        {
            var n=nodes.Single(n=>n.Id==booking.SlotId);var m=n.MatchId is {} id?matches[id]:null;
            rows.Add(new(n.Id,n.MatchId,DateOnly.FromDateTime(booking.Start),booking.Court,n.ProgramName+" · "+n.RoundLabel+" · Match "+(n.Position+1),m is null?"":Reason(m)));
        }
        var represented=rows.Where(r=>r.MatchId is not null).Select(r=>r.MatchId).ToHashSet();
        foreach(var m in matches.Values.Where(m=>m.Status!=MatchStatus.Bye&&m.Start.HasValue&&DateOnly.FromDateTime(m.Start.Value)>=fromDate&&!represented.Contains(m.Id)))
            rows.Add(new(nodes.FirstOrDefault(n=>n.MatchId==m.Id)?.Id??"match:"+m.Id,m.Id,DateOnly.FromDateTime(m.Start!.Value),m.Court,programs[m.ProgramId].Name+" · "+m.RoundLabel+" · Match "+m.Number,Reason(m)));
        return rows;
    }
    public static ResetSchedulePreview List(SetupData s,string eventId,DateOnly? fromDate=null)
    {
        var ev=s.Events.SingleOrDefault(e=>e.Id==eventId);TournamentRules.Need(ev is not null,"Event not found.");
        var date=fromDate??ev!.StartDate;var rows=Range(s,eventId,date);
        return new(s.Revision,eventId,ev!.StartDate,ev.EndDate,date,rows.Count,rows.GroupBy(r=>r.Date).OrderBy(g=>g.Key).Select(g=>new ResetDay(g.Key,g.Count())).ToArray(),rows.Where(r=>r.Reason!="").Select(r=>new ResetBlocker(r.MatchId!,r.Name,r.Date,r.Court,r.Reason)).ToArray());
    }
    public static void ResetSchedule(SetupData s,ResetScheduleRequest request)
    {
        var rows=Range(s,request.EventId,request.FromDate);var blocked=rows.Where(r=>r.Reason!="").ToList();
        TournamentRules.Need(blocked.Count==0,"Reset blocked: "+string.Join("; ",blocked.Take(8).Select(r=>r.Name+" ("+r.Date.ToString("yyyy-MM-dd")+"): "+r.Reason)));
        TournamentRules.Need(rows.Count>0,"No bookings from this date onward.");
        var plan=TournamentRules.Event(s,request.EventId);var ids=rows.Where(r=>r.MatchId is not null).Select(r=>r.MatchId!).ToHashSet();var slots=rows.Select(r=>r.SlotId).ToHashSet();
        foreach(var m in s.ProgramPlans.SelectMany(p=>p.Matches).Where(m=>ids.Contains(m.Id)))
        {
            ResetResult(s,m);m.Start=null;m.End=null;m.Court=null;m.ReviewAvailable=false;m.UmpireId=null;m.ServiceJudgeId=null;m.LineJudgeIds=[];
        }
        if(plan.SchedulePlanner is {} state)
        {
            state.Bookings.RemoveAll(b=>slots.Contains(b.SlotId));
            foreach(var key in state.WalkoverMoves.Where(pair=>ids.Contains(pair.Key)||pair.Value.Any(move=>slots.Contains(move.Before.SlotId))).Select(pair=>pair.Key).ToList())state.WalkoverMoves.Remove(key);
            state.Notices=[$"Schedule cleared from {request.FromDate:dd MMM yyyy}: {rows.Count} bookings removed."];
        }
        CleanPlanning(s,plan,ids);TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
    }
    public static void ResetResult(SetupData s,TournamentMatch m)
    {
        foreach(var ev in s.EventPlans)ev.SchedulePlanner?.WalkoverMoves.Remove(m.Id);
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

    }
    public static void ResetSeeds(SetupData s,string programId)
    {
        var plan=TournamentRules.Program(s,programId);TournamentRules.Need(plan.Matches.Count==0,"Reset Draw before resetting seeding.");
        plan.Seeds=s.Entries.Where(e=>e.ProgramId==programId&&e.Status=="A").ToDictionary(e=>e.Id,e=>(int?)null);
        plan.SeedsReviewed=false;plan.SeedingMode="";plan.SeedCount=0;TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
    }
}
