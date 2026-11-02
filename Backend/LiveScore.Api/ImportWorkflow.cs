using System.Text.Json;
using TLS.Data;

namespace LiveScore.Api;

public static class ImportWorkflow
{
    private static string EventNameKey(string name) => System.Text.RegularExpressions.Regex.Replace(name.Trim(),@"\s+"," ").ToUpperInvariant();
    public static SetupData ValidatePackage(SetupPackage p,string zone)
    {
        if(p.SchemaVersion!=2)throw new RuleException("Use the current template or TRS JSON schema version 2.");
        if(p.Source is not ("EXCEL" or "TRS"))throw new RuleException("Invalid import source.");
        if(string.IsNullOrWhiteSpace(p.SourceSystemId)||p.SourceSystemId.Length>160)throw new RuleException("Source system ID is required (maximum 160 characters).");
        SetupStore.Zone(p.SourceTimeZone);
        if(p.ExportedAt==default||p.ExportedAt>DateTimeOffset.UtcNow.AddMinutes(5))throw new RuleException("A valid export timestamp is required.");
        if(p.Events is null||p.Programs is null||p.Participants is null||p.Entries is null||p.Events.Count!=1)throw new RuleException("Import exactly one complete event.");
        if(p.Events.Any(x=>x is null)||p.Programs.Any(x=>x is null)||p.Participants.Any(x=>x is null)||p.Entries.Any(x=>x is null||x.ParticipantIds is null))throw new RuleException("Records and participant ID lists cannot be null.");
        if(p.Programs.Count==0||p.Participants.Count==0||p.Entries.Count==0)throw new RuleException("Include programs, participants and program entries in the same file.");
        if(p.Source=="TRS")
        {
            if(p.HasFixtures is null||p.RegistrationClosed is null)throw new RuleException("TRS JSON must declare registrationClosed and hasFixtures.");
            if(p.HasFixtures.Value)throw new RuleException("Clear the existing fixtures in TRS before transferring this event to TLS. TLS cannot delete TRS fixtures or results.");
            if(!p.RegistrationClosed.Value)throw new RuleException("Close registration in TRS before transferring the event.");
            var sourceToday=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,SetupStore.Zone(p.SourceTimeZone)).DateTime);
            if(p.Events[0].EndDate<sourceToday)throw new RuleException("Past TRS events cannot be transferred.");
            if(p.Events[0].Status is "C" or "X")throw new RuleException("Completed or archived TRS events cannot be transferred.");
        }
        var data=new SetupData { TimeZone=zone,Events=p.Events,Programs=p.Programs,Participants=p.Participants,Entries=p.Entries };
        var errors=SetupStore.Validate(data);
        if(errors.Count>0)throw new RuleException(string.Join("\n",errors));
        if(p.Source=="TRS")
        {
            var active=p.Programs.Where(x=>x.Status=="A").ToList();
            if(active.Count==0)throw new RuleException("No active programs are available for import.");
            foreach(var program in active)
            {
                var entries=p.Entries.Where(e=>e.ProgramId==program.Id&&e.Status=="A").ToList();
                var count=program.CapacityUnit=="P"?entries.Sum(e=>e.ParticipantIds.Length):entries.Count;
                if(count<program.MinCapacity||(program.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&program.EntryType is "S" or "D"&&entries.Count<2))throw new RuleException($"{program.Name}: minimum participation must be met before transferring this event.");
            }
        }
        var eventId=p.Events[0].Id;
        if(p.Programs.Any(x=>x.EventId!=eventId)||p.Participants.Any(x=>x.EventId!=eventId)||p.Entries.Any(x=>x.EventId!=eventId))throw new RuleException("Every record must belong to the event in this file.");
        return data;
    }
    public static SetupData Check(SetupData current,SetupPackage package,string? replace)
    {
        ValidatePackage(package,current.TimeZone);var incoming=BusinessIds.Import(current,package);var id=incoming.Events[0].Id;
        var name=EventNameKey(incoming.Events[0].Name);
        if(current.Events.Any(x=>x.Id!=replace&&EventNameKey(x.Name)==name))throw new RuleException("An event with this name already exists in TLS.",409);
        var exists=current.Events.Any(x=>x.Id==id);
        if(replace is null && exists)throw new RuleException("An event with this name already exists in TLS.",409);
        if(replace is not null)
        {
            var metadata=current.Imports.SingleOrDefault(x=>x.EventId==replace);
            if(replace!=id||metadata is null)throw new RuleException("Replacement must use the same source system and event ID.",409);
            if(metadata.Stage!="P")throw new RuleException("Replacement is available only before play. Existing competition data is protected.",409);
        }
        EventDates.ValidateRange(current,replace??id,incoming.Events[0].StartDate,incoming.Events[0].EndDate);
        return incoming;
    }
    public static object[] Readiness(SetupData data) => data.Programs.Select(p=>
    {
        var entries=data.Entries.Where(e=>e.ProgramId==p.Id&&e.Status=="A").ToList();
        var count=p.CapacityUnit=="P"?entries.Sum(e=>e.ParticipantIds.Length):entries.Count;
        var supported=p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D";
        var reason=p.Status!="A"?"Program is withdrawn":!supported?"Format not supported for fixtures yet":count<p.MinCapacity?$"Minimum {p.MinCapacity} { (p.CapacityUnit=="P"?"players":"entries") } required":entries.Count<2?"At least two entries required":"Preparation ready";
        return (object)new {programId=p.Id,count,entryCount=entries.Count,ready=reason=="Preparation ready",reason};
    }).ToArray();
    public static SetupData Commit(SetupStore store,long revision,SetupPackage p,string? replace)
    {
        string id="";var now=DateTimeOffset.UtcNow;
        return store.Change(revision,replace is null?"IMPORT":"REPLACE_IMPORT",s=>
        {
            var data=Check(s,p,replace);id=data.Events[0].Id;
            var info=new ImportInfo(id,p.Source,p.SourceSystemId,p.Events[0].Id,p.SourceTimeZone,p.ExportedAt,now,"P");
            var oldProgramIds=s.Programs.Where(x=>x.EventId==id).Select(x=>x.Id).ToHashSet();
            s.ProgramPlans.RemoveAll(x=>oldProgramIds.Contains(x.ProgramId));s.EventPlans.RemoveAll(x=>x.EventId==id);
            s.Entries.RemoveAll(x=>x.EventId==id);s.Participants.RemoveAll(x=>x.EventId==id);s.Programs.RemoveAll(x=>x.EventId==id);s.Events.RemoveAll(x=>x.Id==id);
            s.Events.AddRange(data.Events);s.Programs.AddRange(data.Programs);s.Participants.AddRange(data.Participants);s.Entries.AddRange(data.Entries);
            s.Imports.RemoveAll(x=>x.EventId==id);s.Imports.Add(info);
        },db=>
        {
            var row=db.EventImports.SingleOrDefault(x=>x.EventId==id);
            if(row is null){row=new EventImport{EventId=id};db.EventImports.Add(row);}
            row.Source=p.Source;row.SourceSystemId=p.SourceSystemId;row.SourceEventId=p.Events[0].Id;row.SourceTimeZone=p.SourceTimeZone;
            row.ExportedAt=p.ExportedAt;row.ImportedAt=now;row.Stage="P";row.PackageJson=JsonSerializer.Serialize(p,JsonDefaults.Options);
        });
    }
}
