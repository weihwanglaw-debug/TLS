namespace LiveScore.Api;

public static class EventDates
{
    public static void ValidateRange(SetupData s,string eventId,DateOnly start,DateOnly end)
    {
        TournamentRules.Need(start.Year>=1900&&end>=start,"End date must be on or after the start date (year 1900 or later).");
        var conflict=s.Events.FirstOrDefault(e=>e.Id!=eventId&&start<=e.EndDate&&end>=e.StartDate);
        if(conflict is not null)throw new RuleException($"These dates overlap {conflict.Name} ({conflict.StartDate:dd MMM yyyy} to {conflict.EndDate:dd MMM yyyy}). Only one event is allowed on each day.",409);
    }

    public static void Update(SetupData s,EventDatesRequest request)
    {
        var existing=s.Events.SingleOrDefault(e=>e.Id==request.EventId);
        TournamentRules.Need(existing is not null,"Event not found.");
        ValidateRange(s,request.EventId,request.StartDate,request.EndDate);
        var plan=s.EventPlans.SingleOrDefault(p=>p.EventId==request.EventId);
        bool Outside(DateOnly date)=>date<request.StartDate || date>request.EndDate;
        void RequireDate(DateOnly date,string item)
        {
            if(Outside(date))
                throw new RuleException($"{item} on {date:dd MMM yyyy} falls outside the proposed event dates.");
        }
        var programs=s.Programs.Where(p=>p.EventId==request.EventId).ToDictionary(p=>p.Id);
        var zone=SetupStore.Zone(s.TimeZone);
        foreach(var programPlan in s.ProgramPlans.Where(p=>programs.ContainsKey(p.ProgramId)))
        foreach(var match in programPlan.Matches)
        {
            var label=$"{programs[programPlan.ProgramId].Name} {match.RoundLabel} match {match.Number}";
            var actualStart=match.ActualStartedAt;
            if(actualStart is null && match.Live?.StartedAtUtc is {} utc)
                actualStart=TimeZoneInfo.ConvertTime(utc,zone).DateTime;
            foreach(var played in new[]{actualStart,match.ActualFinishedAt}.OfType<DateTime>())
                RequireDate(DateOnly.FromDateTime(played),$"Recorded play for {label}");
            foreach(var scheduled in new[]{match.Start,match.End}.OfType<DateTime>())
                RequireDate(DateOnly.FromDateTime(scheduled),label);
        }
        if(plan is not null)
        {
            if(plan.SchedulePlanner is {} planner)
                foreach(var booking in planner.Bookings)
                {
                    var first=DateOnly.FromDateTime(booking.Start);
                    var last=DateOnly.FromDateTime(booking.Start.AddMinutes(booking.Minutes));
                    if(!Outside(first) && !Outside(last)) continue;
                    var slot=SchedulePlanner.Slots(s,request.EventId).Find(n=>n.Id==booking.SlotId);
                    var label=slot is null ? $"Court {booking.Court} booking" :
                        $"{slot.ProgramName} {slot.RoundLabel} booking (court {booking.Court})";
                    RequireDate(Outside(first)?first:last,label);
                }
            foreach(var session in EventOperations.Sessions(s,plan))
                RequireDate(session.Date,$"Session at {session.Start:HH:mm}");
            foreach(var day in plan.ScheduleDays)
                RequireDate(day.Date,"Scheduled day");
        }
        var updated=existing! with{StartDate=request.StartDate,EndDate=request.EndDate};
        s.Events[s.Events.FindIndex(e=>e.Id==request.EventId)]=updated;
        var errors=SetupStore.Validate(new SetupData{TimeZone=s.TimeZone,Events=[updated],Programs=s.Programs.Where(p=>p.EventId==request.EventId).ToList(),Participants=s.Participants.Where(p=>p.EventId==request.EventId).ToList(),Entries=s.Entries.Where(p=>p.EventId==request.EventId).ToList()});
        TournamentRules.Need(errors.Count==0,string.Join("\n",errors));
        if(plan is not null) TournamentRules.ValidateEvent(s,plan);
    }
}

public sealed record EventDatesRequest(long Revision,string EventId,DateOnly StartDate,DateOnly EndDate);
