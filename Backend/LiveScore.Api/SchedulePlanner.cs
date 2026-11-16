using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveScore.Api;

public sealed record PlannerBooking(string SlotId,DateTime Start,int Court,int Minutes,bool Manual=false);
public sealed record PlannerDraft(List<PlayingSession> Sessions,Dictionary<string,int> Minutes,int RestWarningMinutes,List<PlannerBooking> Placements);
public sealed class SchedulePlannerState
{
    public PlannerDraft? Draft {get;set;}
    public List<PlannerBooking> Bookings {get;set;}=[];
    public Dictionary<string,int> Minutes {get;set;}=[];
    public int RestWarningMinutes {get;set;}=30;
    public Dictionary<string,string> DrawFingerprints {get;set;}=[];
}
public sealed record PlannerSlot(string Id,string ProgramId,string ProgramName,string RoundKey,string RoundLabel,string Phase,int Round,int Position,string? MatchId,string LabelA,string LabelB,string[] Sources,string[] Players,string[] PossiblePlayers,bool Final,bool Fixed,int Minutes)
{
    public string[] Dependencies {get;init;}=[];
    public string? GroupName {get;init;}
}
public sealed record PlannerFinding(string Kind,string Message,string[] SlotIds);
public sealed record PlannerRequest(long Revision,string EventId,PlannerDraft Draft,string Action="check",string? SlotId=null,string? OtherSlotId=null,DateOnly? Date=null,TimeOnly? SessionStart=null);
public sealed record PlannerConfirm(long Revision,string Token,bool AcceptWarnings=false);

public static class SchedulePlanner
{
    public static string[] Unprepared(SetupData s,string eventId)=>s.Programs.Where(p=>p.EventId==eventId&&p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D"&&!s.ProgramPlans.Any(plan=>plan.ProgramId==p.Id&&plan.Matches.Count>0)).Select(p=>p.Name).ToArray();
    public static string Key(string program,string phase,int round,int position)=>$"{program}/{phase}/{round}/{position}";
    public static string RoundKey(string program,string phase,int round)=>$"{program}:{phase}:{round}";
    public static DateTime Grid(DateTime value)=>new(value.Ticks+(TimeSpan.TicksPerMinute*15-value.Ticks%(TimeSpan.TicksPerMinute*15))%(TimeSpan.TicksPerMinute*15),DateTimeKind.Unspecified);
    private static string Fingerprint(ProgramPlan p)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{p.Format,p.GroupCount,p.AdvancePerGroup,p.Groups,first=p.Matches.Where(m=>p.Groups.Count>0?m.Phase=="G":m.Phase=="K"&&m.Round==1).Select(m=>new{m.Id,m.EntryA,m.EntryB,m.Phase,m.Round})},JsonDefaults.Options))));
    private static string Name(SetupData s,string? entry)=>s.Entries.SingleOrDefault(e=>e.Id==entry) is {} e?string.Join(" / ",e.ParticipantIds.Select(id=>s.Participants.Single(p=>p.Id==id).Name)):"To be confirmed";
    private static string[] People(SetupData s,IEnumerable<string?> entries)=>entries.Where(id=>id is not null).SelectMany(id=>s.Entries.SingleOrDefault(e=>e.Id==id)?.ParticipantIds??[]).Distinct().ToArray();
    public static List<PlannerSlot> Slots(SetupData s,string eventId)
    {
        var ev=TournamentRules.Event(s,eventId);var saved=ev.SchedulePlanner;var result=new List<PlannerSlot>();
        foreach(var p in s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==eventId&&x.Status=="A")&&p.Matches.Count>0))
        {
            var program=s.Programs.Single(x=>x.Id==p.ProgramId);var rows=new Dictionary<(string,int),List<PlannerSlot>>();
            PlannerSlot Make(string phase,int round,int pos,TournamentMatch? m,string a,string b,string[] sources,string[] possible,bool final,string label)
            {
                var key=RoundKey(p.ProgramId,phase,round);var booked=m is not null&&FixtureResets.Slotted(m);
                var duration=booked&&m!.DurationMinutes>0?m.DurationMinutes:saved is not null&&saved.Minutes.TryGetValue(key,out var estimate)?estimate:ev.MatchMinutes;
                var node=new PlannerSlot(Key(p.ProgramId,phase,round,pos),p.ProgramId,program.Name,key,label,phase,round,pos,m?.Id,a,b,sources,m is null||m.PendingA||m.PendingB?[]:People(s,[m.EntryA,m.EntryB]),possible,final,m is not null&&(FixtureResets.StartedOrScored(m)||s.Venue.Bindings.Any(b=>b.MatchId==m.Id)),duration){GroupName=phase=="G"?p.Groups.SingleOrDefault(g=>g.Id==m?.GroupId)?.Name:null};
                result.Add(node);return node;
            }
            var groupMatches=p.Matches.Where(m=>m.Phase=="G").OrderBy(m=>m.Number).ThenBy(m=>m.Id).ToList();
            if(groupMatches.Count>0){var groupRows=new List<PlannerSlot>();for(int i=0;i<groupMatches.Count;i++){var m=groupMatches[i];groupRows.Add(Make("G",1,i,m,Name(s,m.EntryA),Name(s,m.EntryB),[],People(s,[m.EntryA,m.EntryB]),false,p.Format=="RR"?"Round robin":"Group stage"));}rows[("G",1)]=groupRows;}
            if(p.Format=="RR")continue;
            var first=p.Matches.Where(m=>m.Phase=="K"&&m.Round==1).OrderBy(m=>m.Number).ThenBy(m=>m.Id).ToList();
            int count=first.Count;
            if(count==0){int qualifiers=p.Groups.Count*p.AdvancePerGroup;int size=2;while(size<qualifiers)size*=2;count=size/2;}
            if(count==0)continue;
            var roundNumber=1;var previous=new List<PlannerSlot>();
            while(count>0)
            {
                var actual=p.Matches.Where(m=>m.Phase=="K"&&m.Round==roundNumber).OrderBy(m=>m.Number).ThenBy(m=>m.Id).ToList();var current=new List<PlannerSlot>();
                for(int i=0;i<count;i++)
                {
                    var m=actual.ElementAtOrDefault(i);var sources=roundNumber==1?(rows.GetValueOrDefault(("G",1))??[]).Select(x=>x.Id).ToArray():previous.Skip(i*2).Take(2).Select(x=>x.Id).ToArray();
                    string a,b;string[] possible;
                    if(m is not null){a=Name(s,m.EntryA);b=m.Status==MatchStatus.Bye?"Bye":Name(s,m.EntryB);possible=People(s,[m.EntryA,m.EntryB]);}
                    else if(roundNumber==1){a=p.Groups.Count==2&&p.AdvancePerGroup==2?$"{p.Groups[i%2].Name} winner":$"Qualifier slot {i*2+1}";b=p.Groups.Count==2&&p.AdvancePerGroup==2?$"{p.Groups[1-i%2].Name} runner-up":$"Qualifier slot {i*2+2}";possible=People(s,p.Groups.SelectMany(g=>g.EntryIds));}
                    else{a=$"Winner of {previous[i*2].RoundLabel} {i*2+1}";b=$"Winner of {previous[i*2+1].RoundLabel} {i*2+2}";possible=previous.Skip(i*2).Take(2).SelectMany(x=>x.PossiblePlayers).Distinct().ToArray();}
                    var label=count==1?"Final":count==2?"Semi-finals":count==4?"Quarter-finals":$"Round of {count*2}";
                    // BYEs feed subsequent rounds but consume no court time.
                    var predictedBye=m is null&&roundNumber==1&&p.Groups.Count>0&&TournamentRules.SeedLine(count*2).Skip(i*2).Take(2).Any(seed=>seed>p.Groups.Count*p.AdvancePerGroup);
                    if(m?.Status==MatchStatus.Bye||predictedBye){current.Add(new(Key(p.ProgramId,"K",roundNumber,i),p.ProgramId,program.Name,RoundKey(p.ProgramId,"K",roundNumber),label,"K",roundNumber,i,m?.Id,a,b,[],m is null?[]:People(s,[m.EntryA]),possible,false,true,0));continue;}
                    current.Add(Make("K",roundNumber,i,m,a,b,sources,possible,count==1,label));
                }
                previous=current;rows[("K",roundNumber)]=current;if(count==1)break;count/=2;roundNumber++;
            }
        }
        // BYE source slots are already satisfied and are not scheduleable slots.
        var ids=result.Select(x=>x.Id).ToHashSet();return result.Select(x=>x with{Sources=x.Sources.Where(ids.Contains).ToArray(),Dependencies=x.Phase=="G"?[]:result.Where(p=>p.ProgramId==x.ProgramId&&(x.Round==1?p.Phase=="G":p.Phase=="K"&&p.Round==x.Round-1)).Select(p=>p.Id).ToArray()}).ToList();
    }
    public static List<PlannerBooking> Current(SetupData s,string eventId,List<PlannerSlot> nodes)
    {
        var saved=TournamentRules.Event(s,eventId).SchedulePlanner;var rows=new List<PlannerBooking>();
        foreach(var n in nodes){var m=n.MatchId is null?null:LiveRuntime.Find(s,n.MatchId);if(m?.Start is {} start&&m.Court is {} court&&m.End.HasValue)rows.Add(new(n.Id,start,court,m.DurationMinutes>0?m.DurationMinutes:(int)(m.End.Value-start).TotalMinutes,saved?.Bookings.FirstOrDefault(b=>b.SlotId==n.Id)?.Manual??true));else if(m is null&&saved?.Bookings.FirstOrDefault(b=>b.SlotId==n.Id) is {} future)rows.Add(future);}
        return rows;
    }
    public static PlannerDraft Draft(SetupData s,string eventId,List<PlannerSlot> nodes)
    {
        var plan=TournamentRules.Event(s,eventId);var saved=plan.SchedulePlanner;return saved?.Draft??new(EventOperations.Sessions(s,plan).Select(w=>w with{Courts=w.Courts??EventOperations.Courts(s,plan),MatchIds=null,ProgramLimits=null}).ToList(),nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Minutes),saved?.RestWarningMinutes??30,Current(s,eventId,nodes));
    }
    public static void ValidateConfiguration(SetupData s,string eventId,PlannerDraft draft,List<PlannerSlot> nodes)
    {
        TournamentRules.Need(draft is not null,"Provide a scheduling draft.");
        TournamentRules.Need(draft.Sessions is {Count:>0 and <=1000}&&draft.Minutes is not null&&draft.Placements is {Count:<=10000},"Add sessions and round durations.");
        TournamentRules.Need(draft.Sessions.All(w=>w is not null)&&draft.Placements.All(b=>b is not null),"Sessions and placements cannot contain blank records.");
        TournamentRules.Need(draft.Placements.All(b=>b.Minutes is >=1 and <=360&&b.Start.Year is >=1900 and <=9998),"Choose valid match dates and durations.");
        TournamentRules.Need(draft.RestWarningMinutes is >=0 and <=240,"Rest warning must be 0–240 minutes.");
        var ev=s.Events.Single(x=>x.Id==eventId);var ordered=draft.Sessions.OrderBy(w=>w.Date).ThenBy(w=>w.Start).ToList();
        foreach(var w in ordered){TournamentRules.Need(w.Date>=ev.StartDate&&w.Date<=ev.EndDate&&w.Start<w.End,"Sessions must fit event dates and end after they start.");TournamentRules.Need(w.Courts is {Length:>0}&&w.Courts.Distinct().Count()==w.Courts.Length&&w.Courts.All(c=>c>=1&&c<=s.CourtCount&&!s.Venue.ClosedCourts.Contains(c)),"Select distinct, open venue courts.");}
        for(int i=1;i<ordered.Count;i++)TournamentRules.Need(ordered[i].Date!=ordered[i-1].Date||ordered[i].Start>=ordered[i-1].End,"Sessions cannot overlap, even on different courts.");
        foreach(var key in nodes.Select(n=>n.RoundKey).Distinct())TournamentRules.Need(draft.Minutes.TryGetValue(key,out var duration)&&duration is >=1 and <=360,"Set each round's estimated duration to 1–360 minutes.");
        var current=Current(s,eventId,nodes);foreach(var n in nodes.Where(n=>current.Any(b=>b.SlotId==n.Id)))TournamentRules.Need(draft.Minutes[n.RoundKey]==current.First(b=>b.SlotId==n.Id).Minutes,"Reset this round's schedule before changing its estimated duration.");
        TournamentRules.Need(draft.Placements.Select(b=>b.SlotId).Distinct().Count()==draft.Placements.Count&&draft.Placements.All(b=>nodes.Any(n=>n.Id==b.SlotId)),"A selected draw changed. Refresh the planning draft.");
    }
    private static DateTime End(PlannerBooking b)=>b.Start.AddMinutes(b.Minutes);
    public static PlannerDraft Allocate(SetupData s,string eventId,PlannerDraft draft,List<PlannerSlot> nodes,string? single,List<PlayingSession>? onlySessions=null)
    {
        var map=nodes.ToDictionary(n=>n.Id);var original=draft.Placements;var target=nodes.Where(n=>!original.Any(b=>b.SlotId==n.Id)&&(single is null||n.Id==single)).ToList();var sessions=(onlySessions??draft.Sessions).OrderBy(w=>w.Date).ThenBy(w=>w.Start).ToList();
        var errors=Check(s,eventId,draft,nodes).Where(f=>f.Kind=="E").ToList();TournamentRules.Need(errors.Count==0,errors.FirstOrDefault()?.Message??"Review manual placements.");
        var outside=TournamentScheduler.Matches(s).Where(m=>m.Start.HasValue&&m.End.HasValue&&m.Status!=MatchStatus.Bye&&s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId!=eventId)).ToList();
        var officials=nodes.ToDictionary(n=>n.Id,n=>n.MatchId is null?Array.Empty<string>():TournamentRules.OfficialIds(LiveRuntime.Find(s,n.MatchId)));
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int preference=single is null?0:2;preference<3;preference++)for(int order=0;order<2;order++)
        {
            var placed=original.ToDictionary(b=>b.SlotId);var remaining=target.ToList();var budget=0;
            while(remaining.Count>0&&budget<2000000&&watch.ElapsedMilliseconds<8000)
            {
                bool progress=false;var eligible=remaining.Where(n=>n.Dependencies.All(placed.ContainsKey)).OrderByDescending(n=>nodes.Where(x=>x.ProgramId==n.ProgramId).Max(x=>x.Phase=="G"?0:x.Round)-(n.Phase=="G"?0:n.Round)).ThenBy(n=>order==0?n.ProgramId:Reverse(n.ProgramId)).ThenBy(n=>n.Position).ToList();
                foreach(var n in eligible)
                {
                    PlannerBooking? best=null;var dependency=n.Dependencies.Select(id=>End(placed[id])).DefaultIfEmpty(DateTime.MinValue).Max();
                    var windows=n.Final?sessions.OrderByDescending(w=>w.Date==draft.Sessions.Max(w=>w.Date)).ThenBy(w=>w.Date).ThenBy(w=>w.Start):sessions.AsEnumerable();
                    foreach(var w in windows)
                    {
                        var start=w.Date.ToDateTime(w.Start);if(dependency>start)start=dependency;
                        for(var time=Grid(start);time.AddMinutes(draft.Minutes[n.RoundKey])<=w.Date.ToDateTime(w.End)&&best is null&&budget<2000000&&watch.ElapsedMilliseconds<8000;time=time.AddMinutes(15))
                        foreach(var court in w.Courts??[])
                        {
                            budget++;var booking=new PlannerBooking(n.Id,time,court,draft.Minutes[n.RoundKey],single is not null);var end=End(booking);bool conflict=false;
                            foreach(var other in placed.Values)
                            {
                                var on=map[other.SlotId];var overlap=time<End(other)&&other.Start<end;var known=n.Players.Intersect(on.Players).Any();var possible=n.ProgramId!=on.ProgramId&&n.PossiblePlayers.Intersect(on.PossiblePlayers).Any();
                                if(overlap&&(court==other.Court||known||officials[n.Id].Intersect(officials[on.Id]).Any())){conflict=true;break;}
                                var gap=time>=End(other)?(time-End(other)).TotalMinutes:other.Start>=end?(other.Start-end).TotalMinutes:-1;
                                if(preference<2&&(known||n.Sources.Contains(on.Id))&&gap>=0&&gap<draft.RestWarningMinutes||preference==0&&possible&&gap<draft.RestWarningMinutes){conflict=true;break;}
                            }
                            if(conflict||outside.Any(m=>time<m.End&&m.Start<end&&(m.Court==court||TournamentRules.Players(s,m).Overlaps(n.Players)||TournamentRules.OfficialIds(m).Intersect(officials[n.Id]).Any())))continue;
                            best=booking;break;
                        }
                        if(best is not null)break;
                    }
                    if(best is null)continue;placed.Add(n.Id,best);remaining.Remove(n);progress=true;break;
                }
                if(!progress)break;
            }
            if(remaining.Count==0)return draft with{Placements=placed.Values.ToList()};
        }
        throw new RuleException(single is null?"No complete allocation found within the placement budget. Your draft is unchanged. Add session time/courts or adjust manual placements.":"This match cannot fit in that session after its earlier matches and current bookings.");
    }
    private static string Reverse(string value)=>string.Concat(value.Select(c=>(char)(char.MaxValue-c)));
    public static List<PlannerFinding> Check(SetupData s,string eventId,PlannerDraft draft,List<PlannerSlot> nodes,bool complete=false)
    {
        ValidateConfiguration(s,eventId,draft,nodes);var findings=new List<PlannerFinding>();var map=nodes.ToDictionary(n=>n.Id);var placed=draft.Placements.ToDictionary(b=>b.SlotId);var current=Current(s,eventId,nodes).ToDictionary(b=>b.SlotId);
        void Add(string kind,string text,params string[] ids){if(findings.Count<500)findings.Add(new(kind,text,ids));}
        if(complete&&nodes.Any(n=>!placed.ContainsKey(n.Id)))Add("E",$"{nodes.Count(n=>!placed.ContainsKey(n.Id))} matches still need a slot.");
        if(complete&&Unprepared(s,eventId).Length>0)Add("E","Generate opening draws before confirming the whole tournament: "+string.Join(", ",Unprepared(s,eventId)));
        foreach(var b in draft.Placements)
        {
            var n=map[b.SlotId];var title=$"{n.ProgramName} · {n.RoundLabel} {n.Position+1}";
            var unchanged=current.TryGetValue(b.SlotId,out var old)&&old.Start==b.Start&&old.Court==b.Court&&old.Minutes==b.Minutes;
            if(b.Start.Kind!=DateTimeKind.Unspecified||!unchanged&&Grid(b.Start)!=b.Start||b.Minutes!=draft.Minutes[n.RoundKey])Add("E",$"{title}: use a 15-minute start grid and the saved round duration.",n.Id);
            if(!draft.Sessions.Any(w=>w.Date==DateOnly.FromDateTime(b.Start)&&b.Start>=w.Date.ToDateTime(w.Start)&&End(b)<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(b.Court)??false)))Add("E",$"{title} does not fit a session and court.",n.Id);
            if(n.Dependencies.Any(id=>!placed.ContainsKey(id)||End(placed[id])>b.Start))Add("E",$"{title} must follow the complete preceding round.",n.Id);
            foreach(var source in n.Sources){if(!placed.TryGetValue(source,out var before)){Add("E",$"Assign the earlier round before {title}.",n.Id);continue;}if(End(before)>b.Start)Add("E",$"{title} starts before its preceding match ends.",n.Id,source);else if((b.Start-End(before)).TotalMinutes<draft.RestWarningMinutes){var speculative=map[source].Phase=="G"&&n.Players.Length==0;Add(speculative?"I":"W",$"{(speculative?"Possible group qualifier":"Advancing player")}: less than {draft.RestWarningMinutes} minutes rest before {title}.",n.Id,source);}}
            var zone=SetupStore.Zone(s.TimeZone);if(zone.IsInvalidTime(b.Start)||zone.IsAmbiguousTime(b.Start))Add("E",$"{title}: choose a time outside daylight-saving clock changes.",n.Id);
        }
        var list=draft.Placements;
        string[] Officials(PlannerSlot n)=>n.MatchId is null?[]:TournamentRules.OfficialIds(LiveRuntime.Find(s,n.MatchId));
        for(int i=0;i<list.Count;i++)for(int j=i+1;j<list.Count;j++)
        {
            var a=list[i];var b=list[j];var na=map[a.SlotId];var nb=map[b.SlotId];var overlaps=a.Start<End(b)&&b.Start<End(a);var known=na.Players.Intersect(nb.Players).ToArray();var possible=na.PossiblePlayers.Intersect(nb.PossiblePlayers).Any();var gap=a.Start>=End(b)?a.Start-End(b):b.Start>=End(a)?b.Start-End(a):TimeSpan.MinValue;
            if(overlaps&&(a.Court==b.Court||known.Length>0||Officials(na).Intersect(Officials(nb)).Any()))Add("E",$"{na.ProgramName} {na.RoundLabel} and {nb.ProgramName} {nb.RoundLabel} overlap on a court or for a known player/official.",na.Id,nb.Id);
            else if(known.Length>0&&gap.TotalMinutes<draft.RestWarningMinutes)Add("W",$"{string.Join(", ",known.Select(id=>s.Participants.Single(p=>p.Id==id).Name))}: {Math.Max(0,(int)gap.TotalMinutes)} minutes between matches.",na.Id,nb.Id);
            else if(na.ProgramId!=nb.ProgramId&&possible&&(overlaps||gap.TotalMinutes<draft.RestWarningMinutes))Add("I",$"Possible qualifier conflict: {na.ProgramName} {na.RoundLabel} / {nb.ProgramName} {nb.RoundLabel}. Recheck when players are known.",na.Id,nb.Id);
        }
        var outside=TournamentScheduler.Matches(s).Where(m=>s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId!=eventId)&&m.Start.HasValue&&m.End.HasValue&&m.Status!=MatchStatus.Bye);
        foreach(var b in list)foreach(var other in outside)if(b.Start<other.End&&other.Start<End(b)&&(b.Court==other.Court||TournamentRules.Players(s,other).Overlaps(map[b.SlotId].Players)))Add("E","Another event already occupies this court or player.",b.SlotId);
        var finalDate=draft.Sessions.Max(w=>w.Date);foreach(var b in list.Where(b=>map[b.SlotId].Final&&DateOnly.FromDateTime(b.Start)!=finalDate))Add("W",$"{map[b.SlotId].ProgramName} final is earlier than the final playing date.",b.SlotId);
        return findings.DistinctBy(f=>f.Kind+f.Message+string.Join('|',f.SlotIds)).ToList();
    }
    public static void Synchronize(SetupData s)
    {
        foreach(var plan in s.EventPlans.Where(p=>p.SchedulePlanner is not null))
        {
            var state=plan.SchedulePlanner!;var programs=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==plan.EventId)).ToList();
            foreach(var p in programs){var fingerprint=Fingerprint(p);if(state.DrawFingerprints.TryGetValue(p.ProgramId,out var old)&&old!=fingerprint){state.Bookings.RemoveAll(b=>b.SlotId.StartsWith(p.ProgramId+"/",StringComparison.Ordinal));state.Draft=null;}state.DrawFingerprints[p.ProgramId]=fingerprint;}
            var nodes=Slots(s,plan.EventId);var valid=nodes.Select(n=>n.Id).ToHashSet();state.Bookings.RemoveAll(b=>!valid.Contains(b.SlotId));
            foreach(var b in state.Bookings){var n=nodes.Single(n=>n.Id==b.SlotId);if(n.MatchId is null)continue;var m=LiveRuntime.Find(s,n.MatchId);if(!FixtureResets.Slotted(m)&&m.Status==MatchStatus.Scheduled){m.Start=b.Start;m.End=End(b);m.Court=b.Court;m.DurationMinutes=b.Minutes;}}
        }
    }
    public static void RemoveRounds(SetupData s,EventPlan plan,string[] keys)
    {
        if(plan.SchedulePlanner is not {} state)return;var nodes=Slots(s,plan.EventId);var affected=nodes.Where(n=>keys.Any(k=>k==n.RoundKey||k.StartsWith(n.ProgramId+":G:",StringComparison.Ordinal)||k.StartsWith(n.ProgramId+":K:",StringComparison.Ordinal)&&int.TryParse(k.Split(':').Last(),out var r)&&n.Phase=="K"&&n.Round>=r)).Select(n=>n.Id).ToHashSet();state.Bookings.RemoveAll(b=>affected.Contains(b.SlotId));state.Draft=null;
    }
    public static string? CallProblem(SetupData s,TournamentMatch match)
    {
        var eventId=s.Programs.Single(p=>p.Id==match.ProgramId).EventId;var plan=TournamentRules.Event(s,eventId);if(plan.SchedulePlanner is null||!match.Start.HasValue||!match.End.HasValue)return null;
        var players=TournamentRules.Players(s,match);
        var occupied=TournamentScheduler.Matches(s).FirstOrDefault(m=>m.Id!=match.Id&&m.Status==MatchStatus.Playing&&!MatchResults.Won(m)&&TournamentRules.Players(s,m).Overlaps(players));
        if(occupied is not null)return $"A player is still assigned to active match {occupied.Number}. Finish or release that match before calling this match.";
        var other=TournamentScheduler.Matches(s).FirstOrDefault(m=>m.Id!=match.Id&&(m.Status==MatchStatus.Scheduled||m.Status==MatchStatus.Playing&&!MatchResults.Won(m))&&m.Start.HasValue&&m.End.HasValue&&m.Start<match.End&&match.Start<m.End&&TournamentRules.Players(s,m).Overlaps(players));
        return other is null?null:$"A known player overlaps match {other.Number}. Adjust one match in Schedule before calling this match.";
    }
    public static string[] RestWarnings(SetupData s,TournamentMatch match)
    {
        var plan=TournamentRules.Event(s,s.Programs.Single(p=>p.Id==match.ProgramId).EventId);if(plan.SchedulePlanner is not {} planner||!match.Start.HasValue||planner.RestWarningMinutes==0)return [];
        var people=TournamentRules.Players(s,match);var start=match.Start.Value>LiveRuntime.LocalNow(s)?match.Start.Value:LiveRuntime.LocalNow(s);
        return TournamentScheduler.Matches(s).Where(m=>m.Id!=match.Id&&m.End.HasValue&&m.Start<match.Start&&TournamentRules.Players(s,m).Overlaps(people)).Select(m=>new{Match=m,Gap=(start-(m.ActualFinishedAt??m.End!.Value)).TotalMinutes}).Where(x=>x.Gap>=0&&x.Gap<planner.RestWarningMinutes).Select(x=>$"Rest warning: {Math.Floor(x.Gap)} minutes after match {x.Match.Number}; preferred gap {planner.RestWarningMinutes} minutes. Admin may keep these times.").Distinct().Take(10).ToArray();
    }
    public static void ValidateCurrentOverride(SetupData s,string eventId,TournamentMatch match)
    {
        var plan=TournamentRules.Event(s,eventId);if(plan.SchedulePlanner is null)return;var nodes=Slots(s,eventId);var node=nodes.SingleOrDefault(n=>n.MatchId==match.Id);if(node is null)return;
        var draft=new PlannerDraft(EventOperations.Sessions(s,plan),nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Minutes),plan.SchedulePlanner.RestWarningMinutes,Current(s,eventId,nodes));
        var problem=Check(s,eventId,draft,nodes).FirstOrDefault(f=>f.Kind=="E"&&(f.SlotIds.Length==0||f.SlotIds.Contains(node.Id)));TournamentRules.Need(problem is null,problem?.Message??"");
    }
}
