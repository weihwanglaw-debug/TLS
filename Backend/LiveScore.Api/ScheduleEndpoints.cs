using System.Collections.Concurrent;
namespace LiveScore.Api;

public sealed record SchedulePreviewRequest(long Revision,string EventId,List<PlayingSession> Sessions,int MatchMinutes,int RestMinutes,List<ScheduleDay> Days,string[]? MoveMatchIds=null);
public sealed record SessionValidationRequest(long Revision,string EventId,List<PlayingSession> Sessions,int? SessionIndex=null);
public sealed record ScheduleDraftRequest(long Revision,string EventId,List<PlayingSession> Sessions,int SessionIndex,int[] DirtySessions,string[] MoveMatchIds);
public sealed record PlanningDraft(List<PlayingSession> Sessions,int[] DirtySessions,string[] MoveMatchIds,DateTimeOffset SavedAt);
public static class ScheduleEndpoints
{
    private sealed record Preview(long Revision,string EventId,EventPlan Plan,List<TournamentMatch> Matches,DateTimeOffset Expires,string[] Moves,List<UnslottedMatch> Unplaced,int Kept);
    private static readonly ConcurrentDictionary<string,Preview> Previews=new();
    public static void Map(WebApplication app)
    {
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
                ValidateSession(s,new(request.Revision,request.EventId,request.Sessions,request.SessionIndex));
                TournamentRules.Need(request.DirtySessions.All(i=>i>=0&&i<request.Sessions.Count),"Invalid unfinished session.");
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
                        TournamentRules.Need(actual is not null&&actual.Status==MatchStatus.Scheduled&&(moving||!actual.Start.HasValue&&!actual.Court.HasValue)&&actual.ActualStartedAt is null&&(actual.Live?.Corrections.Count??0)==0&&!s.Venue.Bindings.Any(b=>b.MatchId==actual.Id),"A selected match changed. Preview the schedule again.");
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
            // Scheduling simulates allocations in memory. Never apply that simulation to a saved draft.
            var live=TournamentScheduler.Matches(s).ToDictionary(m=>m.Id,m=>m.Live);
            s=System.Text.Json.JsonSerializer.Deserialize<SetupData>(System.Text.Json.JsonSerializer.Serialize(s,JsonDefaults.Options),JsonDefaults.Options)!;
            foreach(var m in TournamentScheduler.Matches(s))m.Live=live.GetValueOrDefault(m.Id);
            var original=TournamentRules.Event(s,request.EventId);
            ScheduleCalculation.ValidateSessionStarts(s,original,request.Sessions);
            var plan=System.Text.Json.JsonSerializer.Deserialize<EventPlan>(System.Text.Json.JsonSerializer.Serialize(original,JsonDefaults.Options),JsonDefaults.Options)!;
            plan.DailySessions=[];plan.Sessions=request.Sessions;
            TournamentRules.ValidateEvent(s,plan);
            if(request.SessionIndex is int index)
            {
                TournamentRules.Need(index>=0&&index<request.Sessions.Count,"Choose a valid session to save.");
                var w=request.Sessions[index];
                var matches=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.EventId==request.EventId&&p.Id==m.ProgramId)).ToDictionary(m=>m.Id);
                TournamentRules.Need(w.MatchIds is {Length:>0}&&w.MatchIds.All(id=>matches.ContainsKey(id)),"Select at least one program and match quantity for this session.");
                TournamentRules.Need(w.Courts is {Length:>0}&&!w.Courts.Any(c=>s.Venue.ClosedCourts.Contains(c)),"Select open courts for this session.");
                if(w.ProgramLimits is not null)
                    TournamentRules.Need(w.ProgramLimits.All(x=>x.Value<=w.MatchIds.Count(id=>matches[id].ProgramId==x.Key&&SessionAllocation.Pending(s,matches[id]))),"Session quantities cannot exceed the remaining selected matches for each program.");
                var hasSaved=w.MatchIds.Any(id=>matches[id].Start.HasValue&&matches[id].Start>=w.Date.ToDateTime(w.Start)&&matches[id].End<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(matches[id].Court??0)??true));
                var hasRequested=w.ProgramLimits?.Any(x=>x.Value>0&&w.MatchIds.Any(id=>matches[id].ProgramId==x.Key&&SessionAllocation.Pending(s,matches[id])))??false;
                TournamentRules.Need(hasSaved||hasRequested,"Select at least one program and match quantity for this session.");
            }
            var calculated=ScheduleCalculation.Calculate(s,new(request.Revision,request.EventId,request.Sessions,plan.MatchMinutes,plan.RestMinutes,ScheduleCalculation.Days(request.Sessions)));
            if(request.SessionIndex is int target)
            {
                var summary=SessionAllocation.Summary(calculated.Plan,calculated.Matches)[target];
                TournamentRules.Need(summary.Programs.All(p=>p.Scheduled>=p.Requested),$"Only {summary.Programs.Sum(p=>p.Scheduled)} of {summary.Programs.Sum(p=>p.Requested)} requested matches fit this session. Adjust the quantities or session settings.");
            }
    }

}
