using System.Collections.Concurrent;
using System.Text.Json;

namespace LiveScore.Api;

public static class SchedulePlannerEndpoints
{
    private sealed record Preview(long Revision,string EventId,PlannerDraft Draft,DateTimeOffset Expires);
    private static readonly ConcurrentDictionary<string,Preview> Previews=new();
    public static void Map(WebApplication app)
    {
        var group=app.MapGroup("/api/scheduling/planner");group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.MapGet("/{eventId}",(string eventId,SetupStore store)=>View(store.Read(),eventId));
        group.MapPost("/reset",(ResetScheduleRequest request,SetupStore store)=>
        {
            var saved=store.Change(request.Revision,"RESET_SCHEDULE",s=>
            {
                var plan=TournamentRules.Event(s,request.EventId);var nodes=SchedulePlanner.Slots(s,request.EventId);TournamentRules.Need(request.RoundKeys is {Length:>0}&&request.RoundKeys.All(key=>nodes.Any(n=>n.RoundKey==key)),"Select existing rounds to reset.");
                var actual=TournamentScheduler.Matches(s).Where(m=>request.RoundKeys.Contains(FixtureResets.Key(m))).Select(FixtureResets.Key).Distinct().ToArray();
                SchedulePlanner.RemoveRounds(s,plan,request.RoundKeys);if(actual.Length>0)FixtureResets.ResetSchedule(s,request with{RoundKeys=actual});
            });return View(saved,request.EventId);
        });
        group.MapPost("/draft",(PlannerRequest request,SetupStore store)=>
        {
            var current=store.Read();SetupStore.CheckRevision(current,request.Revision);
            var existing=TournamentRules.Event(current,request.EventId).SchedulePlanner?.Draft;
            if(existing is not null&&JsonSerializer.Serialize(existing,JsonDefaults.Options)==JsonSerializer.Serialize(request.Draft,JsonDefaults.Options))return View(current,request.EventId);
            var saved=store.Change(request.Revision,"SCHEDULE_PLANNER_DRAFT",s=>
            {
                var nodes=SchedulePlanner.Slots(s,request.EventId);SchedulePlanner.ValidateConfiguration(s,request.EventId,request.Draft,nodes);
                ProtectCurrent(s,request.EventId,request.Draft,nodes);
                var plan=TournamentRules.Event(s,request.EventId);plan.SchedulePlanner??=new(){Bookings=SchedulePlanner.Current(s,request.EventId,nodes)};plan.SchedulePlanner.Draft=request.Draft;TournamentRules.Save(s.EventPlans,plan,x=>x.EventId);
            });return View(saved,request.EventId);
        });
        group.MapPost("/preview",(PlannerRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);var nodes=SchedulePlanner.Slots(s,request.EventId);var draft=Clone(request.Draft);SchedulePlanner.ValidateConfiguration(s,request.EventId,draft,nodes);
            if(request.Action=="auto"){TournamentRules.Need(SchedulePlanner.Unprepared(s,request.EventId).Length==0,"Generate opening draws for all active badminton programs before Auto Assign.");draft=SchedulePlanner.Allocate(s,request.EventId,draft,nodes,null);}
            else if(request.Action=="add")
            {
                TournamentRules.Need(request.SlotId is not null&&nodes.Any(n=>n.Id==request.SlotId)&&draft.Placements.All(b=>b.SlotId!=request.SlotId),"Choose an unassigned match.");
                var session=draft.Sessions.SingleOrDefault(w=>w.Date==request.Date&&w.Start==request.SessionStart);TournamentRules.Need(session is not null,"Choose a session.");draft=SchedulePlanner.Allocate(s,request.EventId,draft,nodes,request.SlotId,[session!]);
            }
            else TournamentRules.Need(request.Action=="check","Choose a valid scheduling action.");
            ProtectCurrent(s,request.EventId,draft,nodes);
            var findings=SchedulePlanner.Check(s,request.EventId,draft,nodes);
            TournamentRules.Need(!findings.Any(f=>f.Kind=="E"),findings.FirstOrDefault(f=>f.Kind=="E")?.Message??"Review the schedule.");
            foreach(var old in Previews.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow))Previews.TryRemove(old.Key,out _);
            TournamentRules.Need(Previews.Count<100,"Too many previews. Let older previews expire.");var token=Guid.NewGuid().ToString("N");Previews[token]=new(s.Revision,request.EventId,draft,DateTimeOffset.UtcNow.AddMinutes(10));
            return new{token,s.Revision,draft,findings};
        });
        group.MapPost("/confirm",(PlannerConfirm request,SetupStore store)=>
        {
            TournamentRules.Need(Previews.TryGetValue(request.Token,out var preview),"Preview missing. Check the schedule again.");lock(preview!)
            {
                TournamentRules.Need(preview.Expires>DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Preview expired. Check the schedule again.");
                var saved=store.Change(request.Revision,"SCHEDULE_PLANNER_CONFIRM",s=>
                {
                    var plan=TournamentRules.Event(s,preview.EventId);var nodes=SchedulePlanner.Slots(s,preview.EventId);ProtectCurrent(s,preview.EventId,preview.Draft,nodes);
                    var findings=SchedulePlanner.Check(s,preview.EventId,preview.Draft,nodes,true);TournamentRules.Need(!findings.Any(f=>f.Kind=="E"),findings.FirstOrDefault(f=>f.Kind=="E")?.Message??"Complete the schedule.");TournamentRules.Need(request.AcceptWarnings||!findings.Any(f=>f.Kind=="W"),"Review the warnings before confirming.");
                    // Previously saved sessions can remain in the past; new starts must be future venue time.
                    ScheduleCalculation.ValidateSessionStarts(s,plan,preview.Draft.Sessions);
                    foreach(var b in preview.Draft.Placements){var n=nodes.Single(n=>n.Id==b.SlotId);if(n.MatchId is null)continue;var m=LiveRuntime.Find(s,n.MatchId);if(n.Fixed)continue;m.Start=b.Start;m.End=b.Start.AddMinutes(b.Minutes);m.ReviewAvailable=m.Court==b.Court&&m.ReviewAvailable;m.Court=b.Court;m.DurationMinutes=b.Minutes;}
                    plan.SchedulePlanner??=new();plan.SchedulePlanner.Bookings=preview.Draft.Placements;plan.SchedulePlanner.Minutes=preview.Draft.Minutes;plan.SchedulePlanner.RestWarningMinutes=preview.Draft.RestWarningMinutes;plan.SchedulePlanner.Draft=null;
                    plan.DailySessions=[];plan.Sessions=preview.Draft.Sessions.Select(w=>w with{MatchIds=null,ProgramLimits=null}).ToList();plan.ScheduleDays=ScheduleCalculation.Days(plan.Sessions);
                },auditView:s=>new{preview.EventId,plan=TournamentRules.Event(s,preview.EventId).SchedulePlanner});
                Previews.TryRemove(request.Token,out _);return View(saved,preview.EventId);
            }
        });
    }
    private static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,JsonDefaults.Options),JsonDefaults.Options)!;
    public static void ProtectCurrent(SetupData s,string eventId,PlannerDraft draft,List<PlannerSlot> nodes)
    {
        var current=SchedulePlanner.Current(s,eventId,nodes);foreach(var b in current)
        {
            var n=nodes.Single(n=>n.Id==b.SlotId);var proposed=draft.Placements.SingleOrDefault(x=>x.SlotId==b.SlotId);
            TournamentRules.Need(proposed is not null,"Reset a scheduled round explicitly; removing it from a draft cannot remove a booking.");
            if(n.Fixed)TournamentRules.Need(proposed.Start==b.Start&&proposed.Court==b.Court&&proposed.Minutes==b.Minutes,"Release called matches before moving them. Playing or completed matches cannot move.");
        }
    }
    private static object View(SetupData s,string eventId)
    {
        var ev=s.Events.SingleOrDefault(e=>e.Id==eventId);TournamentRules.Need(ev is not null,"Event not found.");var nodes=SchedulePlanner.Slots(s,eventId);var draft=SchedulePlanner.Draft(s,eventId,nodes);var booked=SchedulePlanner.Current(s,eventId,nodes);
        List<PlannerFinding> findings=[];try{if(draft.Sessions.Count>0)findings=SchedulePlanner.Check(s,eventId,draft,nodes);}catch(RuleException e){findings=[new("E",e.Message,[])];}
        return new{s.Revision,eventId,eventName=ev.Name,startDate=ev.StartDate,endDate=ev.EndDate,courts=Enumerable.Range(1,s.CourtCount).Where(c=>!s.Venue.ClosedCourts.Contains(c)).ToArray(),nodes,draft,booked,findings,unpreparedPrograms=SchedulePlanner.Unprepared(s,eventId),hasDraft=TournamentRules.Event(s,eventId).SchedulePlanner?.Draft is not null};
    }
}
