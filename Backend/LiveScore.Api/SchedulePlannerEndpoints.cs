using System.Collections.Concurrent;
using System.Text.Json;

namespace LiveScore.Api;

public sealed record PlannerChange(long Revision,string EventId,string SlotId,DateTime Start,int Court,string? OtherSlotId=null,bool AcceptWarnings=false);
public sealed record EarlierRequest(long Revision,string EventId,string MatchId);

public static class SchedulePlannerEndpoints
{
    private sealed record Preview(long Revision,string EventId,ScheduleProposal Plan,DateTimeOffset Expires);
    private static readonly ConcurrentDictionary<string,Preview> Previews=new();
    private sealed record EarlierPreview(long Revision,string EventId,string MatchId,EarlierSchedule Schedule,DateTimeOffset Expires);
    private static readonly ConcurrentDictionary<string,EarlierPreview> EarlierPreviews=new();
    public static void Map(WebApplication app)
    {
        var group=app.MapGroup("/api/scheduling/planner");group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.MapGet("/{eventId}",(string eventId,SetupStore store)=>View(store.Read(),eventId));
        group.MapPost("/earlier-preview",(EarlierRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);var result=SchedulePlanner.Earlier(s,request.EventId,request.MatchId);
            foreach(var old in EarlierPreviews.Where(p=>p.Value.Expires<DateTimeOffset.UtcNow))EarlierPreviews.TryRemove(old.Key,out _);
            TournamentRules.Need(EarlierPreviews.Count<100,"Too many previews. Let older previews expire.");
            var token=Guid.NewGuid().ToString("N");EarlierPreviews[token]=new(s.Revision,request.EventId,request.MatchId,result,DateTimeOffset.UtcNow.AddMinutes(10));
            return new{token,s.Revision,result.Changes,result.Unchanged,result.Findings};
        });
        group.MapPost("/earlier-confirm",(PlannerConfirm request,SetupStore store)=>
        {
            TournamentRules.Need(EarlierPreviews.TryGetValue(request.Token,out var preview),"Preview missing. Review earlier times again.");lock(preview!)
            {
                TournamentRules.Need(EarlierPreviews.ContainsKey(request.Token)&&preview.Expires>DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Preview expired. Review earlier times again.");
                var saved=store.Change(request.Revision,"SCHEDULE_EARLIER",s=>
                {
                    var fresh=SchedulePlanner.Earlier(s,preview.EventId,preview.MatchId);
                    TournamentRules.Need(fresh.Changes.Count>0,"No matches can move earlier.");
                    TournamentRules.Need(JsonSerializer.Serialize(fresh.Changes,JsonDefaults.Options)==JsonSerializer.Serialize(preview.Schedule.Changes,JsonDefaults.Options)&&JsonSerializer.Serialize(fresh.Findings,JsonDefaults.Options)==JsonSerializer.Serialize(preview.Schedule.Findings,JsonDefaults.Options),"Schedule checks changed. Review earlier times again.");
                    TournamentRules.Need(!fresh.Findings.Any(f=>f.Kind=="E"),"The proposed earlier times are no longer available.");
                    TournamentRules.Need(request.AcceptWarnings||!fresh.Findings.Any(f=>f.Kind=="W"),"Review the warnings before moving matches earlier.");
                    SchedulePlanner.ApplyBookings(s,preview.EventId,fresh.Changes.Select(c=>c.After));
                    TournamentRules.Event(s,preview.EventId).SchedulePlanner!.WalkoverMoves[preview.MatchId]=fresh.Changes;
                },auditView:s=>new{preview.EventId,preview.MatchId,changes=preview.Schedule.Changes});
                EarlierPreviews.TryRemove(request.Token,out _);return View(saved,preview.EventId);
            }
        });
        group.MapPost("/reset-preview",(ResetScheduleRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);return FixtureResets.List(s,request.EventId,request.FromDate);
        });
        group.MapPost("/reset",(ResetScheduleRequest request,SetupStore store)=>
        {
            var saved=store.Change(request.Revision,"RESET_SCHEDULE",s=>FixtureResets.ResetSchedule(s,request),auditView:s=>FixtureResets.List(s,request.EventId,request.FromDate));return View(saved,request.EventId);
        });
        group.MapPost("/preview",(PlannerRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);var nodes=SchedulePlanner.Slots(s,request.EventId);var proposal=Clone(request.Plan);SchedulePlanner.ValidateConfiguration(s,request.EventId,proposal,nodes);
            if(request.Action=="auto"){TournamentRules.Need(SchedulePlanner.Unprepared(s,request.EventId).Length==0,"Generate opening draws for all active badminton programs before Auto Assign.");proposal=SchedulePlanner.Allocate(s,request.EventId,proposal,nodes,null);}
            else if(request.Action=="add")
            {
                TournamentRules.Need(request.SlotId is not null&&nodes.Any(n=>n.Id==request.SlotId)&&proposal.Placements.All(b=>b.SlotId!=request.SlotId),"Choose an unassigned match.");
                var session=proposal.Sessions.SingleOrDefault(w=>w.Date==request.Date&&w.Start==request.SessionStart);TournamentRules.Need(session is not null,"Choose a session.");proposal=SchedulePlanner.Allocate(s,request.EventId,proposal,nodes,request.SlotId,[session!]);
            }
            else TournamentRules.Need(request.Action=="check","Choose a valid scheduling action.");
            ProtectCurrent(s,request.EventId,proposal,nodes);
            var findings=SchedulePlanner.Check(s,request.EventId,proposal,nodes);
            TournamentRules.Need(!findings.Any(f=>f.Kind=="E"),findings.FirstOrDefault(f=>f.Kind=="E")?.Message??"Review the schedule.");
            foreach(var old in Previews.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow))Previews.TryRemove(old.Key,out _);
            TournamentRules.Need(Previews.Count<100,"Too many previews. Let older previews expire.");var token=Guid.NewGuid().ToString("N");Previews[token]=new(s.Revision,request.EventId,proposal,DateTimeOffset.UtcNow.AddMinutes(10));
            return new{token,s.Revision,plan=proposal,findings};
        });
        group.MapPost("/change",(PlannerChange request,SetupStore store)=>
        {
            var snapshot=store.Read();SetupStore.CheckRevision(snapshot,request.Revision);
            var checkedChange=ChangeProposal(snapshot,request);var warnings=checkedChange.Findings.Any(f=>f.Kind=="W");
            if(warnings&&!request.AcceptWarnings)return Results.Ok(new{saved=false,revision=snapshot.Revision,findings=checkedChange.Findings});
            var saved=store.Change(request.Revision,"SCHEDULE_PLANNER_CHANGE",s=>
            {
                var change=ChangeProposal(s,request);TournamentRules.Need(request.AcceptWarnings||!change.Findings.Any(f=>f.Kind=="W"),"Review the change warnings.");
                var plan=TournamentRules.Event(s,request.EventId);var state=plan.SchedulePlanner!;
                foreach(var b in change.Plan.Placements.Where(b=>change.Ids.Contains(b.SlotId)))
                {
                    var node=change.Nodes.Single(n=>n.Id==b.SlotId);
                    if(node.MatchId is {} id){var match=LiveRuntime.Find(s,id);match.ReviewAvailable=match.Court==b.Court&&match.ReviewAvailable;match.Start=b.Start;match.End=b.Start.AddMinutes(b.Minutes);match.Court=b.Court;match.DurationMinutes=b.Minutes;}
                    state.Bookings.RemoveAll(old=>old.SlotId==b.SlotId);state.Bookings.Add(b);
                }
                state.HasConfirmed=true;
            },auditView:s=>new{request.EventId,request.SlotId,request.OtherSlotId,bookings=TournamentRules.Event(s,request.EventId).SchedulePlanner!.Bookings.Where(b=>checkedChange.Ids.Contains(b.SlotId))});
            return Results.Ok(new{saved=true,view=View(saved,request.EventId)});
        });
        group.MapPost("/confirm",(PlannerConfirm request,SetupStore store)=>
        {
            TournamentRules.Need(Previews.TryGetValue(request.Token,out var preview),"Preview missing. Check the schedule again.");lock(preview!)
            {
                TournamentRules.Need(preview.Expires>DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Preview expired. Check the schedule again.");
                var saved=store.Change(request.Revision,"SCHEDULE_PLANNER_CONFIRM",s=>
                {
                    var plan=TournamentRules.Event(s,preview.EventId);var nodes=SchedulePlanner.Slots(s,preview.EventId);ProtectCurrent(s,preview.EventId,preview.Plan,nodes);
                    var findings=SchedulePlanner.Check(s,preview.EventId,preview.Plan,nodes,true);TournamentRules.Need(!findings.Any(f=>f.Kind=="E"),findings.FirstOrDefault(f=>f.Kind=="E")?.Message??"Complete the schedule.");TournamentRules.Need(request.AcceptWarnings||!findings.Any(f=>f.Kind=="W"),"Review the warnings before confirming.");
                    // Previously saved sessions can remain in the past; new starts must be future venue time.
                    ScheduleCalculation.ValidateSessionStarts(s,plan,preview.Plan.Sessions);
                    foreach(var b in preview.Plan.Placements){var n=nodes.Single(n=>n.Id==b.SlotId);if(n.MatchId is null)continue;var m=LiveRuntime.Find(s,n.MatchId);if(n.Fixed)continue;m.Start=b.Start;m.End=b.Start.AddMinutes(b.Minutes);m.ReviewAvailable=m.Court==b.Court&&m.ReviewAvailable;m.Court=b.Court;m.DurationMinutes=b.Minutes;}
                    plan.SchedulePlanner??=new();plan.SchedulePlanner.HasConfirmed=true;plan.SchedulePlanner.Bookings=preview.Plan.Placements;plan.SchedulePlanner.Minutes=preview.Plan.Minutes;plan.SchedulePlanner.RestWarningMinutes=preview.Plan.RestWarningMinutes;plan.SchedulePlanner.StartStepMinutes=preview.Plan.StartStepMinutes;
                    plan.DailySessions=[];plan.Sessions=preview.Plan.Sessions.Select(w=>w with{MatchIds=null,ProgramLimits=null}).ToList();plan.ScheduleDays=ScheduleCalculation.Days(plan.Sessions);
                    TournamentRules.Save(s.EventPlans,plan,p=>p.EventId);
                },auditView:s=>new{preview.EventId,plan=TournamentRules.Event(s,preview.EventId).SchedulePlanner});
                Previews.TryRemove(request.Token,out _);return View(saved,preview.EventId);
            }
        });
    }
    private sealed record CheckedChange(ScheduleProposal Plan,List<PlannerSlot> Nodes,HashSet<string> Ids,List<PlannerFinding> Findings);
    private static CheckedChange ChangeProposal(SetupData s,PlannerChange request)
    {
        var plan=TournamentRules.Event(s,request.EventId);TournamentRules.Need(SchedulePlanner.Confirmed(plan),"Confirm the initial timetable before applying a direct change.");
        var nodes=SchedulePlanner.Slots(s,request.EventId);var placements=SchedulePlanner.Current(s,request.EventId,nodes);var selected=placements.SingleOrDefault(b=>b.SlotId==request.SlotId);TournamentRules.Need(selected is not null,"Choose a confirmed match booking.");
        var ids=new HashSet<string>{request.SlotId};
        if(request.OtherSlotId is {} otherId)
        {
            TournamentRules.Need(otherId!=request.SlotId,"Choose a different match.");var other=placements.SingleOrDefault(b=>b.SlotId==otherId);TournamentRules.Need(other is not null,"Choose another confirmed booking.");SchedulePlanner.ValidateSwap(nodes.Single(n=>n.Id==request.SlotId),nodes.Single(n=>n.Id==otherId));ids.Add(otherId);
            placements[placements.IndexOf(selected!)]=selected! with{Start=other!.Start,Court=other.Court,Manual=true};placements[placements.IndexOf(other)]=other with{Start=selected!.Start,Court=selected.Court,Manual=true};
        }
        else placements[placements.IndexOf(selected!)]=selected! with{Start=request.Start,Court=request.Court,Manual=true};
        TournamentRules.Need(nodes.Where(n=>ids.Contains(n.Id)).All(n=>!n.Fixed),"Release called matches before moving them. Playing or completed matches cannot move.");
        var proposal=new ScheduleProposal(EventOperations.Sessions(s,plan).Select(w=>w with{Courts=w.Courts??EventOperations.Courts(s,plan)}).ToList(),nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Minutes),plan.SchedulePlanner!.RestWarningMinutes,placements,plan.SchedulePlanner.StartStepMinutes);
        var findings=SchedulePlanner.Check(s,request.EventId,proposal,nodes,false,ids).Where(f=>f.SlotIds.Length==0||f.SlotIds.Any(ids.Contains)).ToList();
        TournamentRules.Need(!findings.Any(f=>f.Kind=="E"),findings.FirstOrDefault(f=>f.Kind=="E")?.Message??"Review the change.");
        return new(proposal,nodes,ids,findings);
    }
    private static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,JsonDefaults.Options),JsonDefaults.Options)!;
    public static void ProtectCurrent(SetupData s,string eventId,ScheduleProposal proposal,List<PlannerSlot> nodes)
    {
        var current=SchedulePlanner.Current(s,eventId,nodes);foreach(var b in current)
        {
            var n=nodes.Single(n=>n.Id==b.SlotId);var proposed=proposal.Placements.SingleOrDefault(x=>x.SlotId==b.SlotId);
            TournamentRules.Need(proposed is not null,"Reset a scheduled round explicitly; removing it from a proposal cannot remove a booking.");
            if(n.Fixed)TournamentRules.Need(proposed.Start==b.Start&&proposed.Court==b.Court&&proposed.Minutes==b.Minutes,"Release called matches before moving them. Playing or completed matches cannot move.");
        }
    }
    private static object View(SetupData s,string eventId)
    {
        var ev=s.Events.SingleOrDefault(e=>e.Id==eventId);TournamentRules.Need(ev is not null,"Event not found.");var nodes=SchedulePlanner.Slots(s,eventId);var proposal=SchedulePlanner.Configuration(s,eventId,nodes);var booked=SchedulePlanner.Current(s,eventId,nodes);
        List<PlannerFinding> findings=[];try{if(proposal.Sessions.Count>0)findings=SchedulePlanner.Check(s,eventId,proposal,nodes);}catch(RuleException e){findings=[new("E",e.Message,[])];}
        var walkovers=nodes.Where(n=>n.Status==MatchStatus.Walkover&&n.MatchId is not null).Select(n=>{var m=LiveRuntime.Find(s,n.MatchId!);return new{slotId=n.Id,matchId=m.Id,version=m.MatchVersion,winner=m.WinnerId==m.EntryA?n.LabelA:n.LabelB,winnerSide=m.WinnerId==m.EntryA?"A":"B",hasEarlierTimes=TournamentRules.Event(s,eventId).SchedulePlanner?.WalkoverMoves.ContainsKey(m.Id)??false,booking=m.Start.HasValue&&m.Court.HasValue?new PlannerBooking(n.Id,m.Start.Value,m.Court.Value,m.DurationMinutes):null};}).ToArray();
        return new{s.Revision,eventId,walkovers,eventName=ev.Name,startDate=ev.StartDate,endDate=ev.EndDate,courts=Enumerable.Range(1,s.CourtCount).Where(c=>!s.Venue.ClosedCourts.Contains(c)).ToArray(),nodes,plan=proposal,booked,findings,unpreparedPrograms=SchedulePlanner.Unprepared(s,eventId),hasConfirmed=SchedulePlanner.Confirmed(TournamentRules.Event(s,eventId)),notices=TournamentRules.Event(s,eventId).SchedulePlanner?.Notices??[]};
    }
}
