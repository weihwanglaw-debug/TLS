using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveScore.Api;

public sealed record ImportedIdentity(string EventId,string Kind,string SourceId,string LocalId);
public static class BusinessIds
{
    // Committed with the business write under SetupStore's SQL transaction/lock.
    public static string Next(SetupData s,string prefix,int width)
    {
        var value=checked(s.IdCounters.GetValueOrDefault(prefix)+1);
        s.IdCounters[prefix]=value;
        return prefix+value.ToString("D"+width,System.Globalization.CultureInfo.InvariantCulture);
    }
    public static string Fingerprint(SetupPackage p,string kind,string id)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{p.Source,p.SourceSystemId,p.Events[0].Id,kind,id}))));
    public static SetupData Import(SetupData s,SetupPackage p)
    {
        string Map(string kind,string sourceId,string eventId,string prefix,int width,string? parent=null)
        {
            var key=Fingerprint(p,parent is null?kind:kind+":"+parent,sourceId);
            if(s.ImportIdentities.TryGetValue(key,out var existing))return existing.LocalId;
            var id=Next(s,prefix,width);
            s.ImportIdentities.Add(key,new(kind=="event"?id:eventId,kind,sourceId,id));return id;
        }
        var ev=Map("event",p.Events[0].Id,"","EV",6);
        var programs=p.Programs.ToDictionary(x=>x.Id,x=>Map("program",x.Id,ev,ev+"-P",3));
        var people=p.Participants.ToDictionary(x=>x.Id,x=>Map("participant",x.Id,ev,ev+"-U",5));
        return new SetupData
        {
            TimeZone=s.TimeZone,
            Events=p.Events.Select(x=>x with{Id=ev}).ToList(),
            Programs=p.Programs.Select(x=>x with{Id=programs[x.Id],EventId=ev}).ToList(),
            Participants=p.Participants.Select(x=>x with{Id=people[x.Id],EventId=ev}).ToList(),
            Entries=p.Entries.Select(x=>x with{Id=Map("entry",x.Id,ev,programs[x.ProgramId]+"-E",4,x.ProgramId),EventId=ev,ProgramId=programs[x.ProgramId],ParticipantIds=x.ParticipantIds.Select(id=>people[id]).ToArray()}).ToList()
        };
    }
    public static void Officials(SetupData s,EventPlan plan)
    {
        var old=TournamentRules.Event(s,plan.EventId).Officials.Select(o=>o.Id).ToHashSet();
        TournamentRules.Need(plan.Officials is not null&&plan.Officials.All(o=>o is not null)&&plan.Officials.Select(o=>o.Id).Distinct().Count()==plan.Officials.Count,"Choose distinct officials.");
        plan.Officials=plan.Officials.Select(o=>old.Contains(o.Id)?o:o with{Id=Next(s,plan.EventId+"-O",3)}).ToList();
    }
}
