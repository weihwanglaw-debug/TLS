namespace LiveScore.Api;

public sealed record UnslottedMatch(string Id,int Number,string ProgramId,string Reason);
public sealed record ProgramAllocation(string ProgramId,int Requested,int Scheduled);
public sealed record SessionAllocationSummary(int Index,DateOnly Date,TimeOnly Start,TimeOnly End,int Capacity,List<ProgramAllocation> Programs);

public static class SessionAllocation
{
    // Derive circle-method rotations without changing saved draws or match numbers.
    public static Dictionary<string,(int Rotation,int Pair,int Group)> Order(SetupData s)
    {
        var result=new Dictionary<string,(int,int,int)>();
        foreach(var plan in s.ProgramPlans)
        foreach(var (group,index) in plan.Groups.Select((g,i)=>(g,i)))
        {
            var entries=group.EntryIds.Cast<string?>().ToList();if(entries.Count%2==1)entries.Add(null);
            for(var r=0;r<entries.Count-1;r++)
            {
                for(var p=0;p<entries.Count/2;p++)
                {
                    var a=entries[p];var b=entries[^(p+1)];if(a is null||b is null)continue;
                    var m=plan.Matches.SingleOrDefault(m=>m.Phase=="G"&&m.GroupId==group.Id&&(m.EntryA==a&&m.EntryB==b||m.EntryA==b&&m.EntryB==a));
                    if(m is not null)result[m.Id]=(r,p,index);
                }
                if(entries.Count>2){var last=entries[^1];entries.RemoveAt(entries.Count-1);entries.Insert(1,last);}
            }
        }
        return result;
    }
    public static bool Pending(SetupData s,TournamentMatch m)=>m.Status==MatchStatus.Scheduled&&!m.Start.HasValue&&!m.End.HasValue&&!m.Court.HasValue&&!s.Venue.Bindings.Any(b=>b.MatchId==m.Id);
    public static List<TournamentMatch> Allocate(SetupData s,EventPlan plan,List<PlayingSession> sessions,Dictionary<string,TournamentMatch> matches)
    {
        var added=new List<TournamentMatch>();var order=Order(s);
        foreach(var session in sessions.OrderBy(w=>w.Date).ThenBy(w=>w.Start))
        {
            var counts=new Dictionary<string,int>();
            var candidates=(session.MatchIds??[]).Select(id=>matches[id]).Where(m=>Pending(s,m)).ToList();
            var window=new EventPlan{EventId=plan.EventId,Sessions=[session],Courts=session.Courts??EventOperations.Courts(s,plan),CourtsConfigured=true,MatchMinutes=plan.MatchMinutes,RestMinutes=plan.RestMinutes};
            while(candidates.Count>0)
            {
                TournamentMatch? best=null;DateTime? bestStart=null;int? bestCourt=null;
                foreach(var m in candidates.OrderBy(m=>order.GetValueOrDefault(m.Id).Rotation).ThenBy(m=>counts.GetValueOrDefault(m.ProgramId)).ThenBy(m=>order.GetValueOrDefault(m.Id).Pair).ThenBy(m=>order.GetValueOrDefault(m.Id).Group).ThenBy(m=>m.Number))
                {
                    var limit=session.ProgramLimits?.GetValueOrDefault(m.ProgramId)??int.MaxValue;
                    if(counts.GetValueOrDefault(m.ProgramId)>=limit)continue;
                    var earliest=session.Date.ToDateTime(session.Start);
                    if(order.TryGetValue(m.Id,out var rank))
                    {
                        var previous=matches.Values.Where(other=>other.ProgramId==m.ProgramId&&other.GroupId==m.GroupId&&order.TryGetValue(other.Id,out var before)&&before.Rotation<rank.Rotation).ToList();
                        if(previous.Any(other=>Pending(s,other)))continue;
                        if(previous.Any())earliest=new[]{earliest,previous.Select(TournamentScheduler.Finish).Max()}.Max();
                    }
                    var duration=m.DurationMinutes;var umpire=m.UmpireId;
                    try
                    {
                        TournamentScheduler.Schedule(s,window,[m],new(s.Revision,plan.EventId,[m.ProgramId],window.Courts,earliest,plan.MatchMinutes),false);
                        if(bestStart is null||m.Start<bestStart){best=m;bestStart=m.Start;bestCourt=m.Court;}
                    }
                    catch(RuleException e)when(e.Message.StartsWith("Cannot fit match",StringComparison.Ordinal)){}
                    finally{m.Start=null;m.End=null;m.Court=null;m.DurationMinutes=duration;m.UmpireId=umpire;}
                }
                if(best is null)break;
                best.Start=bestStart;best.End=bestStart!.Value.AddMinutes(plan.MatchMinutes);best.Court=bestCourt;best.DurationMinutes=plan.MatchMinutes;
                TournamentScheduler.ValidateOverride(s,best);added.Add(best);candidates.Remove(best);counts[best.ProgramId]=counts.GetValueOrDefault(best.ProgramId)+1;
            }
        }
        return added;
    }
    public static List<SessionAllocationSummary> Summary(EventPlan plan,List<TournamentMatch> added)=>plan.Sessions.Select((w,i)=>
    {
        var placed=added.Where(m=>m.Start>=w.Date.ToDateTime(w.Start)&&m.End<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(m.Court??0)??true)).ToList();
        var requested=w.ProgramLimits??placed.GroupBy(m=>m.ProgramId).ToDictionary(g=>g.Key,g=>g.Count());
        var programs=requested.Keys.Union(placed.Select(m=>m.ProgramId)).Select(id=>new ProgramAllocation(id,requested.GetValueOrDefault(id),placed.Count(m=>m.ProgramId==id))).ToList();
        var capacity=placed.Count;
        return new SessionAllocationSummary(i,w.Date,w.Start,w.End,capacity,programs);
    }).ToList();
}


public sealed record ScheduleCalculationResult(EventPlan Plan,List<TournamentMatch> Matches,List<UnslottedMatch> Unplaced,int Kept);
public sealed record ProgramCapacity(string ProgramId,int Requested,int Scheduled,int Maximum);
public sealed record SessionCapacity(int Index,DateOnly Date,TimeOnly Start,TimeOnly End,int Capacity,List<ProgramCapacity> Programs);

public static class ScheduleCalculation
{
    public static ScheduleCalculationResult Calculate(SetupData s,SchedulePreviewRequest request)
    {
            SetupStore.CheckRevision(s,request.Revision);
            var ev=s.Events.SingleOrDefault(e=>e.Id==request.EventId);TournamentRules.Need(ev is not null,"Event not found.");
            var plan=TournamentRules.Event(s,request.EventId);
            ValidateSessionStarts(s,plan,request.Sessions);
            TournamentRules.Need(request.Sessions is {Count:>0 and <=10000}&&request.Days is {Count:>0 and <=10000},"Add dated playing sessions and court selections.");
            TournamentRules.Need(request.Days.All(d=>d is not null&&d.Courts is not null&&d.MatchIds is not null),"Each date needs courts and a match selection.");
            TournamentRules.Need(request.Days.Select(d=>d.Date).Distinct().Count()==request.Days.Count,"Each date must occur once.");
            TournamentRules.Need(request.Sessions.All(w=>w is not null),"Sessions cannot contain blank records.");
            var ids=request.Days.SelectMany(d=>d.MatchIds).ToList();TournamentRules.Need(ids.Count<=10000&&request.Days.All(d=>d.MatchIds.Distinct().Count()==d.MatchIds.Length),"Select each match only once within a date.");
            var matches=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==request.EventId&&p.Status=="A")).ToDictionary(m=>m.Id);
            TournamentRules.Need(ids.All(id=>!string.IsNullOrWhiteSpace(id)&&matches.ContainsKey(id)),"A selected draw changed. Reload the schedule.");
            var moves=request.MoveMatchIds??[];
            TournamentRules.Need(moves.Distinct().Count()==moves.Length&&moves.All(id=>ids.Contains(id)),"Select valid, distinct matches for the round move.");
            string Stage(TournamentMatch m)=>m.ProgramId+":"+m.Phase+":"+m.Round;
            foreach(var stage in request.Days.SelectMany(d=>d.MatchIds.Select(id=>new{Match=matches[id],d.Date})).Where(x=>x.Match.Status!=MatchStatus.Bye).GroupBy(x=>Stage(x.Match)))
            {
                var whole=matches.Values.Where(m=>Stage(m)==stage.Key&&m.Status!=MatchStatus.Bye).ToList();
                var moving=whole.Any(m=>moves.Contains(m.Id));
                if(moving)
                {
                    TournamentRules.Need(whole.All(m=>moves.Contains(m.Id))&&stage.Select(x=>x.Date).Distinct().Count()==1,"Move the entire round to one date.");
                    TournamentRules.Need(whole.All(m=>m.Status==MatchStatus.Scheduled&&m.Games.Count==0&&m.ActualStartedAt is null&&(m.Live?.Corrections.Count??0)==0&&!s.Venue.Bindings.Any(b=>b.MatchId==m.Id)),"Only an entirely unplayed, unpaired round can be moved.");
                }

            }
            foreach(var id in moves){var m=matches[id];m.Start=null;m.End=null;m.Court=null;}
            foreach(var day in request.Days)
            {
                TournamentRules.Need(day.Date>=ev!.StartDate&&day.Date<=ev.EndDate,"Schedule dates must be inside the event dates.");
                TournamentRules.Need(day.MatchIds.Select(id=>matches[id]).All(m=>!m.Start.HasValue||DateOnly.FromDateTime(m.Start.Value)==day.Date||moves.Contains(m.Id)),"A saved match belongs to another date. Keep its slot or explicitly move the unplayed round.");
                TournamentRules.Need(day.Courts.Length>0&&day.Courts.Distinct().Count()==day.Courts.Length&&day.Courts.All(c=>EventOperations.Courts(s,plan).Contains(c)),"Select courts from Master Setting for each day.");
                TournamentRules.Need(!day.Courts.Any(c=>s.Venue.ClosedCourts.Contains(c)),"Choose open courts for scheduling.");
                TournamentRules.Need(day.MatchIds.Length==0||request.Sessions.Any(w=>w.Date==day.Date),"Add a playing session for each selected match date.");
            }
            foreach(var session in request.Sessions)
            {
                var day=request.Days.SingleOrDefault(d=>d.Date==session.Date);
                TournamentRules.Need(day is not null,"Select courts and programs for each session date.");
                if(session.Courts is not null||session.MatchIds is not null)
                {
                    TournamentRules.Need(session.Courts is not null&&session.MatchIds is not null,"Each session needs both courts and program selections.");
                    TournamentRules.Need(session.Courts.All(c=>day.Courts.Contains(c))&&!session.Courts.Any(c=>s.Venue.ClosedCourts.Contains(c)),"Choose open session courts from the day's selection.");
                    TournamentRules.Need(session.MatchIds.All(id=>!string.IsNullOrWhiteSpace(id)&&day.MatchIds.Contains(id)),"Session matches must belong to that date's selection.");
                }
            }
            foreach(var session in request.Sessions.Where(w=>w.ProgramLimits is not null))
            {
                TournamentRules.Need(session.MatchIds is not null&&session.ProgramLimits!.All(x=>x.Value>=0&&x.Value<=session.MatchIds.Select(id=>matches[id]).Count(m=>m.ProgramId==x.Key&&SessionAllocation.Pending(s,m))),"Session quantities must be between zero and the remaining selected matches for that program.");
                TournamentRules.Need(session.MatchIds.Select(id=>matches[id]).Where(m=>SessionAllocation.Pending(s,m)).All(m=>session.ProgramLimits!.ContainsKey(m.ProgramId)),"Set a quantity for each selected program.");
            }
            foreach(var day in request.Days)
            {
                var sessions=request.Sessions.Where(w=>w.Date==day.Date).ToList();
                if(sessions.Any(w=>w.MatchIds is not null))
                {
                    TournamentRules.Need(sessions.All(w=>w.MatchIds is not null),"Use session selections consistently within each date.");
                    TournamentRules.Need(sessions.SelectMany(w=>w.MatchIds!).ToHashSet().SetEquals(day.MatchIds),"Select a session for every match on this date.");
                    TournamentRules.Need(sessions.SelectMany(w=>w.Courts!).ToHashSet().SetEquals(day.Courts),"Day courts must match the selected session courts.");
                }
            }
            plan.DailySessions=[];plan.Sessions=request.Sessions;plan.MatchMinutes=request.MatchMinutes;plan.RestMinutes=request.RestMinutes;plan.ScheduleDays=request.Days;
            TournamentRules.ValidateEvent(s,plan);TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
            var additions=new List<TournamentMatch>();var unplaced=new List<UnslottedMatch>();var kept=request.Days.SelectMany(d=>d.MatchIds).Distinct().Select(id=>matches[id]).Count(m=>m.Status!=MatchStatus.Bye&&!SessionAllocation.Pending(s,m));
            if(request.Sessions.Any(w=>w.ProgramLimits is not null))
            {
                additions=SessionAllocation.Allocate(s,plan,request.Sessions,matches);
                unplaced=ids.Distinct().Select(id=>matches[id]).Where(m=>m.Status!=MatchStatus.Bye&&SessionAllocation.Pending(s,m)).Select(m=>new UnslottedMatch(m.Id,m.Number,m.ProgramId,"Remaining after selected quantities, session capacity and player rest.")).ToList();
            }
            else
            {
            foreach(var day in request.Days.OrderBy(d=>d.Date))
            foreach(var match in day.MatchIds.Select(id=>matches[id]).OrderBy(m=>m.Phase=="G"?0:m.Round).ThenBy(m=>m.Number))
            {
                if(match.Status==MatchStatus.Bye)continue;
                if(match.Status!=MatchStatus.Scheduled||match.Start.HasValue||match.Court.HasValue||s.Venue.Bindings.Any(b=>b.MatchId==match.Id)){continue;}
                var window=new EventPlan{EventId=plan.EventId,Sessions=plan.Sessions.Where(w=>w.Date==day.Date).ToList(),Courts=day.Courts,CourtsConfigured=true,MatchMinutes=plan.MatchMinutes,RestMinutes=plan.RestMinutes};
                try
                {
                    var umpire=match.UmpireId;
                    TournamentScheduler.Schedule(s,window,[match],new(request.Revision,request.EventId,[match.ProgramId],day.Courts,day.Date.ToDateTime(TimeOnly.MinValue),request.MatchMinutes),false);
                    match.UmpireId=umpire;
                    TournamentScheduler.ValidateOverride(s,match);
                    additions.Add(match);
                }
                catch(RuleException e)when(e.Message.StartsWith("Cannot fit match",StringComparison.Ordinal))
                {unplaced.Add(new(match.Id,match.Number,match.ProgramId,"No space on "+day.Date.ToString("yyyy-MM-dd")+" within the selected sessions, courts and player rest."));}
            }
            }
            unplaced=unplaced.GroupBy(m=>m.Id).Select(g=>g.First()).Where(m=>SessionAllocation.Pending(s,matches[m.Id])).ToList();
            TournamentRules.Need(moves.All(id=>additions.Any(m=>m.Id==id)),"The entire moved round must fit. Adjust sessions, courts or duration and preview again.");
        return new(plan,additions,unplaced,kept);
    }

    public static void ValidateSessionStarts(SetupData s,EventPlan original,List<PlayingSession> sessions)
    {
        TournamentRules.Need(sessions is {Count:>0 and <=10000}&&sessions.All(w=>w is not null),"Add valid dated sessions.");
        var saved=EventOperations.Sessions(s,original);
        var now=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,SetupStore.Zone(s.TimeZone)).DateTime;
        foreach(var w in sessions)
            TournamentRules.Need(saved.Any(old=>old.Date==w.Date&&old.Start==w.Start)||w.Date.ToDateTime(w.Start)>now,"A new or changed session start must be later than the current venue time.");
    }

    public static List<ScheduleDay> Days(List<PlayingSession> sessions)=>sessions.GroupBy(w=>w.Date).OrderBy(g=>g.Key).Select(g=>new ScheduleDay(g.Key,g.SelectMany(w=>w.Courts??[]).Distinct().ToArray(),g.SelectMany(w=>w.MatchIds??[]).Distinct().ToArray())).ToList();
    private static SetupData Copy(SetupData s)=>System.Text.Json.JsonSerializer.Deserialize<SetupData>(System.Text.Json.JsonSerializer.Serialize(s,JsonDefaults.Options),JsonDefaults.Options)!;
    public static object Capacity(SetupData s,SchedulePreviewRequest request)
    {
        var actual=Calculate(Copy(s),request);
        var actualSummary=SessionAllocation.Summary(actual.Plan,actual.Matches);
        var candidates=TournamentScheduler.Matches(s).Where(m=>SessionAllocation.Pending(s,m)&&s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==request.EventId&&p.Status=="A")).GroupBy(m=>m.ProgramId).ToDictionary(g=>g.Key,g=>g.Select(m=>m.Id).ToArray());
        var result=new List<SessionCapacity>();
        foreach(var (target,index) in request.Sessions.Select((w,i)=>(w,i)))
        {
            var selected=target.ProgramLimits?.Keys.ToHashSet()??(target.MatchIds??[]).Where(id=>candidates.Values.Any(ids=>ids.Contains(id))).Select(id=>s.ProgramPlans.Single(p=>p.Matches.Any(m=>m.Id==id)).ProgramId).ToHashSet();
            if(selected.Count==0)selected=candidates.Keys.ToHashSet();
            int Allowance(string pid)=>Math.Max(0,candidates.GetValueOrDefault(pid,[]).Length-request.Sessions.Where((_,i)=>i!=index).Sum(w=>w.ProgramLimits?.GetValueOrDefault(pid)??0));
            var trials=new Dictionary<string,List<TournamentMatch>>();
            List<TournamentMatch> Trial(Dictionary<string,int> limits)
            {
                var key=System.Text.Json.JsonSerializer.Serialize(limits.OrderBy(x=>x.Key));
                if(trials.TryGetValue(key,out var cached))return cached;
                var windows=request.Sessions.Select((w,i)=>
                {
                    if(i==index)return w with{MatchIds=(w.MatchIds??[]).Concat(limits.Keys.SelectMany(pid=>candidates.GetValueOrDefault(pid,[]))).Distinct().ToArray(),ProgramLimits=limits};
                    if(w.Date>target.Date||w.Date==target.Date&&w.Start>target.Start)return w with{ProgramLimits=w.ProgramLimits?.ToDictionary(x=>x.Key,_=>0)};
                    return w;
                }).ToList();
                var trial=Calculate(Copy(s),request with{Sessions=windows,Days=Days(windows)});
                var placed=trial.Matches.Where(m=>m.Start>=target.Date.ToDateTime(target.Start)&&m.End<=target.Date.ToDateTime(target.End)&&(target.Courts?.Contains(m.Court??0)??true)).ToList();
                trials[key]=placed;return placed;
            }
            var placed=Trial(selected.ToDictionary(pid=>pid,Allowance));
            var programs=new List<ProgramCapacity>();
            foreach(var pid in candidates.Keys.Union(actualSummary[index].Programs.Select(p=>p.ProgramId)))
            {
                var limits=(target.ProgramLimits??new()).ToDictionary(x=>x.Key,x=>x.Value);limits[pid]=Allowance(pid);
                var maximum=Trial(limits).Count(m=>m.ProgramId==pid);
                var current=actualSummary[index].Programs.SingleOrDefault(p=>p.ProgramId==pid);
                programs.Add(new(pid,current?.Requested??0,current?.Scheduled??0,Math.Max(maximum,current?.Scheduled??0)));
            }
            result.Add(new(index,target.Date,target.Start,target.End,Math.Max(placed.Count,actualSummary[index].Capacity),programs));
        }
        return new{revision=s.Revision,sessions=result,matches=actual.Matches,unplaced=actual.Unplaced,kept=actual.Kept};
    }
}
