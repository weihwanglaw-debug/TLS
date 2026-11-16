using LiveScore.Api;
public static class PlannerChecks
{
    public static int Run()
    {
        var checks=0;void Check(bool ok,string text){if(!ok)throw new Exception("Planner: "+text);checks++;}
        var first=new DateOnly(2030,6,1);var second=first.AddDays(1);
        SetupData Make(int programs=2,int entries=4)
        {
            var s=new SetupData{CourtCount=4,Events=[new("E","Planner","Hall",first,second)],EventPlans=[new(){EventId="E",MatchMinutes=30}]};
            for(int i=0;i<entries;i++)s.Participants.Add(new("person"+i,"E","Player "+i,new(1990,1,1),"M","p@example.com","12345678"));
            for(int p=0;p<programs;p++)
            {
                var id="P"+p;s.Programs.Add(new(id,"E",id,"Badminton","S",1,1,0,100,"O",1,100,"E"));
                for(int i=0;i<entries;i++)s.Entries.Add(new(id+"E"+i,"E",id,["person"+i],Seed:i+1));
                var plan=new ProgramPlan{ProgramId=id,Format="KO",SeedsReviewed=true,Seeds=s.Entries.Where(e=>e.ProgramId==id).ToDictionary(e=>e.Id,e=>e.Seed)};s.ProgramPlans.Add(plan);var matches=TournamentRules.CreateRound(s,plan);for(int i=0;i<matches.Count;i++)matches[i].Number=i+1;plan.Matches.AddRange(matches);
            }
            return s;
        }
        var s=Make();var nodes=SchedulePlanner.Slots(s,"E");Check(nodes.Count==6,"whole knockout tree before results");Check(nodes.Count(n=>n.MatchId is null)==2,"future finals are not fabricated matches");
        var draft=new PlannerDraft([new(first,new(14,0),new(18,0),[1,2]),new(second,new(14,0),new(18,0),[1,2])],nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Final?60:40),30,[]);
        var full=SchedulePlanner.Allocate(s,"E",draft,nodes,null);Check(full.Placements.Count==6,"complete allocation");Check(full.Placements.All(b=>b.Start.Minute%15==0),"15min grid");Check(!SchedulePlanner.Check(s,"E",full,nodes).Any(f=>f.Kind is "E" or "W" or "I"),"rest and possible participants honoured when space available");
        var off=full with{Placements=full.Placements.Select((b,i)=>i==0?b with{Start=b.Start.AddMinutes(5)}:b).ToList()};Check(SchedulePlanner.Check(s,"E",off,nodes).Any(f=>f.Kind=="E"),"off-grid placement invalid");
        var small=draft with{Sessions=[new(first,new(14,0),new(14,30),[1])],Placements=[]};try{SchedulePlanner.Allocate(s,"E",small,nodes,null);Check(false,"shortage accepted");}catch(RuleException){checks++;}Check(small.Placements.Count==0,"no-fit request unchanged");
        var root=nodes.First(n=>n.Sources.Length==0);var partial=draft with{Placements=[new(root.Id,first.ToDateTime(new(14,0)),1,40,true)]};var completed=SchedulePlanner.Allocate(s,"E",partial,nodes,null);Check(completed.Placements.Single(b=>b.SlotId==root.Id)==partial.Placements[0],"manual placement retained");
        Check(SchedulePlanner.Grid(first.ToDateTime(new(15,10)))==first.ToDateTime(new(15,15)),"round upward after40min+30rest");
        var staged=Make(1,8);var sn=SchedulePlanner.Slots(staged,"E");Check(sn.Where(n=>n.Round==2).All(n=>n.Dependencies.Length==4&&n.Sources.Length==2),"round order follows all preceding matches, rest follows feeding matches");
        var legacy=Make(1,2);var lm=legacy.ProgramPlans[0].Matches[0];lm.Start=first.ToDateTime(new(14,5));lm.End=lm.Start.Value.AddMinutes(30);lm.Court=1;lm.DurationMinutes=30;var legacyNodes=SchedulePlanner.Slots(legacy,"E");var legacyDraft=new PlannerDraft([new(first,new(14,0),new(18,0),[1])],legacyNodes.ToDictionary(n=>n.RoundKey,n=>30),0,SchedulePlanner.Current(legacy,"E",legacyNodes));Check(!SchedulePlanner.Check(legacy,"E",legacyDraft,legacyNodes).Any(f=>f.Kind=="E"),"unchanged legacy booking is not blocked by new grid");
        var changedLegacy=legacyDraft with{Placements=[legacyDraft.Placements[0] with{Start=first.ToDateTime(new(15,5))}]};Check(SchedulePlanner.Check(legacy,"E",changedLegacy,legacyNodes).Any(f=>f.Kind=="E"),"moving legacy booking requires grid");
        foreach(var count in new[]{2,3,5,6,9}){var uneven=Make(1,count);Check(SchedulePlanner.Slots(uneven,"E").Count==count-1,"byes consume no slots for "+count);}
        var group=Make(1,8);var gp=group.ProgramPlans[0];gp.Matches=[];gp.Phase="P";gp.Format="GK";gp.GroupCount=2;gp.AdvancePerGroup=2;var gm=TournamentRules.CreateRound(group,gp);for(int i=0;i<gm.Count;i++)gm[i].Number=i+1;gp.Matches=gm;var gn=SchedulePlanner.Slots(group,"E");Check(gn.Count==15&&gn.Count(n=>n.Phase=="G")==12,"two groups4 plus semifinal/final");Check(gn.Any(n=>n.LabelA.Contains("Group A winner")),"qualifier labels");
        foreach(var g in gp.Groups){var matches=gm.Where(m=>m.GroupId==g.Id).ToList();Check(matches.Count==6,"four-player group6matches");Check(g.EntryIds.All(id=>matches.Count(m=>m.EntryA==id||m.EntryB==id)==3),"each group entrant plays3matches");}
        gp.AdvancePerGroup=1;Check(SchedulePlanner.Slots(group,"E").Count==13,"one qualifier per group gives one final");
        var rr=Make(1,4);var rp=rr.ProgramPlans[0];rp.Matches=[];rp.Phase="P";rp.Format="RR";rp.Matches=TournamentRules.CreateRound(rr,rp);Check(SchedulePlanner.Slots(rr,"E").Count==6,"round robin has no knockout final");
        var tight=Make(2,2);var tn=SchedulePlanner.Slots(tight,"E");var td=new PlannerDraft([new(second,new(14,0),new(16,0),[1])],tn.ToDictionary(n=>n.RoundKey,n=>60),30,[]);var tf=SchedulePlanner.Allocate(tight,"E",td,tn,null);Check(tf.Placements.Count==2,"tight plan relaxes rest rather than reject complete fit");Check(SchedulePlanner.Check(tight,"E",tf,tn).Any(f=>f.Kind=="W"),"relaxed known rest is actionable warning");
        var speculative=full with{Placements=full.Placements.Select(b=>nodes.Single(n=>n.Id==b.SlotId).Final?b with{Start=second.ToDateTime(new(15,0)),Court=nodes.Single(n=>n.Id==b.SlotId).ProgramId=="P0"?1:2}:b).ToList()};var info=SchedulePlanner.Check(s,"E",speculative,nodes);Check(info.Any(f=>f.Kind=="I")&&!info.Any(f=>f.Kind is "E" or "W"),"possible-only overlap remains information without actionable warning");
        tight.EventPlans[0].SchedulePlanner=new();var t0=tight.ProgramPlans[0].Matches[0];var t1=tight.ProgramPlans[1].Matches[0];t0.Start=second.ToDateTime(new(14,0));t0.End=t0.Start.Value.AddHours(1);t0.Court=1;t1.Start=t0.Start;t1.End=t0.End;t1.Court=2;Check(SchedulePlanner.CallProblem(tight,t0)?.Contains("known player")==true,"known resolved player overlap blocks calling");t1.Status=MatchStatus.Playing;t1.Start=t0.Start.Value.AddHours(-2);t1.End=t0.Start.Value.AddHours(-1);Check(SchedulePlanner.CallProblem(tight,t0)?.Contains("active match")==true,"active player blocks a second call despite old planned finish");
        var large=Make(8,16);foreach(var entry in large.Entries.ToList()){var id=entry.ProgramId+entry.ParticipantIds[0];if(!large.Participants.Any(p=>p.Id==id))large.Participants.Add(new(id,"E",id,new(1990,1,1),"M","p@example.com","12345678"));large.Entries[large.Entries.IndexOf(entry)]=entry with{ParticipantIds=[id]};}
        var ln=SchedulePlanner.Slots(large,"E");var ld=new PlannerDraft([new(first,new(8,0),new(20,0),[1,2,3,4]),new(second,new(8,0),new(20,0),[1,2,3,4])],ln.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Final?60:30),30,[]);var watch=System.Diagnostics.Stopwatch.StartNew();var allocated=SchedulePlanner.Allocate(large,"E",ld,ln,null);watch.Stop();Check(allocated.Placements.Count==120,"120-match allocation");Check(!SchedulePlanner.Check(large,"E",allocated,ln).Any(f=>f.Kind=="E"),"120-match plan valid");Console.WriteLine($"120-match allocation: {watch.ElapsedMilliseconds} ms");
        Console.WriteLine($"PASS: {checks} whole-tournament planner rules checks");return checks;
    }
}
