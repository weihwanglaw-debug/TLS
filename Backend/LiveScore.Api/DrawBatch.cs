using System.Text.Json;
namespace LiveScore.Api;

public sealed record ApplyDrawSettingsRequest(long Revision,string EventId,string SourceProgramId,string[] ProgramIds);
public sealed record DrawSettingsCandidate(string Id,string Name,string Reason);
public static class DrawBatch
{
    private static ProgramPlan Source(SetupData s,string eventId,string sourceId)
    {
        var program=s.Programs.SingleOrDefault(p=>p.Id==sourceId&&p.EventId==eventId);
        TournamentRules.Need(program is not null&&program.Status=="A"&&program.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&program.EntryType is "S" or "D","Choose a saved draw from an active badminton singles/doubles program.");
        var source=TournamentRules.Program(s,sourceId);
        TournamentRules.Need(source.Matches.Count>0&&source.Format is "KO" or "RR" or "GK","Save the source program's draw first.");return source;
    }
    private static ProgramPlan Prepare(SetupData s,ProgramPlan source,string id)
    {
        var plan=JsonSerializer.Deserialize<ProgramPlan>(JsonSerializer.Serialize(TournamentRules.Program(s,id),JsonDefaults.Options),JsonDefaults.Options)!;
        var entries=s.Entries.Where(e=>e.ProgramId==id&&e.Status=="A").ToList();
        TournamentRules.Need(entries.Count>=2,"At least two entries required");
        var ids=entries.Select(e=>e.Id).ToHashSet();var seeds=plan.Seeds.Values.Where(v=>v.HasValue).Select(v=>v!.Value).ToList();
        TournamentRules.Need(plan.Seeds.Keys.ToHashSet().SetEquals(ids)&&seeds.Distinct().Count()==seeds.Count&&seeds.All(v=>v>=1&&v<=entries.Count&&(plan.SeedCount<=0||v<=plan.SeedCount)),"Seeding needs review");
        var draft=new SetupData{Programs=s.Programs,Entries=s.Entries,ProgramPlans=[plan]};
        TournamentRules.ApplyDrawSettings(draft,new(0,s.Programs.Single(p=>p.Id==id).EventId,[id],source.GroupCount,source.AdvancePerGroup,source.Format));
        var count=entries.Count;
        if(source.Format=="RR")TournamentRules.Need((long)count*(count-1)/2<=1000,"Too many round-robin matches (maximum 1000)");
        if(source.Format=="GK")
        {
            var small=count/source.GroupCount;var large=count%source.GroupCount;
            TournamentRules.Need((long)large*(small+1)*small/2+(long)(source.GroupCount-large)*small*(small-1)/2<=1000,"Too many group matches (maximum 1000)");
        }
        if(source.Format=="KO"){var size=2;while(size<count)size*=2;TournamentRules.Need(size/2<=2000,"Draw exceeds 2000 matches");}
        plan.Phase="P";plan.LastRound=0;plan.Groups=[];return plan;
    }
    private static string Reason(SetupData s,ProgramPlan source,SetupProgram p)
    {
        if(p.Status!="A")return "Program inactive";
        if(!p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)||p.EntryType is not ("S" or "D"))return "Unsupported sport/format";
        var plan=TournamentRules.Program(s,p.Id);
        if(plan.Matches.Count>0)return "Draw already exists";
        if(!plan.SeedsReviewed)return "Seeding not reviewed";
        if(GamePresets.Default(s) is null)return "No active default preset";
        try{_=Prepare(s,source,p.Id);return "";}catch(RuleException ex){return "Incompatible settings: "+ex.Message;}
    }
    public static object List(SetupData s,string eventId,string sourceId)
    {
        var source=Source(s,eventId,sourceId);
        return new{revision=s.Revision,sourceProgramId=sourceId,source.Format,source.GroupCount,source.AdvancePerGroup,programs=s.Programs.Where(p=>p.EventId==eventId&&p.Id!=sourceId).Select(p=>new DrawSettingsCandidate(p.Id,p.Name,Reason(s,source,p))).ToArray()};
    }
    public static int Apply(SetupData s,ApplyDrawSettingsRequest request)
    {
        var source=Source(s,request.EventId,request.SourceProgramId);
        TournamentRules.Need(request.ProgramIds is {Length:>0}&&request.ProgramIds.Distinct().Count()==request.ProgramIds.Length,"Select distinct eligible programs.");
        var targets=new List<ProgramPlan>();
        foreach(var id in request.ProgramIds)
        {
            var p=s.Programs.SingleOrDefault(p=>p.Id==id&&p.EventId==request.EventId&&p.Id!=source.ProgramId);
            TournamentRules.Need(p is not null,"Select other programs within this event.");
            var reason=Reason(s,source,p);TournamentRules.Need(reason=="",p.Name+": "+reason);targets.Add(Prepare(s,source,id));
        }
        var next=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId==request.EventId)).Select(m=>m.Number).DefaultIfEmpty(0).Max()+1;var total=0;
        foreach(var plan in targets)
        {
            var matches=TournamentRules.CreateRound(s,plan);total+=matches.Count;
            TournamentRules.Need(total<=2000,"Select fewer programs (maximum 2000 new matches).");
            foreach(var m in matches)m.Number=next++;
            GamePresets.ApplyDefault(s,matches);plan.Matches.AddRange(matches);TournamentRules.Save(s.ProgramPlans,plan,p=>p.ProgramId);
        }
        var index=s.Imports.FindIndex(i=>i.EventId==request.EventId);TournamentRules.Need(index>=0,"Import metadata is missing.");s.Imports[index]=s.Imports[index] with{Stage="F"};return targets.Count;
    }
}
