using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace LiveScore.Api;
public static class ResultDependencies
{
    public static void Link(ProgramPlan p)
    {
        foreach(var round in p.Matches.Where(m=>m.Phase=="K"&&m.Round>1).GroupBy(m=>m.Round)){
            var previous=p.Matches.Where(m=>m.Phase=="K"&&m.Round==round.Key-1).OrderBy(m=>m.Number).ToList();var rows=round.OrderBy(m=>m.Number).ToList();
            for(int i=0;i<rows.Count&&i*2+1<previous.Count;i++){rows[i].SourceMatchA??=previous[i*2].Id;rows[i].SourceMatchB??=previous[i*2+1].Id;}
        }
    }
    public static string Snapshot(ProgramPlan p){var node=JsonSerializer.SerializeToNode(p,JsonDefaults.Options)!;node.AsObject().Remove("officialDecisions");return JsonSerializer.Serialize(new{plan=node,live=p.Matches.Select(m=>new{m.Id,m.Live})},JsonDefaults.Options);}
    public static string Token(SetupData s,ProgramPlan p,ResultRequest r)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(p)+JsonSerializer.Serialize(new{r.MatchId,r.Games,r.Outcome,r.OfficialWinner,r.Reason,bindings=s.Venue.Bindings.Where(b=>p.Matches.Any(m=>m.Id==b.MatchId))},JsonDefaults.Options))));
    public static List<TournamentMatch> Affected(ProgramPlan p,TournamentMatch source,string winner)
    {
        if(source.Phase=="G")return p.Matches.Where(m=>m.Phase=="K").OrderBy(m=>m.Round).ThenBy(m=>m.Number).ToList();
        if(source.WinnerId==winner)return [];
        Link(p);var ids=new HashSet<string>{source.Id};var affected=new List<TournamentMatch>();
        foreach(var m in p.Matches.Where(m=>m.Phase=="K"&&m.Round>source.Round).OrderBy(m=>m.Round).ThenBy(m=>m.Number))if(ids.Contains(m.SourceMatchA??"")||ids.Contains(m.SourceMatchB??"")){ids.Add(m.Id);affected.Add(m);}
        return affected;
    }
    public static object Review(SetupData s,ResultRequest r)
    {
        SetupStore.CheckRevision(s,r.Revision);var p=s.ProgramPlans.Single(p=>p.Matches.Any(m=>m.Id==r.MatchId));var m=p.Matches.Single(m=>m.Id==r.MatchId);var winner=OfficialResults.Winner(m,r);Link(p);
        var affected=Affected(p,m,winner);return new{token=Token(s,p,r),dangerous=affected.Any(m=>m.Status is "IP" or "RC" or "C" or "WO"),matches=affected.Select(m=>new{m.Id,m.Number,m.EntryA,m.EntryB,m.Status}),message=affected.Count>0?"This result has affected later matches. Review the consequences before confirming.":"No downstream matches are affected."};
    }
    public static void Apply(SetupData s,ProgramPlan p,TournamentMatch source,ResultRequest r,List<TournamentMatch> affected)
    {
        if(affected.Count==0)return;
        TournamentRules.Need(r.DownstreamHandling is "U" or "K" or "R","Choose how to handle the affected matches.");
        var dangerous=affected.Any(m=>m.Status is "IP" or "RC" or "C" or "WO");
        TournamentRules.Need(!dangerous||r.DownstreamHandling is "K" or "R","Later matches have started or recorded results. Explicitly retain their outcomes or reset the listed matches for replay.");
        if(r.DownstreamHandling=="K"){foreach(var m in affected)m.DependencyOverride=true;return;}
        if(source.Phase=="G"){
            foreach(var g in p.Groups)TournamentRules.Need(BadmintonStandings.Pending(p,g).Length==0,"The corrected group is tied. Retain downstream outcomes, then resolve the tournament decision explicitly.");
            var origins=p.Groups.SelectMany((g,i)=>TournamentRules.Standings(p,g).Take(p.AdvancePerGroup).Select((row,j)=>new{row.EntryId,Origin=((char)('A'+i)).ToString()+(j+1)})).ToDictionary(x=>x.Origin,x=>x.EntryId);
            foreach(var m in affected.Where(m=>m.Round==1)){
                TournamentRules.Need(p.QualificationOrigins.ContainsKey(m.EntryA)&&(m.EntryB is null||p.QualificationOrigins.ContainsKey(m.EntryB)),"Qualifier origins are missing. Retain existing outcomes and review this older draw manually.");
                m.EntryA=origins[p.QualificationOrigins[m.EntryA]];if(m.EntryB is not null)m.EntryB=origins[p.QualificationOrigins[m.EntryB]];
            }
            p.QualificationOrigins=origins.ToDictionary(x=>x.Value,x=>x.Key);
        }
        foreach(var m in affected){s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);var version=(m.Live?.Version??0)+1;m.Live=new(){Version=version};m.WinnerId=m.EntryB is null?m.EntryA:null;m.Games=[];m.Status=m.EntryB is null?"BY":"SC";m.ActualFinishedAt=null;m.ResultKind="N";m.OfficialReason="";m.DependencyOverride=false;m.UmpireId=null;m.ServiceJudgeId=null;m.LineJudgeIds=[];}
        p.Phase="K";Refresh(p);
    }
    public static void Refresh(ProgramPlan p)
    {
        Link(p);foreach(var m in p.Matches.Where(m=>m.Phase=="K"&&m.Round>1&&m.Status is "SC" or "BY").OrderBy(m=>m.Round)){
            if(m.DependencyOverride)continue;
            var a=p.Matches.FirstOrDefault(x=>x.Id==m.SourceMatchA);var b=p.Matches.FirstOrDefault(x=>x.Id==m.SourceMatchB);
            m.PendingA=a is not null&&!MatchStatus.Finished(a.Status);m.PendingB=b is not null&&!MatchStatus.Finished(b.Status);
            if(a?.WinnerId is not null&&!m.PendingA)m.EntryA=a.WinnerId;if(b?.WinnerId is not null&&!m.PendingB)m.EntryB=b.WinnerId;
        }
    }
    public static void Ready(TournamentMatch m)=>TournamentRules.Need(!m.PendingA&&!m.PendingB,"This match is waiting for corrected earlier results. Complete those matches first.");
}
