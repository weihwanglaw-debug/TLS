using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace LiveScore.Api;

public static class TournamentEndpoints
{
    private sealed record Generation(long Revision,string EventId,ProgramPlan[] Plans,DateTimeOffset Expires,GenerateRequest Request,Dictionary<string,long> Counters);
    private static readonly ConcurrentDictionary<string,Generation> Previews=new();
    public static void MapTournament(this WebApplication app)
    {
        var group=app.MapGroup("/api/tournament");
        group.AddEndpointFilter(async(c,next)=>
        {
            var ip=c.HttpContext.Connection.RemoteIpAddress;
            if(!LiveAccess.LocalAdmin(c.HttpContext))return Results.Json(new{message="Use the console on the local server."},statusCode:403);
            try{return await next(c);}catch(Exception ex)when(ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
            {return Results.BadRequest(new{message="Invalid tournament data. Check IDs, dates and required fields."});}
        });
        group.MapPost("/ranking-decision",(RankingDecisionRequest request,SetupStore store)=>{var s=store.Change(request.Revision,"RANKING_DECISION",s=>BadmintonStandings.Resolve(s,request));return View(s,s.Programs.Single(p=>p.Id==request.ProgramId).EventId);});
        group.MapGet("/{eventId}",(string eventId,SetupStore store)=>View(store.Read(),eventId));
        group.MapGet("/{eventId}/apply-draw-settings",(string eventId,string sourceProgramId,SetupStore store)=>DrawBatch.List(store.Read(),eventId,sourceProgramId));
        group.MapPost("/apply-draw-settings",(ApplyDrawSettingsRequest request,SetupStore store)=>
        {
            var count=0;var s=store.Change(request.Revision,"APPLY_DRAW_SETTINGS",s=>count=DrawBatch.Apply(s,request),db=>db.EventImports.Single(x=>x.EventId==request.EventId).Stage="F");
            return new{revision=s.Revision,count};
        });
        group.MapPut("/event-dates",(EventDatesRequest request,SetupStore store)=>View(store.Change(request.Revision,"EVENT_DATES",s=>EventDates.Update(s,request),auditView:s=>s.Events.Where(e=>e.Id==request.EventId).ToArray()),request.EventId));
        group.MapPut("/event",(PlanRequest request,SetupStore store)=>View(store.Change(request.Revision,"EVENT_PLAN",s=>
        {
            request.Plan.PlanningDraft=TournamentRules.Event(s,request.Plan.EventId).PlanningDraft;
            request.Plan.Publication=TournamentRules.Event(s,request.Plan.EventId).Publication;
            BusinessIds.Officials(s,request.Plan);
            TournamentRules.ValidateEvent(s,request.Plan);
            TournamentRules.Save(s.EventPlans,request.Plan,x=>x.EventId);
        }),request.Plan.EventId));
        group.MapPut("/draw-settings",(DrawSettingsRequest request,SetupStore store)=>View(store.Change(request.Revision,"DRAW_SETTINGS",s=>TournamentRules.ApplyDrawSettings(s,request)),request.EventId));
        group.MapPut("/seeds",(SeedsRequest request,SetupStore store)=>
        {
            var s=store.Change(request.Revision,"PROGRAM_SEEDS",s=>TournamentRules.SetSeeds(s,request));return View(s,s.Programs.Single(p=>p.Id==request.ProgramId).EventId);
        });
        group.MapPost("/preview",(GenerateRequest request,SetupStore store)=>
        {
            var s=store.Read();SetupStore.CheckRevision(s,request.Revision);var plan=TournamentRules.Event(s,request.EventId);if(!request.DrawOnly)TournamentRules.ValidateEvent(s,plan);
            TournamentRules.Need(request.ProgramIds is {Length:>0}&&request.ProgramIds.Distinct().Count()==request.ProgramIds.Length,"Select distinct programs.");
            if(!request.DrawOnly)
            {
            TournamentRules.Need(request.Courts is {Length:>0}&&request.Courts.Distinct().Count()==request.Courts.Length&&request.Courts.All(c=>c>=1&&c<=s.CourtCount),"Select valid venue courts.");
            TournamentRules.Need(!request.Courts.Any(c=>s.Venue.ClosedCourts.Contains(c)),"Select open courts for fixture generation.");
            TournamentRules.Need(request.MatchMinutes is null or >=1 and <=360,"Estimated match duration must be 1–360 minutes.");
            TournamentRules.Need(request.EarliestStart.Kind==DateTimeKind.Unspecified,"Enter earliest start as local venue time without a UTC offset.");
            var zone=SetupStore.Zone(s.TimeZone);TournamentRules.Need(!zone.IsInvalidTime(request.EarliestStart)&&!zone.IsAmbiguousTime(request.EarliestStart),"Earliest start falls on a daylight-saving clock change.");
            }
            TournamentRules.Need(GamePresets.Default(s) is not null,"Set an active default in Game Setting Preset before generating a draw.");
            var additions=new List<TournamentMatch>();var changed=new List<ProgramPlan>();
            var nextNumber=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==request.EventId)).Select(m=>m.Number).DefaultIfEmpty(0).Max()+1;
            foreach(var id in request.ProgramIds)
            {
                var p=s.Programs.SingleOrDefault(p=>p.Id==id&&p.EventId==request.EventId);TournamentRules.Need(p is not null&&p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D","Only active badminton singles/doubles programs can generate fixtures.");
                if(request.Settings is not null||request.Seeds is not null)
                {
                    TournamentRules.Need(request.ProgramIds.Length==1&&request.Settings is not null,"A first-draw preview needs one program and configuration.");
                    TournamentRules.ApplyDrawSettings(s,new(request.Revision,request.EventId,[id],request.Settings.GroupCount,request.Settings.AdvancePerGroup,request.Settings.Format));if(request.Seeds is not null)TournamentRules.SetSeeds(s,new(request.Revision,id,request.Seeds));
                }
                var pp=TournamentRules.Program(s,id);TournamentRules.Save(s.ProgramPlans,pp,x=>x.ProgramId);
                var matches=TournamentRules.CreateRound(s,pp);foreach(var m in matches)m.Number=nextNumber++;
                additions.AddRange(matches);changed.Add(pp);
            }
            TournamentRules.Need(additions.Count<=2000,"Generate fewer programs at a time (maximum 2000 new matches).");
            if(!request.DrawOnly)TournamentScheduler.Schedule(s,plan,additions,request);
            GamePresets.ApplyDefault(s,additions);
            foreach(var pp in changed)pp.Matches.AddRange(additions.Where(m=>m.ProgramId==pp.ProgramId));
            foreach(var item in Previews.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow))Previews.TryRemove(item.Key,out _);
            TournamentRules.Need(Previews.Count<50,"Too many previews. Let older previews expire.");
            var token=Guid.NewGuid().ToString("N");Previews[token]=new(request.Revision,request.EventId,changed.ToArray(),DateTimeOffset.UtcNow.AddMinutes(10),request,new(s.IdCounters));
            return new{token,revision=request.Revision,matches=additions,groups=changed.Select(p=>new{p.ProgramId,p.Groups}),view=View(s,request.EventId)};
        });
        group.MapPost("/preview-swap",(SwapPreviewRequest request,SetupStore store)=>
        {
            TournamentRules.Need(Previews.TryGetValue(request.Token,out var preview),"Draw preview is unavailable.");
            lock(preview)
            {
                TournamentRules.Need(Previews.ContainsKey(request.Token)&&preview.Expires>=DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Preview expired. Generate a fresh preview.");
                var s=store.Read();SetupStore.CheckRevision(s,request.Revision);
                TournamentRules.Need(preview.Plans.Length==1,"Swap positions in one program at a time.");
                var saved=TournamentRules.Program(s,preview.Plans[0].ProgramId);
                var first=saved.Matches.Count==0;
                var qualifier=!first&&saved.Format=="GK"&&saved.Phase=="G"&&saved.Matches.All(m=>m.Phase=="G"&&m.Status is MatchStatus.Completed or MatchStatus.Walkover);
                TournamentRules.Need(first||qualifier,"Swap positions only in the first draw or the unconfirmed qualifier knockout opening round.");
                var plan=JsonSerializer.Deserialize<ProgramPlan>(JsonSerializer.Serialize(preview.Plans[0],JsonDefaults.Options),JsonDefaults.Options)!;
                // Live result metadata is excluded from JSON cloning; keep saved group rows intact.
                var existing=saved.Matches.ToDictionary(m=>m.Id);
                plan.Matches=plan.Matches.Select(m=>existing.GetValueOrDefault(m.Id)??m).ToList();
                var existingIds=existing.Keys.ToHashSet();
                var targets=plan.Matches.Where(m=>!existingIds.Contains(m.Id)).ToList();
                if(qualifier)TournamentRules.Need(preview.Request.DrawOnly&&targets.Count>0&&targets.All(m=>m.Phase=="K"&&m.Round==1&&m.Start is null&&m.End is null&&m.Court is null&&m.Status is MatchStatus.Scheduled or MatchStatus.Bye),"Swap qualifier positions before confirming or scheduling the opening knockout round.");
                var ids=targets.SelectMany(m=>new[]{m.EntryA,m.EntryB}).Where(id=>id is not null).ToHashSet();
                TournamentRules.Need(request.EntryA!=request.EntryB&&ids.Contains(request.EntryA)&&ids.Contains(request.EntryB),"Choose two distinct entries from this draw.");
                var groupOf=plan.Groups.SelectMany(g=>g.EntryIds.Select(id=>(id,g.Id))).ToDictionary(x=>x.id,x=>x.Id);
                int SameGroup()=>targets.Count(m=>m.EntryB is not null&&groupOf.GetValueOrDefault(m.EntryA)==groupOf.GetValueOrDefault(m.EntryB));
                var conflicts=qualifier?SameGroup():0;
                string? Swap(string? id)=>id==request.EntryA?request.EntryB:id==request.EntryB?request.EntryA:id;
                foreach(var m in targets){m.EntryA=Swap(m.EntryA)!;m.EntryB=Swap(m.EntryB);if(m.Status==MatchStatus.Bye)m.WinnerId=m.EntryA;}
                if(qualifier)TournamentRules.Need(SameGroup()<=conflicts,"This swap would introduce a same-group opening knockout match. Choose a different pair.");
                else plan.Groups=plan.Groups.Select(g=>g with{EntryIds=g.EntryIds.Select(id=>Swap(id)!).ToArray()}).ToList();
                plan.Matches=plan.Matches.Where(m=>existingIds.Contains(m.Id)).ToList();
                TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
                if(!preview.Request.DrawOnly)TournamentScheduler.Schedule(s,TournamentRules.Event(s,preview.EventId),targets,preview.Request);
                plan.Matches.AddRange(targets);
                preview.Plans[0]=plan;TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
                return new{token=request.Token,revision=request.Revision,matches=targets,groups=new[]{new{plan.ProgramId,plan.Groups}},view=View(s,preview.EventId)};
            }
        });
        group.MapPost("/confirm",(GenerationConfirm request,SetupStore store)=>
        {
            TournamentRules.Need(request.Token is not null&&Previews.TryGetValue(request.Token,out _),"Generation preview missing or already confirmed.");
            var preview=Previews[request.Token];TournamentRules.Need(preview.Expires>=DateTimeOffset.UtcNow&&preview.Revision==request.Revision,"Preview expired or changed. Generate a fresh preview.");
            lock(preview)
            {
            TournamentRules.Need(Previews.ContainsKey(request.Token),"Preview has already been confirmed.");
            var s=store.Change(request.Revision,"GENERATE_ROUND",s=>
            {
                foreach(var counter in preview.Counters)s.IdCounters[counter.Key]=Math.Max(s.IdCounters.GetValueOrDefault(counter.Key),counter.Value);
                foreach(var plan in preview.Plans)
                {
                    var existing=TournamentRules.Program(s,plan.ProgramId).Matches.ToDictionary(m=>m.Id);
                    plan.Matches=plan.Matches.Select(m=>existing.GetValueOrDefault(m.Id)??m).ToList();
                    TournamentRules.Save(s.ProgramPlans,plan,x=>x.ProgramId);
                }
                var index=s.Imports.FindIndex(i=>i.EventId==preview.EventId);TournamentRules.Need(index>=0,"Import metadata is missing.");s.Imports[index]=s.Imports[index] with{Stage="F"};
            },db=>db.EventImports.Single(x=>x.EventId==preview.EventId).Stage="F");
            Previews.TryRemove(request.Token,out _);return View(s,preview.EventId);
            }
        });
        group.MapPost("/result-impact",(ResultRequest request,SetupStore store)=>ResultDependencies.Review(store.Read(),request));
        group.MapPut("/result",(ResultRequest request,SetupStore store)=>
        {
            string eventId="";
            var s=store.Change(request.Revision,"MATCH_RESULT",s=>
            {
                var plan=s.ProgramPlans.SingleOrDefault(p=>p.Matches.Any(m=>m.Id==request.MatchId));TournamentRules.Need(plan is not null,"Match not found.");
                var m=plan!.Matches.Single(m=>m.Id==request.MatchId);eventId=s.Programs.Single(p=>p.Id==m.ProgramId).EventId;
                TournamentRules.Need(m.Status!=MatchStatus.Playing||m.Live?.PaperMode==true||request.Outcome is not null,"Use the dashboard to switch the active match to paper before entering a manual result.");
                if(m.Live is not null&&request.MatchVersion!=m.Live.Version)throw new RuleException("Match data changed. Refresh before saving the result.",409);
                TournamentRules.Need(request.Reason is not null&&request.Reason.Length<=300,"Correction reason must be at most 300 characters.");
                ResultDependencies.Ready(m);
                ResultDependencies.Link(plan);
                var beforeDecision=ResultDependencies.Snapshot(plan);
                if(request.Outcome is not null)TournamentRules.Need(request.ImpactToken==ResultDependencies.Token(s,plan,request),"Review the result impact again; match or dependent data changed.");
                GamePresets.Assign(s,m,request.PresetId);
                var previousGames=m.Games.ToList();var previousWinner=m.Status==MatchStatus.Review?MatchResults.Winner(m,m.Games):m.WinnerId;var previousDetails=ResultValidation.Copy(m.ResultDetails);
                var hadResult=MatchStatus.Finished(m.Status)||m.Status==MatchStatus.Review;
                var proposedWinner=OfficialResults.Winner(m,request);
                var affected=request.Outcome is null?new List<TournamentMatch>():ResultDependencies.Affected(plan,m,proposedWinner);
                var changed=hadResult&&(request.Outcome is not null||!previousGames.SequenceEqual(request.Games)||previousWinner!=proposedWinner||request.FinishedAt.HasValue&&m.ActualFinishedAt.HasValue&&request.FinishedAt!=m.ActualFinishedAt||request.Details is not null&&JsonSerializer.Serialize(previousDetails,JsonDefaults.Options)!=JsonSerializer.Serialize(request.Details,JsonDefaults.Options));
                TournamentRules.Need(!changed||!string.IsNullOrWhiteSpace(request.Reason),"Provide a reason for correcting an existing or tablet-submitted result.");
                TournamentRules.Need(m.Status!="BY","BYE matches advance automatically and cannot be scored.");
                TournamentRules.Need(m.Start.HasValue&&m.End.HasValue&&m.Court.HasValue,"Schedule the match before entering a result.");
                TournamentRules.Need(request.Outcome is not null||(m.Phase=="G"?!plan.Matches.Any(x=>x.Phase=="K"):!plan.Matches.Any(x=>x.Phase=="K"&&x.Round>m.Round)),"A later round exists. Use Override Official Result to review its impact.");
                if(request.Outcome is not null)
                {m.WinnerId=proposedWinner;m.Games=request.Games;m.Status=request.Outcome=="W"?"WO":"C";m.ResultKind=request.Outcome;m.OfficialReason=request.Reason.Trim();}
                else if(request.WalkoverWinner is not null)
                {
                    TournamentRules.Need(request.WalkoverWinner==m.EntryA||request.WalkoverWinner==m.EntryB,"Choose one of the match entries as walkover winner.");m.WinnerId=request.WalkoverWinner;m.Games=[];m.Status="WO";m.ResultKind="W";
                }
                else
                {
                    m.WinnerId=MatchResults.Winner(m,request.Games);m.Games=request.Games;m.Status="C";m.ResultKind="N";
                }
                m.Live??=new();
                if(changed)m.Live.Corrections.Add(new(DateTimeOffset.UtcNow,request.Reason,previousGames,previousWinner,previousDetails));
                if(request.Details is not null){ResultValidation.Details(s,m,request.Details);m.Live.Details=request.Details;}
                if(request.StartedAt.HasValue)
                {
                    TournamentRules.Need(request.StartedAt.Value.Kind==DateTimeKind.Unspecified,"Enter actual start as local venue time.");
                    var ev=s.Events.Single(e=>e.Id==eventId);var date=DateOnly.FromDateTime(request.StartedAt.Value);TournamentRules.Need(date>=ev.StartDate&&date<=ev.EndDate,"Actual start must be within the event dates.");
                    TournamentRules.Need(m.Live.StartedLocal is null||m.Live.StartedLocal==request.StartedAt.Value,"Preserve the start time recorded by the tablet.");
                    m.Live.StartedLocal=request.StartedAt;m.Live.StartedAtUtc=LiveRuntime.Utc(s,request.StartedAt.Value);m.Live.TimeZone=s.TimeZone;
                }
                if(m.Live.PaperMode)m.Live.ResultSource="P";else if(m.Live.ResultSource!="T")m.Live.ResultSource="M";
                if(m.Live.ResultSource!="T"||!previousGames.SequenceEqual(m.Games))m.Live.RallyHistoryComplete=false;
                TournamentRules.Need(request.FinishedAt is null||request.FinishedAt.Value.Kind==DateTimeKind.Unspecified,"Enter finish as local venue time without a UTC offset.");
                TournamentRules.Need(request.FinishedAt is null||request.FinishedAt>=TournamentScheduler.Begin(m),"Finish time must not precede the match start.");
                if(request.FinishedAt.HasValue)
                {
                    var imported=s.Events.Single(e=>e.Id==eventId);TournamentRules.Need(m.Live?.StartedAtUtc is not null||DateOnly.FromDateTime(request.FinishedAt.Value)<=imported.EndDate,"Finish time must be within the event dates.");
                    m.ActualFinishedAt=request.FinishedAt;
                }
                if(m.Live is not null){m.Live.History.Clear();m.Live.Receipts.Clear();m.Live.Version++;m.Live.SubmittedAtUtc=request.FinishedAt.HasValue?LiveRuntime.Utc(s,request.FinishedAt.Value):m.Live.SubmittedAtUtc??DateTimeOffset.UtcNow;}
                ResultDependencies.Apply(s,plan,m,request,affected);
                ResultDependencies.Refresh(plan);
                LiveRuntime.CompleteProgram(s,m);
                if(request.Outcome is not null)plan.OfficialDecisions.Add(new(DateTimeOffset.UtcNow,m.Id,request.Reason,request.DownstreamHandling??"U",beforeDecision,ResultDependencies.Snapshot(plan)));
            });return View(s,eventId);
        });
        group.MapPut("/schedule",(ScheduleRequest request,SetupStore store)=>
        {
            string eventId="";var s=store.Change(request.Revision,"MATCH_SCHEDULE",s=>
            {
                var m=TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==request.MatchId);TournamentRules.Need(m is not null&&m.Status=="SC","Only unplayed matches can be rescheduled.");
                TournamentRules.Need(!s.Venue.Bindings.Any(b=>b.MatchId==m!.Id),"Release the dashboard assignment before rescheduling this match.");
                TournamentRules.Need(request.Start.Kind==DateTimeKind.Unspecified,"Enter start as local venue time without a UTC offset.");eventId=s.Programs.Single(p=>p.Id==m!.ProgramId).EventId;m!.ReviewAvailable=request.ReviewAvailable??(m.Court==request.Court&&m.ReviewAvailable);m.Court=request.Court;m.Start=request.Start;m.End=request.Start.AddMinutes(m.DurationMinutes);m.UmpireId=request.UmpireId;m.ServiceJudgeId=request.ServiceJudgeId;m.LineJudgeIds=request.LineJudgeIds??[];
                TournamentScheduler.ValidateOverride(s,m);
            });return View(s,eventId);
        });
        group.MapPost("/clear-result",(ClearResultRequest request,SetupStore store)=>
        {
            string eventId="";
            var s=store.Change(request.Revision,"MATCH_RESULT_CLEAR",s=>
            {
                var plan=s.ProgramPlans.SingleOrDefault(p=>p.Matches.Any(m=>m.Id==request.MatchId));TournamentRules.Need(plan is not null,"Match not found.");
                var m=plan.Matches.Single(m=>m.Id==request.MatchId);eventId=s.Programs.Single(p=>p.Id==m.ProgramId).EventId;
                TournamentRules.Need(m.Status is MatchStatus.Completed or MatchStatus.Walkover,"Only a completed result can be cleared.");
                if(m.MatchVersion!=request.MatchVersion)throw new RuleException("The result changed. Refresh before clearing it.",409);
                TournamentRules.Need(m.Phase=="G"?!plan.Matches.Any(x=>x.Phase=="K"):!plan.Matches.Any(x=>x.Phase=="K"&&x.Round>m.Round),"A dependent round exists. Reset the unplayed latest round first.");
                TournamentRules.Need(!string.IsNullOrWhiteSpace(request.Reason)&&request.Reason.Length<=300,"Provide a reason for clearing the result.");
                m.Live??=new();m.Live.Corrections.Add(new(DateTimeOffset.UtcNow,request.Reason,m.Games.ToList(),m.WinnerId,ResultValidation.Copy(m.ResultDetails)));
                m.Status=MatchStatus.Scheduled;m.WinnerId=null;m.Games=[];m.ActualFinishedAt=null;m.Live.Details=new();m.Live.A=0;m.Live.B=0;m.Live.StartedAtUtc=null;m.Live.StartedLocal=null;m.Live.SubmittedAtUtc=null;m.Live.AwaitingNextGame=false;m.Live.PaperMode=false;m.Live.History.Clear();m.Live.Receipts.Clear();m.Live.Version++;m.Live.ResultSource="";m.Live.Serving=null;m.Live.Rallies.Clear();m.Live.RallyHistoryComplete=false;m.Live.IntervalUntil=null;
                plan.Phase=m.Phase;s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
            });return View(s,eventId);
        });
        group.MapGet("/{eventId}/reset-schedule",(string eventId,SetupStore store)=>FixtureResets.List(store.Read(),eventId));
        group.MapPost("/reset-schedule",(ResetScheduleRequest request,SetupStore store)=>
        {
            var s=store.Change(request.Revision,"RESET_SCHEDULE",s=>FixtureResets.ResetSchedule(s,request));return View(s,request.EventId);
        });
        group.MapPost("/reset-seeding",(ResetRequest request,SetupStore store)=>
        {
            var s=store.Change(request.Revision,"RESET_SEEDING",s=>FixtureResets.ResetSeeds(s,request.ProgramId));return new{s.Revision};
        });
        group.MapPost("/reset-latest-round",(ResetRequest request,SetupStore store)=>
        {
            var eventId=store.Read().Programs.Single(p=>p.Id==request.ProgramId).EventId;
            var s=store.Change(request.Revision,"RESET_LATEST_ROUND",s=>
            {
                var plan=TournamentRules.Program(s,request.ProgramId);var latest=plan.Matches.Where(m=>m.Phase=="K"&&m.Round==plan.LastRound).ToList();
                TournamentRules.Need(latest.Count>0&&latest.All(m=>m.Status is MatchStatus.Scheduled or MatchStatus.Bye)&&!latest.Any(m=>GamePresets.Locked(m)),"Only an entirely unplayed latest knockout round can be reset.");
                TournamentRules.Need(!s.Venue.Bindings.Any(b=>latest.Any(m=>m.Id==b.MatchId)),"Release waiting tablet assignments before resetting the round.");
                FixtureResets.RequireScheduleReset(s,eventId,latest);
                plan.Matches.RemoveAll(m=>latest.Contains(m));plan.LastRound=plan.Matches.Where(m=>m.Phase=="K").Select(m=>m.Round).DefaultIfEmpty(0).Max();plan.Phase=plan.LastRound>0?"K":plan.Format=="GK"?"G":"P";
                if(plan.Matches.Count==0)plan.Groups=[];TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
            },db=>
            {
                var rows=db.ProgramOperations.ToList().Select(x=>JsonSerializer.Deserialize<ProgramPlan>(x.Json,JsonDefaults.Options)!);
                if(!rows.Any(pp=>db.Programs.Any(x=>x.Id==pp.ProgramId&&x.EventId==eventId)&&pp.Matches.Count>0))db.EventImports.Single(x=>x.EventId==eventId).Stage="P";
            });return View(s,eventId);
        });
        group.MapPost("/reset",(ResetRequest request,SetupStore store)=>
        {
            var eventId=store.Read().Programs.Single(p=>p.Id==request.ProgramId).EventId;
            var s=store.Change(request.Revision,"RESET_DRAW",s=>
            {
                var p=TournamentRules.Program(s,request.ProgramId);TournamentRules.Need(p.Matches.All(m=>m.Status is "SC" or "BY")&&!p.Matches.Any(m=>GamePresets.Locked(m)),"Cannot reset a draw after results have been entered.");FixtureResets.RequireScheduleReset(s,eventId,p.Matches);p.Matches=[];p.Groups=[];p.Phase="P";p.LastRound=0;
                TournamentRules.Save(s.ProgramPlans,p,x=>x.ProgramId);
                if(!s.ProgramPlans.Any(pp=>s.Programs.Any(x=>x.Id==pp.ProgramId&&x.EventId==eventId)&&pp.Matches.Count>0))
                {var index=s.Imports.FindIndex(i=>i.EventId==eventId);s.Imports[index]=s.Imports[index] with{Stage="P"};}
            },db=>
            {
                // Determine replacement eligibility from the transaction's saved operation rows.
                var rows=db.ProgramOperations.ToList().Select(x=>JsonSerializer.Deserialize<ProgramPlan>(x.Json,JsonDefaults.Options)!);
                if(!rows.Any(pp=>db.Programs.Any(x=>x.Id==pp.ProgramId&&x.EventId==eventId)&&pp.Matches.Count>0))db.EventImports.Single(x=>x.EventId==eventId).Stage="P";
            });return View(s,eventId);
        });
    }
    private static object View(SetupData s,string eventId)
    {
        var ev=s.Events.SingleOrDefault(e=>e.Id==eventId);TournamentRules.Need(ev is not null,"Event not found.");
        var programIds=s.Programs.Where(p=>p.EventId==eventId).Select(p=>p.Id).ToHashSet();
        var plans=programIds.Select(id=>TournamentRules.Program(s,id)).ToList();
        foreach(var p in plans)foreach(var m in p.Matches)m.PresetLocked=GamePresets.Locked(m);
        return new{calledMatchIds=s.Venue.Bindings.Select(b=>b.MatchId).Where(id=>plans.Any(p=>p.Matches.Any(m=>m.Id==id))).ToArray(),datesEditable=EventDates.Editable(s,eventId),warnings=TournamentScheduler.Warnings(s,eventId),revision=s.Revision,timeZone=s.TimeZone,courtCount=s.CourtCount,closedCourts=s.Venue.ClosedCourts,progressionLocked=EventOperations.Started(s,eventId),eventInfo=ev,eventPlan=TournamentRules.Event(s,eventId),dailySessions=EventOperations.LegacyDaily(s,TournamentRules.Event(s,eventId)),eventCourts=EventOperations.Courts(s,TournamentRules.Event(s,eventId)),presets=s.Venue.Presets.Where(p=>p.Status=="A"),progress=plans.Select(p=>new{p.ProgramId,waiting=EventOperations.Waiting(s,p)}),programs=s.Programs.Where(p=>p.EventId==eventId),participants=s.Participants.Where(p=>p.EventId==eventId),entries=s.Entries.Where(e=>e.EventId==eventId),plans,
            standings=plans.SelectMany(p=>p.Groups.Select(g=>new{p.ProgramId,groupId=g.Id,rows=TournamentRules.Standings(p,g),pending=BadmintonStandings.Pending(p,g)}))};
    }
}
