namespace LiveScore.Api;
public static class QualificationPlacement
{
    // Keep the seeded side and BYEs. Match the opposing slots across groups where possible.
    public static void Separate(List<TournamentMatch> matches,List<DrawGroup> groups)
    {
        var rows=matches.Where(m=>m.EntryB is not null).ToList();
        var opponents=rows.Select(m=>m.EntryB!).ToArray();var owners=Enumerable.Repeat(-1,rows.Count).ToArray();
        var groupOf=groups.SelectMany(g=>g.EntryIds.Select(id=>(id,g.Id))).ToDictionary(x=>x.id,x=>x.Id);
        bool Assign(int row,bool[] seen){for(int j=0;j<rows.Count;j++){if(seen[j]||groupOf[rows[row].EntryA]==groupOf[opponents[j]])continue;seen[j]=true;if(owners[j]<0||Assign(owners[j],seen)){owners[j]=row;return true;}}return false;}
        var assigned=new HashSet<int>();for(int i=0;i<rows.Count;i++)if(Assign(i,new bool[rows.Count]))assigned.Add(i);
        var unused=new Queue<string>(opponents.Where((_,j)=>owners[j]<0));
        for(int j=0;j<owners.Length;j++)if(owners[j]>=0)rows[owners[j]].EntryB=opponents[j];
        foreach(var i in Enumerable.Range(0,rows.Count).Where(i=>!assigned.Contains(i)))rows[i].EntryB=unused.Dequeue();
    }
}
