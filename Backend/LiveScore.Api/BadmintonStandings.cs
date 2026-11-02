using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace LiveScore.Api;

public static class BadmintonStandings
{
    public static string Fingerprint(ProgramPlan plan,DrawGroup group)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan.Matches.Where(m=>m.GroupId==group.Id).Select(m=>new{m.Id,m.Status,m.WinnerId,m.Games}),JsonDefaults.Options))));
    public static List<List<Standing>> Order(ProgramPlan plan,DrawGroup group,List<Standing> rows,List<TournamentMatch> results)
    {
        List<List<Standing>> Rank(List<Standing> tied,int stage)
        {
            if(tied.Count<2)return [tied];
            if(tied.Count==2){var ids=tied.Select(r=>r.EntryId).ToHashSet();var h=results.FirstOrDefault(m=>ids.Contains(m.EntryA)&&m.EntryB is not null&&ids.Contains(m.EntryB));if(h?.WinnerId is not null)return tied.OrderBy(r=>r.EntryId==h.WinnerId?0:1).Select(r=>new List<Standing>{r}).ToList();}
            if(stage==2)return [tied];
            return tied.GroupBy(r=>stage==0?r.GamesFor-r.GamesAgainst:r.PointsFor-r.PointsAgainst).OrderByDescending(g=>g.Key).SelectMany(g=>Rank(g.ToList(),stage+1)).ToList();
        }
        var blocks=rows.GroupBy(r=>r.Wins).OrderByDescending(g=>g.Key).SelectMany(g=>Rank(g.ToList(),0)).ToList();
        var decision=plan.RankingDecisions.LastOrDefault(d=>d.GroupId==group.Id&&d.Fingerprint==Fingerprint(plan,group));
        if(decision is not null)return blocks.SelectMany(b=>b.OrderBy(r=>Array.IndexOf(decision.EntryIds,r.EntryId)).Select(r=>new List<Standing>{r})).ToList();
        return blocks;
    }
    public static string[][] Pending(ProgramPlan plan,DrawGroup group)
    {
        var results=plan.Matches.Where(m=>m.GroupId==group.Id).ToList();
        if(results.Count==0||results.Any(m=>!MatchStatus.Finished(m.Status)))return [];
        var rows=TournamentRules.Standings(plan,group);
        return Order(plan,group,rows,results).Where(b=>b.Count>1).Select(b=>b.Select(r=>r.EntryId).ToArray()).ToArray();
    }
    public static void Resolve(SetupData data,RankingDecisionRequest request)
    {
        var p=TournamentRules.Program(data,request.ProgramId);var g=p.Groups.SingleOrDefault(g=>g.Id==request.GroupId);TournamentRules.Need(g is not null,"Group not found.");
        TournamentRules.Need(p.Matches.Where(m=>m.Phase=="K").All(m=>m.DependencyOverride),"Review the result impact and explicitly retain the existing knockout draw before resolving this changed group order.");
        TournamentRules.Need(!string.IsNullOrWhiteSpace(request.Reason)&&request.Reason.Length<=300,"Enter a short reason for this ranking decision.");
        var pending=Pending(p,g!);TournamentRules.Need(pending.Length>0,"This group has no unresolved tie.");
        TournamentRules.Need(request.EntryIds is not null&&request.EntryIds.Length==g!.EntryIds.Length&&request.EntryIds.Distinct().ToHashSet().SetEquals(g.EntryIds),"Include every group competitor exactly once.");
        var before=TournamentRules.Standings(p,g!);for(int i=0;i<before.Count;i++)TournamentRules.Need(request.EntryIds![i]==before[i].EntryId||pending.Any(t=>t.Contains(before[i].EntryId)&&t.Contains(request.EntryIds[i])),"Only tied competitors can change ranking positions.");
        p.RankingDecisions.Add(new(g!.Id,Fingerprint(p,g),request.EntryIds!,request.Reason.Trim(),DateTimeOffset.UtcNow));
    }
}
