namespace LiveScore.Api;

public static class EventDates
{
    public static bool Editable(SetupData s,string eventId)=>!s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==eventId)).SelectMany(p=>p.Matches).Any(m=>FixtureResets.Slotted(m)||GamePresets.Locked(m))&&!s.Venue.Bindings.Any(b=>s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==eventId)).SelectMany(p=>p.Matches).Any(m=>m.Id==b.MatchId));

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
        TournamentRules.Need(Editable(s,request.EventId),"Reset Schedule before changing event dates. Started matches and results keep event dates locked.");
        ValidateRange(s,request.EventId,request.StartDate,request.EndDate);
        var updated=existing! with{StartDate=request.StartDate,EndDate=request.EndDate};
        s.Events[s.Events.FindIndex(e=>e.Id==request.EventId)]=updated;
        var errors=SetupStore.Validate(new SetupData{TimeZone=s.TimeZone,Events=[updated],Programs=s.Programs.Where(p=>p.EventId==request.EventId).ToList(),Participants=s.Participants.Where(p=>p.EventId==request.EventId).ToList(),Entries=s.Entries.Where(p=>p.EventId==request.EventId).ToList()});
        TournamentRules.Need(errors.Count==0,string.Join("\n",errors));
        var plan=s.EventPlans.SingleOrDefault(p=>p.EventId==request.EventId);
        if(plan is not null)
        {
            if(existing.StartDate!=request.StartDate||existing.EndDate!=request.EndDate){plan.DailySessions=[];plan.Sessions=[];plan.ScheduleDays=[];}
            TournamentRules.ValidateEvent(s,plan);
        }
    }
}

public sealed record EventDatesRequest(long Revision,string EventId,DateOnly StartDate,DateOnly EndDate);
