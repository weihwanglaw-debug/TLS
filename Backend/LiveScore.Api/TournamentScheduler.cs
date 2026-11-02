namespace LiveScore.Api;



public static class TournamentScheduler

{

    public static IEnumerable<TournamentMatch> Matches(SetupData data)=>data.ProgramPlans.SelectMany(p=>p.Matches);

    public static DateTime Begin(TournamentMatch m)=>m.Live?.StartedLocal??m.Start??DateTime.MinValue;
    public static DateTime Finish(TournamentMatch m)
    {
        if(m.Status==MatchStatus.Playing&&m.Live?.StartedLocal is DateTime start)
        {
            var expected=start.AddMinutes(m.DurationMinutes);var now=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,SetupStore.Zone(m.Live.TimeZone)).DateTime;
            return expected>now?expected:now.AddMinutes(5);
        }
        return m.ActualFinishedAt??m.End??DateTime.MinValue;
    }

    public static DateTime PrerequisiteEnd(ProgramPlan plan,TournamentMatch match)

    {

        if(match.Phase!="K")return DateTime.MinValue;

        return plan.Matches.Where(m=>match.Round==1?m.Phase=="G":m.Phase=="K"&&m.Round<match.Round).Select(Finish).DefaultIfEmpty(DateTime.MinValue).Max();

    }

    public static void Schedule(SetupData data,EventPlan eventPlan,List<TournamentMatch> additions,GenerateRequest request,bool assignOfficials=true)

    {

        TournamentRules.Need(request.Courts.All(c=>EventOperations.Courts(data,eventPlan).Contains(c)),"Choose courts from Master Setting.");
        var sessions=EventOperations.Sessions(data,eventPlan).OrderBy(s=>s.Date).ThenBy(s=>s.Start).ToList();

        var existing=Matches(data).Where(m=>m.Status!="BY"&&m.Start.HasValue&&m.End.HasValue).ToList();

        foreach(var match in additions.Where(m=>m.Status!="BY"))

        {


            var dependency=EventOperations.PrerequisiteEnd(data,match);

            var earliest=request.EarliestStart;if(dependency!=DateTime.MinValue&&earliest<dependency.AddMinutes(eventPlan.RestMinutes))earliest=dependency.AddMinutes(eventPlan.RestMinutes);

            var duration=request.MatchMinutes??eventPlan.MatchMinutes;

            (DateTime Start,int Court,string Official)? best=null;

            var players=TournamentRules.Players(data,match);

            foreach(var session in sessions)

            {

                if(session.MatchIds is not null&&!session.MatchIds.Contains(match.Id))continue;
                var sessionStart=session.Date.ToDateTime(session.Start);var sessionEnd=session.Date.ToDateTime(session.End);

                if(best.HasValue&&sessionStart>best.Value.Start)break;

                foreach(var court in request.Courts.Where(c=>session.Courts is null||session.Courts.Contains(c)).Order())foreach(var official in assignOfficials?eventPlan.Officials:new List<Official>{new("","")})

                {

                    var time=earliest>sessionStart?earliest:sessionStart;

                    while(time.AddMinutes(duration)<=sessionEnd)

                    {

                        if(best.HasValue&&time>best.Value.Start)break;

                        var conflicts=existing.Select(m=>new {Match=m,PlayerConflict=TournamentRules.Players(data,m).Overlaps(players)})

                            .Where(x=>TournamentRules.Overlap(time,time.AddMinutes(duration),Begin(x.Match).AddMinutes(x.PlayerConflict?-eventPlan.RestMinutes:0),Finish(x.Match).AddMinutes(x.PlayerConflict?eventPlan.RestMinutes:0))

                                &&(x.Match.Court==court||TournamentRules.OfficialIds(x.Match).Contains(official.Id)||TournamentRules.OfficialIds(x.Match).Intersect(TournamentRules.OfficialIds(match)).Any()||x.PlayerConflict)).ToList();

                        if(conflicts.Count==0)

                        {

                            if(!best.HasValue||time<best.Value.Start)best=(time,court,official.Id);break;

                        }

                        var next=conflicts.Max(x=>Finish(x.Match).AddMinutes(x.PlayerConflict?eventPlan.RestMinutes:0));

                        time=next>time?next:time.AddMinutes(1);

                    }

                }

            }

            TournamentRules.Need(best.HasValue,$"Cannot fit match {match.Number} within the event's sessions. Add sessions, use more courts/officials, or review match duration and rest. No fixtures were saved.");

            match.Start=best!.Value.Start;match.End=match.Start.Value.AddMinutes(duration);match.Court=best.Value.Court;match.UmpireId=assignOfficials?best.Value.Official:null;match.DurationMinutes=duration;

            existing.Add(match);

        }

    }

    public static string[] Warnings(SetupData data,string eventId)

    {

        var relevant=Matches(data).Where(m=>data.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==eventId)).ToList();

        var messages=new List<string>();

        foreach(var played in relevant.Where(m=>m.ActualFinishedAt.HasValue&&m.End.HasValue&&m.ActualFinishedAt>m.End))

        foreach(var next in relevant.Where(m=>m.Status=="SC"&&m.Start.HasValue&&m.End.HasValue))

        {

            var plan=EventRest(data,next);var shared=TournamentRules.Players(data,played).Overlaps(TournamentRules.Players(data,next));

            if(TournamentRules.Overlap(next.Start!.Value,next.End!.Value,Begin(played),Finish(played).AddMinutes(shared?plan:0))&&(next.Court==played.Court||OfficialIdsOverlap(next,played)||shared))

                messages.Add($"Match {next.Number} conflicts with the recorded finish of match {played.Number}. Review the remaining schedule.");

        }

        return messages.Distinct().Take(25).ToArray();

    }

    private static bool OfficialIdsOverlap(TournamentMatch a,TournamentMatch b)=>TournamentRules.OfficialIds(a).Intersect(TournamentRules.OfficialIds(b)).Any();

    private static int EventRest(SetupData data,TournamentMatch m)=>TournamentRules.Event(data,data.Programs.Single(p=>p.Id==m.ProgramId).EventId).RestMinutes;

    public static void ValidateOverride(SetupData data,TournamentMatch match)

    {

        var program=data.Programs.Single(p=>p.Id==match.ProgramId);var plan=TournamentRules.Event(data,program.EventId);

        TournamentRules.Need(match.Court>=1&&match.Court<=data.CourtCount&&EventOperations.Courts(data,plan).Contains(match.Court??0)&&match.Start.HasValue&&match.End.HasValue&&TournamentRules.InsideMatch(data,plan,match),"Court and time must fit the event courts and a playing session.");


        var ids=TournamentRules.OfficialIds(match);

        TournamentRules.Need(ids.Distinct().Count()==ids.Length&&ids.All(id=>plan.Officials.Any(o=>o.Id==id)),"Select different available officials for each match position.");

        var zone=SetupStore.Zone(data.TimeZone);TournamentRules.Need(!zone.IsInvalidTime(match.Start!.Value)&&!zone.IsAmbiguousTime(match.Start.Value),"Choose a time outside daylight-saving clock changes.");

        var dependency=EventOperations.PrerequisiteEnd(data,match);

        TournamentRules.Need(dependency==DateTime.MinValue||match.Start>=dependency.AddMinutes(plan.RestMinutes),"The next round must start after its prerequisite matches and player rest.");

        var players=TournamentRules.Players(data,match);

        foreach(var other in Matches(data).Where(m=>m.Id!=match.Id&&m.Status!="BY"&&m.Start.HasValue&&m.End.HasValue))

        {

            var shared=TournamentRules.Players(data,other).Overlaps(players);

            if(!TournamentRules.Overlap(match.Start.Value,match.End!.Value,other.Start!.Value.AddMinutes(shared?-plan.RestMinutes:0),Finish(other).AddMinutes(shared?plan.RestMinutes:0)))continue;

            TournamentRules.Need(other.Court!=match.Court&&!TournamentRules.OfficialIds(other).Intersect(ids).Any()&&!shared,"This override conflicts with a court, player/rest period or official assignment.");

        }

    }

}

