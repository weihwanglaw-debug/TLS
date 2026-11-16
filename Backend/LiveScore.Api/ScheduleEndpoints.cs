using System.Collections.Concurrent;
namespace LiveScore.Api;

public sealed record SchedulePreviewRequest(long Revision,string EventId,List<PlayingSession> Sessions,int MatchMinutes,int RestMinutes,List<ScheduleDay> Days,string[]? MoveMatchIds=null);
public sealed record SessionValidationRequest(long Revision,string EventId,List<PlayingSession> Sessions,int? SessionIndex=null,int[]? DirtySessions=null);
public sealed record ScheduleDraftRequest(long Revision,string EventId,List<PlayingSession> Sessions,int SessionIndex,int[] DirtySessions,string[] MoveMatchIds);
public sealed record PlanningDraft(List<PlayingSession> Sessions,int[] DirtySessions,string[] MoveMatchIds,DateTimeOffset SavedAt);
public static class ScheduleEndpoints
{
    private sealed record Preview(long Revision,string EventId,EventPlan Plan,List<TournamentMatch> Matches,DateTimeOffset Expires,string[] Moves,List<UnslottedMatch> Unplaced,int Kept);
    private static readonly ConcurrentDictionary<string,Preview> Previews=new();
    public static void Map(WebApplication app)
    {
        SchedulePlannerEndpoints.Map(app);
        var group=app.MapGroup("/api/scheduling");
        group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.MapPost("/validate-session",(SessionValidationRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);
            TournamentRules.Need(s.Events.Any(e=>e.Id==request.EventId),"Event not found.");
            ValidateSession(s,request);
            return new{valid=true};
        });
        group.MapPost("/draft",(ScheduleDraftRequest request,SetupStore store)=>
        {
            var saved=store.Change(request.Revision,"SCHEDULE_DRAFT",s=>
            {
                TournamentRules.Need(s.Events.Any(e=>e.Id==request.EventId),"Event not found.");
                TournamentRules.Need(request.DirtySessions is not null&&request.DirtySessions.All(i=>i>=0&&i<request.Sessions.Count&&i!=request.SessionIndex),"Invalid unfinished session.");
                ValidateSession(s,new(request.Revision,request.EventId,request.Sessions,request.SessionIndex,request.DirtySessions));
                var eventMatches=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==request.EventId)).Select(m=>m.Id).ToHashSet();
                TournamentRules.Need(request.MoveMatchIds.All(eventMatches.Contains),"Choose matches belonging to this event.");
                TournamentRules.Event(s,request.EventId).PlanningDraft=new(request.Sessions,request.DirtySessions,request.MoveMatchIds,DateTimeOffset.UtcNow);
            });
            return new{saved.Revision};
        });
        IResult Calculate(SchedulePreviewRequest request,SetupStore store)
        {
            var result=ScheduleCalculation.Calculate(store.Read(),request);
            var plan=result.Plan;var additions=result.Matches;var unplaced=result.Unplaced;var kept=result.Kept;var moves=request.MoveMatchIds??[];
            foreach(var old in Previews.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow))Previews.TryRemove(old.Key,out _);
            TournamentRules.Need(Previews.Count<50,"Too many schedule previews. Let older previews expire.");
            var token=Guid.NewGuid().ToString("N");Previews[token]=new(request.Revision,request.EventId,plan,additions,DateTimeOffset.UtcNow.AddMinutes(10),moves,unplaced,kept);
            return Results.Ok(new{token,revision=request.Revision,matches=additions,unplaced,kept,sessions=SessionAllocation.Summary(plan,additions)});
        }
        group.MapPost("/capacity",(SchedulePreviewRequest request,SetupStore store)=>ScheduleCalculation.Capacity(store.Read(),request));
        group.MapPost("/preview",(SchedulePreviewRequest request,SetupStore store)=>Calculate(request,store));
        group.MapPost("/swap",(ScheduleSlotSwap request,SetupStore store)=>
        {
            if(request.Token is null)
            {
                var saved=store.Change(request.Revision,"SCHEDULE_SLOT_SWAP",s=>ScheduleSlotSwaps.Apply(s,TournamentRules.Event(s,request.EventId),request.MatchA,request.MatchB),auditView:s=>new{matches=TournamentScheduler.Matches(s).Where(m=>m.Id==request.MatchA||m.Id==request.MatchB).ToArray(),plan=TournamentRules.Event(s,request.EventId)});
                return Results.Ok(new{saved.Revision});
            }
            TournamentRules.Need(Previews.TryGetValue(request.Token,out var preview),"Schedule preview missing or already confirmed.");
            lock(preview)
            {
                TournamentRules.Need(Previews.ContainsKey(request.Token)&&preview.Expires>=DateTimeOffset.UtcNow&&preview.Revision==request.Revision&&preview.EventId==request.EventId,"Schedule preview expired or changed. Preview again.");
                TournamentRules.Need(preview.Matches.Any(m=>m.Id==request.MatchA)&&preview.Matches.Any(m=>m.Id==request.MatchB),"Choose two matches from this preview.");
                var s=store.Read();SetupStore.CheckRevision(s,request.Revision);
                var plan=System.Text.Json.JsonSerializer.Deserialize<EventPlan>(System.Text.Json.JsonSerializer.Serialize(preview.Plan,JsonDefaults.Options),JsonDefaults.Options)!;
                TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
                foreach(var proposed in preview.Matches)
                {
                    var m=LiveRuntime.Find(s,proposed.Id);TournamentRules.Need(m.Status==MatchStatus.Scheduled&&!s.Venue.Bindings.Any(b=>b.MatchId==m.Id),"A match changed. Preview again.");
                    m.Start=proposed.Start;m.End=proposed.End;m.Court=proposed.Court;m.DurationMinutes=proposed.DurationMinutes;
                }
                ScheduleSlotSwaps.Apply(s,plan,request.MatchA,request.MatchB);
                var updated=preview.Matches.Select(p=>LiveRuntime.Find(s,p.Id)).ToList();
                var token=Guid.NewGuid().ToString("N");Previews[token]=preview with{Plan=plan,Matches=updated};Previews.TryRemove(request.Token,out _);
                return Results.Ok(new{token,revision=s.Revision,matches=updated,unplaced=preview.Unplaced,kept=preview.Kept,sessions=SessionAllocation.Summary(plan,updated)});
            }
        });
        group.MapPost("/confirm",(GenerationConfirm request,SetupStore store)=>
        {
            TournamentRules.Need(Previews.TryGetValue(request.Token,out var preview),"Schedule preview missing or already confirmed.");
            lock(preview)
            {
                TournamentRules.Need(Previews.ContainsKey(request.Token)&&preview.Expires>=DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Schedule preview expired or changed. Preview again.");
                var saved=store.Change(request.Revision,"EVENT_SCHEDULE",s=>
                {
                    var plan=System.Text.Json.JsonSerializer.Deserialize<EventPlan>(System.Text.Json.JsonSerializer.Serialize(preview.Plan,JsonDefaults.Options),JsonDefaults.Options)!;
                    ScheduleCalculation.ValidateSessionStarts(s,TournamentRules.Event(s,preview.EventId),plan.Sessions);
                    plan.PlanningDraft=null;
                    plan.Sessions=plan.Sessions.Select(w=>w.ProgramLimits is null?w:w with{ProgramLimits=w.ProgramLimits.ToDictionary(x=>x.Key,x=>Math.Max(0,x.Value-preview.Matches.Count(m=>m.ProgramId==x.Key&&m.Start>=w.Date.ToDateTime(w.Start)&&m.End<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(m.Court??0)??true))))}).ToList();
                    TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
                    foreach(var proposed in preview.Matches)
                    {
                        var actual=TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==proposed.Id);
                        var moving=preview.Moves.Contains(proposed.Id);
                        TournamentRules.Need(actual is not null&&actual.Status==MatchStatus.Scheduled&&(moving||!actual.Start.HasValue&&!actual.Court.HasValue)&&!FixtureResets.StartedOrScored(actual)&&!s.Venue.Bindings.Any(b=>b.MatchId==actual.Id),"A selected match changed. Preview the schedule again.");
                        actual.ReviewAvailable=actual.Court==proposed.Court&&actual.ReviewAvailable;actual.Start=proposed.Start;actual.End=proposed.End;actual.Court=proposed.Court;actual.DurationMinutes=proposed.DurationMinutes;
                    }
                    foreach(var proposed in preview.Matches)TournamentScheduler.ValidateOverride(s,TournamentScheduler.Matches(s).Single(m=>m.Id==proposed.Id));
                    TournamentRules.ValidateEvent(s,plan);
                });
                Previews.TryRemove(request.Token,out _);return new{saved.Revision,eventId=preview.EventId,scheduled=preview.Matches.Count};
            }
        });
    }

    private static void ValidateSession(SetupData s,SessionValidationRequest request)
    {
            TournamentRules.Need(request.Sessions is {Count:>0 and <=10000}&&request.Sessions.All(w=>w is not null),"Add valid dated sessions.");
            var dirty=request.DirtySessions??[];
            TournamentRules.Need(dirty.All(i=>i>=0&&i<request.Sessions.Count&&i!=request.SessionIndex),"Invalid unfinished session.");
            if(request.SessionIndex is int chosen)TournamentRules.Need(chosen>=0&&chosen<request.Sessions.Count,"Choose a valid session to save.");
            var original=TournamentRules.Event(s,request.EventId);
            var plan=System.Text.Json.JsonSerializer.Deserialize<EventPlan>(System.Text.Json.JsonSerializer.Serialize(original,JsonDefaults.Options),JsonDefaults.Options)!;
            var ready=request.Sessions.Where((_,i)=>!dirty.Contains(i)).ToList();
            var targets=request.SessionIndex is int target?new List<PlayingSession>{request.Sessions[target]}:ready;
            ScheduleCalculation.ValidateSessionStarts(s,original,targets);
            // Unfinished cards do not validate or replace published sessions yet.
            var published=EventOperations.Sessions(s,original);
            var booked=TournamentScheduler.Matches(s).Where(m=>m.Start.HasValue&&m.End.HasValue).ToList();
            plan.DailySessions=[];plan.Sessions=[];
            foreach(var (w,i) in request.Sessions.Select((w,i)=>(w,i)))
            {
                if(!dirty.Contains(i)){plan.Sessions.Add(w);continue;}
                var position=request.Sessions.Take(i).Count(before=>before.Date==w.Date);
                var old=published.Where(old=>old.Date==w.Date).Skip(position).FirstOrDefault();
                if(old is not null&&booked.Any(m=>m.Start>=old.Date.ToDateTime(old.Start)&&m.End<=old.Date.ToDateTime(old.End)&&TournamentRules.SessionAllows(old,m)))plan.Sessions.Add(old);
            }
            TournamentRules.ValidateEvent(s,plan);
            var matches=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.EventId==request.EventId&&p.Id==m.ProgramId)).ToDictionary(m=>m.Id);
            foreach(var w in targets)
            {
                TournamentRules.Need(w.MatchIds is not null&&w.MatchIds.All(matches.ContainsKey),"Choose matches belonging to this event.");
                TournamentRules.Need(w.Courts is {Length:>0}&&!w.Courts.Any(c=>s.Venue.ClosedCourts.Contains(c)),"Select open courts for this session.");
                if(w.ProgramLimits is not null)
                    TournamentRules.Need(w.ProgramLimits.All(x=>x.Value>=0&&x.Value<=w.MatchIds.Count(id=>matches[id].ProgramId==x.Key&&SessionAllocation.Pending(s,matches[id]))),"Session quantities cannot exceed the remaining selected matches for each program.");
                var requested=w.ProgramLimits?.Values.Sum()??0;
                var available=SessionAllocation.Available(s,plan,w)??0;
                TournamentRules.Need(requested<=available,$"This session has space for {available} additional matches. Reduce the quantities or adjust the session settings.");
                if(request.SessionIndex.HasValue)
                {
                    var hasSaved=w.MatchIds.Any(id=>matches[id].Start.HasValue&&matches[id].Start>=w.Date.ToDateTime(w.Start)&&matches[id].End<=w.Date.ToDateTime(w.End)&&w.Courts.Contains(matches[id].Court??0));
                    TournamentRules.Need(hasSaved||requested>0,"Select at least one program and match quantity for this session.");
                }
            }
            foreach(var pid in targets.SelectMany(w=>w.ProgramLimits?.Keys??Enumerable.Empty<string>()).Distinct())
            {
                var requested=ready.Sum(w=>w.ProgramLimits?.GetValueOrDefault(pid)??0);
                TournamentRules.Need(requested<=matches.Values.Count(m=>m.ProgramId==pid&&SessionAllocation.Pending(s,m)),"Requested quantities exceed the remaining matches for this program across saved sessions.");
            }
    }

}
