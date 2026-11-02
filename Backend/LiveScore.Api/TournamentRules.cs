using System.Security.Cryptography;

namespace LiveScore.Api;

public static class TournamentRules
{
    public static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message) {if(!ok)throw new RuleException(message);}
    public static EventPlan Event(SetupData data,string id)
    {
        Need(data.Events.Any(e=>e.Id==id),"Event not found.");
        return data.EventPlans.SingleOrDefault(e=>e.EventId==id)??new EventPlan{EventId=id};
    }
    public static ProgramPlan Program(SetupData data,string id)
    {
        Need(data.Programs.Any(p=>p.Id==id),"Program not found.");
        var existing=data.ProgramPlans.SingleOrDefault(p=>p.ProgramId==id);
        return existing??new ProgramPlan{ProgramId=id,Seeds=data.Entries.Where(e=>e.ProgramId==id&&e.Status=="A").ToDictionary(e=>e.Id,e=>e.Seed)};
    }
    public static void Save<T>(List<T> values,T value,Func<T,string> key)
    {var index=values.FindIndex(x=>key(x)==key(value));if(index<0)values.Add(value);else values[index]=value;}
    public static void ValidateRules(BadmintonRules r)
    {
        Need(r is not null,"Scoring rules are required.");
        Need(!string.IsNullOrWhiteSpace(r.Name)&&r.Name.Length<=100,"Name the scoring profile (maximum 100 characters).");
        Need(r.GamesToWin is >=1 and <=3&&r.PointsToWin is >=5 and <=50&&r.WinBy is >=1 and <=5&&r.PointCap>=r.PointsToWin&&r.PointCap<=99,"Invalid game, point, winning-margin or cap settings.");
        Need(r.IntervalAt>=1&&r.IntervalAt<r.PointsToWin&&r.DecidingEndsAt>=1&&r.DecidingEndsAt<r.PointsToWin,"Interval/end-change score must be below the winning score.");
        Need(r.IntervalSeconds is >=0 and <=600&&r.BetweenGamesSeconds is >=0 and <=600&&r.ChallengesPerGame is >=1 and <=5,"Invalid interval duration or challenge allowance.");
    }
    public static void ValidateEvent(SetupData data,EventPlan plan)
    {
        Need(plan is not null,"Event setup is required.");
        var ev=data.Events.SingleOrDefault(e=>e.Id==plan.EventId);Need(ev is not null,"Event not found.");
        Need(EventOperations.Sessions(data,plan).Count<=10000&&plan.Officials.Count<=200,"Maximum 10000 sessions and 200 officials.");
        Need(plan.MatchMinutes is >=1 and <=360&&plan.RestMinutes is >=0 and <=240,"Match duration must be 1–360 minutes; player rest 0–240 minutes.");
        Need(plan.Officials.All(o=>o is not null&&!string.IsNullOrWhiteSpace(o.Id)&&o.Id.Length<=160&&!string.IsNullOrWhiteSpace(o.Name)&&o.Name.Length<=100),"Official ID/name is missing or too long.");
        Need(plan.Officials.Select(o=>o.Id).Distinct().Count()==plan.Officials.Count&&plan.Officials.Select(o=>o.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()==plan.Officials.Count,"Official IDs and names must be unique.");
        Need(plan.Sessions.All(s=>s is not null),"Sessions cannot contain blank records.");
        Need(plan.Progression is "I" or "A","Choose Each program separately or All programs together.");

        var courts=EventOperations.Courts(data,plan);Need(courts.Length>0&&courts.Distinct().Count()==courts.Length&&courts.All(c=>c>=1&&c<=data.CourtCount),"Configure courts in Master Setting.");

        Need(plan.DailySessions.Count<=12,"Maximum 12 daily sessions.");
        foreach(var m in data.ProgramPlans.Where(p=>data.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==plan.EventId)).SelectMany(p=>p.Matches))Need(OfficialIds(m).All(id=>plan.Officials.Any(o=>o.Id==id)),"An official is assigned to a saved fixture. Keep the name or change the assignment first.");
        var ordered=EventOperations.Sessions(data,plan).OrderBy(s=>s.Date).ThenBy(s=>s.Start).ToList();
        var zone=SetupStore.Zone(data.TimeZone);
        for(var i=0;i<ordered.Count;i++)
        {
            var s=ordered[i];
            Need((s.Courts is null)==(s.MatchIds is null),"Each session needs both courts and program selections.");
            if(s.Courts is not null)
            {
                Need(s.Courts.Length>0&&s.Courts.Distinct().Count()==s.Courts.Length&&s.Courts.All(c=>courts.Contains(c)),"Select valid, distinct courts for each session.");
                Need(s.MatchIds!.Length<=10000&&s.MatchIds.Distinct().Count()==s.MatchIds.Length&&s.MatchIds.All(id=>!string.IsNullOrWhiteSpace(id)),"Select valid, distinct matches for each session.");
            }
            if(s.ProgramLimits is not null)Need(s.ProgramLimits.All(x=>x.Value>=0&&x.Value<=10000&&data.Programs.Any(p=>p.Id==x.Key&&p.EventId==plan.EventId)),"Choose valid session program quantities.");
            Need(s.Date>=ev!.StartDate&&s.Date<=ev.EndDate&&s.Start<s.End,"Sessions must be inside the event dates and end after they start.");
            Need(!zone.IsInvalidTime(s.Date.ToDateTime(s.Start))&&!zone.IsAmbiguousTime(s.Date.ToDateTime(s.Start))&&!zone.IsInvalidTime(s.Date.ToDateTime(s.End))&&!zone.IsAmbiguousTime(s.Date.ToDateTime(s.End)),"Choose session times outside daylight-saving clock changes.");
            if(i>0)Need(ordered[i-1].Date!=s.Date||ordered[i-1].End<=s.Start,"Playing sessions must not overlap.");
        }
        foreach(var match in data.ProgramPlans.Where(p=>data.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==plan.EventId)).SelectMany(p=>p.Matches).Where(m=>m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review))
        {
            if(match.Status==MatchStatus.Scheduled&&match.Start is null&&match.End is null&&match.Court is null)continue;
            Need(match.Start is not null&&match.End is not null&&InsideMatch(data,plan,match)&&courts.Contains(match.Court??0),"A session change would exclude a scheduled match. Adjust its schedule first.");
            Need(OfficialIds(match).All(id=>plan.Officials.Any(o=>o.Id==id)),"An official is still assigned to a scheduled match. Change the assignment before removing the name.");
        }
    }
    public static bool Inside(SetupData data,EventPlan plan,DateTime start,DateTime end)=>EventOperations.Sessions(data,plan).Any(s=>start>=s.Date.ToDateTime(s.Start)&&end<=s.Date.ToDateTime(s.End));
    public static bool SessionAllows(PlayingSession session,TournamentMatch match)=>
        (session.MatchIds is null||session.MatchIds.Contains(match.Id))&&SessionCourts.Allows(session,match.Court??0);
    public static bool InsideMatch(SetupData data,EventPlan plan,TournamentMatch match)=>match.Start.HasValue&&match.End.HasValue&&
        EventOperations.Sessions(data,plan).Any(s=>match.Start>=s.Date.ToDateTime(s.Start)&&match.End<=s.Date.ToDateTime(s.End)&&SessionAllows(s,match));
    public static string[] OfficialIds(TournamentMatch m)=>new[]{m.UmpireId,m.ServiceJudgeId}.Where(x=>x is not null).Select(x=>x!).Concat(m.LineJudgeIds).ToArray();
    public static HashSet<string> Players(SetupData data,TournamentMatch m)=>data.Entries.Where(e=>e.Id==m.EntryA||e.Id==m.EntryB).SelectMany(e=>e.ParticipantIds).ToHashSet();
    public static bool Overlap(DateTime a,DateTime b,DateTime c,DateTime d)=>a<d&&c<b;

    public static void ApplyDrawSettings(SetupData data,DrawSettingsRequest request)
    {
        Need(request.ProgramIds is {Length:>0}&&request.ProgramIds.Distinct().Count()==request.ProgramIds.Length,"Choose distinct programs.");
        foreach(var id in request.ProgramIds)
        {
            var p=data.Programs.SingleOrDefault(p=>p.Id==id&&p.EventId==request.EventId);
            Need(p is not null&&p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D","Scoring setup is available only for active badminton singles/doubles programs.");
            var plan=Program(data,id);Need(plan.Matches.Count==0,"Reset unplayed fixtures before changing program rules.");
            var count=data.Entries.Count(e=>e.ProgramId==id&&e.Status=="A");
            Need(request.Format is "KO" or "RR" or "GK","Choose Knockout, Round Robin or Group + Knockout. Heats cannot be used for badminton.");
            if(request.Format=="GK")Need(request.GroupCount>=1&&request.GroupCount<=26&&count/request.GroupCount>=2,"Every group must contain at least two entries (maximum 26 groups).");
            if(request.Format=="GK")Need(request.AdvancePerGroup>=1&&request.AdvancePerGroup<=count/request.GroupCount&&request.GroupCount*request.AdvancePerGroup>=2,"Qualifiers must fit the smallest group and produce at least two knockout entrants.");
            plan.Format=request.Format;plan.GroupCount=request.Format=="RR"?1:request.GroupCount;plan.AdvancePerGroup=request.AdvancePerGroup;
            Save(data.ProgramPlans,plan,x=>x.ProgramId);
        }
    }
    public static void SetSeeds(SetupData data,SeedsRequest request)
    {
        _=SeedingEndpoints.Program(data,request.ProgramId);
        var plan=Program(data,request.ProgramId);Need(plan.Matches.Count==0,"Seeding cannot change after fixture generation. Reset an unplayed draw first.");
        var ids=data.Entries.Where(e=>e.ProgramId==request.ProgramId&&e.Status=="A").Select(e=>e.Id).ToHashSet();
        Need(request.Seeds is not null&&request.Seeds.Keys.ToHashSet().SetEquals(ids),"Review every active entry, using blank for unseeded entries.");
        var values=request.Seeds.Values.Where(x=>x.HasValue).Select(x=>x!.Value).ToList();
        Need(values.All(x=>x>=1&&x<=ids.Count)&&values.Distinct().Count()==values.Count,"Seeds must be unique numbers between 1 and the entry count.");
        plan.Seeds=request.Seeds;plan.SeedCount=values.DefaultIfEmpty(0).Max();plan.SeedsReviewed=true;Save(data.ProgramPlans,plan,x=>x.ProgramId);
    }
    private static int[] SeedLine(int size)
    {
        if(size==1)return [1];if(size==2)return [1,2];if(size==4)return [1,4,3,2];
        return SeedLine(size/2).SelectMany(s=>new[]{s,size+1-s}).ToArray();
    }
    private static List<TournamentMatch> Knockout(string programId,List<(string Id,int Seed)> entrants,int round,BadmintonRules rules)
    {
        var size=1;while(size<entrants.Count)size*=2;
        var slots=new string?[size];var line=SeedLine(size);
        foreach(var item in entrants)slots[Array.IndexOf(line,item.Seed)]=item.Id;
        var matches=new List<TournamentMatch>();
        var label=size==2?"Final":size==4?"Semifinals":size==8?"Quarterfinals":$"Round of {size}";
        for(var i=0;i<size;i+=2)
        {
            if(slots[i] is null&&slots[i+1] is null)continue;
            var a=slots[i]??slots[i+1]!;var b=slots[i] is null?null:slots[i+1];
            matches.Add(new TournamentMatch{Id="",ProgramId=programId,EntryA=a,EntryB=b,Phase="K",Round=round,RoundLabel=label,Rules=rules,Status=b is null?"BY":"SC",WinnerId=b is null?a:null});
        }
        return matches;
    }
    public static List<TournamentMatch> CreateRound(SetupData data,ProgramPlan plan)
    {
        var scoring=plan.Rules??new BadmintonRules();Need(plan.SeedsReviewed,"Review and confirm the program's seeding first.");
        var created=new List<TournamentMatch>();
        if(plan.Phase=="P")
        {
            var entries=data.Entries.Where(e=>e.ProgramId==plan.ProgramId&&e.Status=="A").ToList();
            Need(entries.Count>=2,"At least two entries are needed.");
            if(plan.Format=="GK")Need(entries.Count/plan.GroupCount>=2&&plan.AdvancePerGroup<=entries.Count/plan.GroupCount,"Group sizes or qualifiers are no longer valid.");
            var unseeded=entries.Where(e=>!plan.Seeds[e.Id].HasValue).Select(e=>e.Id).ToArray();RandomNumberGenerator.Shuffle(unseeded.AsSpan());
            var sorted=entries.Where(e=>plan.Seeds[e.Id].HasValue).OrderBy(e=>plan.Seeds[e.Id]).Select(e=>e.Id).Concat(unseeded).ToList();
            if(plan.Format=="KO")
            {
                created=Knockout(plan.ProgramId,sorted.Select((id,i)=>(id,i+1)).ToList(),1,scoring);plan.Phase="K";plan.LastRound=1;foreach(var m in created)m.Id=BusinessIds.Next(data,plan.ProgramId+"-M",4);return created;
            }
            if(plan.Format=="RR")plan.GroupCount=1;
            var groups=Enumerable.Range(0,plan.GroupCount).Select(_=>new List<string>()).ToArray();
            for(var i=0;i<sorted.Count;i++){var groupIndex=i%(plan.GroupCount*2)<plan.GroupCount?i%plan.GroupCount:plan.GroupCount-1-i%plan.GroupCount;groups[groupIndex].Add(sorted[i]);}
            Need(groups.Sum(g=>g.Count*(g.Count-1)/2)<=1000,"Too many group matches; increase the group count (maximum 1000 generated matches per program).");
            plan.Groups=groups.Select((g,i)=>new DrawGroup(BusinessIds.Next(data,plan.ProgramId+"-G",3),"Group "+(char)('A'+i),g.ToArray())).ToList();
            foreach(var group in plan.Groups)for(var a=0;a<group.EntryIds.Length-1;a++)for(var b=a+1;b<group.EntryIds.Length;b++)
                created.Add(new TournamentMatch{Id="",ProgramId=plan.ProgramId,EntryA=group.EntryIds[a],EntryB=group.EntryIds[b],Phase="G",GroupId=group.Id,Round=1,RoundLabel=group.Name,Rules=scoring});
            plan.Phase="G";plan.LastRound=1;
        }
        else if(plan.Phase=="G")
        {
            Need(plan.Format!="RR","Round Robin ends with its final standings; there is no knockout stage.");
            Need(plan.Matches.Where(m=>m.Phase=="G").All(m=>m.Status is "C" or "WO"),"Complete and confirm every group result before generating knockout.");
            foreach(var g in plan.Groups)Need(!BadmintonStandings.Pending(plan,g).Any(t=>t.Any(id=>Standings(plan,g).Take(plan.AdvancePerGroup).Any(r=>r.EntryId==id))),"Resolve the tied qualifying positions before generating knockout.");
            var qualifiers=plan.Groups.Select(g=>Standings(plan,g).Take(plan.AdvancePerGroup).Select(s=>s.EntryId).ToList()).ToList();
            if(qualifiers.Count==2&&plan.AdvancePerGroup==2)
            {
                var label="Semifinals";
                for(var i=0;i<2;i++)created.Add(new TournamentMatch{Id="",ProgramId=plan.ProgramId,EntryA=qualifiers[0][i],EntryB=qualifiers[1][1-i],Phase="K",Round=1,RoundLabel=label,Rules=scoring});
            }
            else
            {
                var ordered=Enumerable.Range(0,plan.AdvancePerGroup).SelectMany(rank=>qualifiers.Select(g=>g[rank]).OrderBy(id=>plan.Seeds.GetValueOrDefault(id)??int.MaxValue).ThenBy(id=>id,StringComparer.Ordinal)).ToList();
                created=Knockout(plan.ProgramId,ordered.Select((id,i)=>(id,i+1)).ToList(),1,scoring);
            }
            plan.QualificationOrigins=qualifiers.SelectMany((entries,index)=>entries.Select((id,rank)=>new{Id=id,Origin=((char)('A'+index)).ToString()+(rank+1)})).ToDictionary(x=>x.Id,x=>x.Origin);
            QualificationPlacement.Separate(created,plan.Groups);
            plan.Phase="K";plan.LastRound=1;
        }
        else if(plan.Phase=="K")
        {
            var previous=plan.Matches.Where(m=>m.Phase=="K"&&m.Round==plan.LastRound).OrderBy(m=>m.Number).ToList();
            Need(previous.All(m=>m.Status is "C" or "WO" or "BY"),"Complete the current knockout round before advancing.");
            Need(previous.Count>1,"The final is already generated. Complete it to finish the program.");
            var label=previous.Count==2?"Final":previous.Count==4?"Semifinals":previous.Count==8?"Quarterfinals":$"Round of {previous.Count}";
            for(var i=0;i<previous.Count;i+=2)created.Add(new TournamentMatch{Id="",ProgramId=plan.ProgramId,EntryA=previous[i].WinnerId!,EntryB=previous[i+1].WinnerId,Phase="K",Round=plan.LastRound+1,RoundLabel=label,Rules=scoring});
            plan.LastRound++;
        }
        else throw new RuleException("Program is completed.");
        foreach(var m in created)m.Id=BusinessIds.Next(data,plan.ProgramId+"-M",4);
        return created;
    }
    public static List<Standing> Standings(ProgramPlan plan,DrawGroup group)
    {
        var rows=group.EntryIds.ToDictionary(id=>id,id=>new Standing(id,0,0,0,0,0,0,0));
        var results=plan.Matches.Where(m=>m.GroupId==group.Id&&m.Status is "C" or "WO").ToList();
        foreach(var m in results)
        {
            var a=rows[m.EntryA];var b=rows[m.EntryB!];
            var af=m.Games.Count(g=>g.A>g.B&&MatchResults.GameWon(g.A,g.B,m.Rules));var bf=m.Games.Count(g=>g.B>g.A&&MatchResults.GameWon(g.A,g.B,m.Rules));
            rows[a.EntryId]=a with{Played=a.Played+1,Wins=a.Wins+(m.WinnerId==a.EntryId?1:0),Points=a.Points+(m.WinnerId==a.EntryId?2:0),GamesFor=a.GamesFor+af,GamesAgainst=a.GamesAgainst+bf,PointsFor=a.PointsFor+m.Games.Sum(g=>g.A),PointsAgainst=a.PointsAgainst+m.Games.Sum(g=>g.B)};
            rows[b.EntryId]=b with{Played=b.Played+1,Wins=b.Wins+(m.WinnerId==b.EntryId?1:0),Points=b.Points+(m.WinnerId==b.EntryId?2:0),GamesFor=b.GamesFor+bf,GamesAgainst=b.GamesAgainst+af,PointsFor=b.PointsFor+m.Games.Sum(g=>g.B),PointsAgainst=b.PointsAgainst+m.Games.Sum(g=>g.A)};
        }
        return BadmintonStandings.Order(plan,group,rows.Values.ToList(),results).SelectMany(x=>x).ToList();
    }
}
