using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveScore.Api;

public sealed record PlannerBooking(string SlotId,DateTime Start,int Court,int Minutes,bool Manual=false);
public sealed record EarlierChange(PlannerBooking Before,PlannerBooking After);
public sealed record EarlierUnchanged(string SlotId,string Reason);
public sealed record EarlierSchedule(ScheduleProposal Plan,List<EarlierChange> Changes,List<EarlierUnchanged> Unchanged,List<PlannerFinding> Findings);
public sealed record ScheduleProposal(List<PlayingSession> Sessions,Dictionary<string,int> Minutes,int RestWarningMinutes,List<PlannerBooking> Placements,int StartStepMinutes=15);
public sealed class SchedulePlannerState
{
    public bool HasConfirmed {get;set;}
    public string[] Notices {get;set;}=[];
    public List<PlannerBooking> Bookings {get;set;}=[];
    public Dictionary<string,int> Minutes {get;set;}=[];
    public int RestWarningMinutes {get;set;}=30;
    public int StartStepMinutes {get;set;}=15;
    public Dictionary<string,string> DrawFingerprints {get;set;}=[];
    public Dictionary<string,List<EarlierChange>> WalkoverMoves {get;set;}=[];
}
public sealed record PlannerSlot(string Id,string ProgramId,string ProgramName,string RoundKey,string RoundLabel,string Phase,int Round,int Position,string? MatchId,string LabelA,string LabelB,string[] Sources,string[] Players,string[] PossiblePlayers,bool Final,bool Fixed,int Minutes)
{
    public string[] Dependencies {get;init;}=[];
    public string? GroupName {get;init;}
    public string Status {get;init;}=MatchStatus.Scheduled;
    public bool RequiresCourt=>Status!=MatchStatus.Walkover;
}
public sealed record PlannerFinding(string Kind,string Message,string[] SlotIds);
public sealed record PlannerRequest(long Revision,string EventId,ScheduleProposal Plan,string Action="check",string? SlotId=null,string? OtherSlotId=null,DateOnly? Date=null,TimeOnly? SessionStart=null);
public sealed record PlannerConfirm(long Revision,string Token,bool AcceptWarnings=false);

public static class SchedulePlanner
{
    public static string[] Unprepared(SetupData s,string eventId)=>s.Programs.Where(p=>p.EventId==eventId&&p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D"&&!s.ProgramPlans.Any(plan=>plan.ProgramId==p.Id&&plan.Matches.Count>0)).Select(p=>p.Name).ToArray();
    public static string Key(string program,string phase,int round,int position)=>$"{program}/{phase}/{round}/{position}";
    public static string RoundKey(string program,string phase,int round)=>$"{program}:{phase}:{round}";
    public static DateTime Grid(DateTime value,int step=15)=>new(value.Ticks+(TimeSpan.TicksPerMinute*step-value.Ticks%(TimeSpan.TicksPerMinute*step))%(TimeSpan.TicksPerMinute*step),DateTimeKind.Unspecified);
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
                var node=new PlannerSlot(Key(p.ProgramId,phase,round,pos),p.ProgramId,program.Name,key,label,phase,round,pos,m?.Id,a,b,sources,m is null||m.PendingA||m.PendingB?[]:People(s,[m.EntryA,m.EntryB]),possible,final,m is not null&&(FixtureResets.StartedOrScored(m)||s.Venue.Bindings.Any(b=>b.MatchId==m.Id)),duration){Status=m?.Status??MatchStatus.Scheduled,GroupName=phase=="G"?p.Groups.SingleOrDefault(g=>g.Id==m?.GroupId)?.Name:null};
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
                    if(m is not null){a=Name(s,m.EntryA);b=m.Status==MatchStatus.Bye?"Bye":Name(s,m.EntryB);possible=People(s,m.Status==MatchStatus.Walkover?[m.WinnerId]:[m.EntryA,m.EntryB]);}
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
        var ids=result.Where(x=>x.RequiresCourt).Select(x=>x.Id).ToHashSet();return result.Select(x=>x with{Sources=x.Sources.Where(ids.Contains).ToArray(),Dependencies=x.Phase=="G"?[]:result.Where(p=>p.RequiresCourt&&p.ProgramId==x.ProgramId&&(x.Round==1?p.Phase=="G":p.Phase=="K"&&p.Round==x.Round-1)).Select(p=>p.Id).ToArray()}).ToList();
    }
    public static List<PlannerBooking> Current(SetupData s,string eventId,List<PlannerSlot> nodes)
    {
        var saved=TournamentRules.Event(s,eventId).SchedulePlanner;var rows=new List<PlannerBooking>();
        foreach(var n in nodes.Where(n=>n.RequiresCourt)){var m=n.MatchId is null?null:LiveRuntime.Find(s,n.MatchId);if(m?.Start is {} start&&m.Court is {} court&&m.End.HasValue)rows.Add(new(n.Id,start,court,m.DurationMinutes>0?m.DurationMinutes:(int)(m.End.Value-start).TotalMinutes,saved?.Bookings.FirstOrDefault(b=>b.SlotId==n.Id)?.Manual??true));else if(m is null&&saved?.Bookings.FirstOrDefault(b=>b.SlotId==n.Id) is {} future)rows.Add(future);}
        return rows;
    }
    public static ScheduleProposal Configuration(SetupData s,string eventId,List<PlannerSlot> nodes)
    {
        var plan=TournamentRules.Event(s,eventId);var saved=plan.SchedulePlanner;return new(EventOperations.Sessions(s,plan).Select(w=>w with{Courts=w.Courts??EventOperations.Courts(s,plan),MatchIds=null,ProgramLimits=null}).ToList(),nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Minutes),saved?.RestWarningMinutes??30,Current(s,eventId,nodes),saved?.StartStepMinutes??15);
    }
    public static void ValidateConfiguration(SetupData s,string eventId,ScheduleProposal proposal,List<PlannerSlot> nodes)
    {
        TournamentRules.Need(proposal is not null,"Provide a schedule.");
        TournamentRules.Need(proposal.Sessions is {Count:>0 and <=1000}&&proposal.Minutes is not null&&proposal.Placements is {Count:<=10000},"Add sessions and round durations.");
        TournamentRules.Need(proposal.Sessions.All(w=>w is not null)&&proposal.Placements.All(b=>b is not null),"Sessions and placements cannot contain blank records.");
        TournamentRules.Need(proposal.Placements.All(b=>b.Minutes is >=1 and <=360&&b.Start.Year is >=1900 and <=9998),"Choose valid match dates and durations.");
        TournamentRules.Need(proposal.StartStepMinutes is 5 or 10 or 15,"Choose 5, 10 or 15-minute start-time steps.");
        var savedGrid=TournamentRules.Event(s,eventId).SchedulePlanner;TournamentRules.Need(savedGrid is null||Current(s,eventId,nodes).Count==0&&!nodes.Any(n=>n.Status==MatchStatus.Walkover)||proposal.StartStepMinutes==savedGrid.StartStepMinutes,"Reset schedule to edit start-time steps.");
        TournamentRules.Need(proposal.RestWarningMinutes is >=0 and <=240,"Rest warning must be 0–240 minutes.");
        var ev=s.Events.Single(x=>x.Id==eventId);var ordered=proposal.Sessions.OrderBy(w=>w.Date).ThenBy(w=>w.Start).ToList();
        foreach(var w in ordered){TournamentRules.Need(w.Date>=ev.StartDate&&w.Date<=ev.EndDate&&w.Start<w.End,"Sessions must fit event dates and end after they start.");TournamentRules.Need(w.Courts is {Length:>0}&&w.Courts.Distinct().Count()==w.Courts.Length&&w.Courts.All(c=>c>=1&&c<=s.CourtCount),"Select distinct venue courts.");}
        for(int i=1;i<ordered.Count;i++)TournamentRules.Need(ordered[i].Date!=ordered[i-1].Date||ordered[i].Start>=ordered[i-1].End,"Sessions cannot overlap, even on different courts.");
        foreach(var key in nodes.Select(n=>n.RoundKey).Distinct())TournamentRules.Need(proposal.Minutes.TryGetValue(key,out var duration)&&duration is >=1 and <=360,"Set each round's estimated duration to 1–360 minutes.");
        var current=Current(s,eventId,nodes);foreach(var n in nodes.Where(n=>current.Any(b=>b.SlotId==n.Id)||n.Status==MatchStatus.Walkover))TournamentRules.Need(proposal.Minutes[n.RoundKey]==(current.FirstOrDefault(b=>b.SlotId==n.Id)?.Minutes??n.Minutes),"Reset this round's schedule before changing its estimated duration.");
        TournamentRules.Need(proposal.Placements.Select(b=>b.SlotId).Distinct().Count()==proposal.Placements.Count&&proposal.Placements.All(b=>nodes.Any(n=>n.Id==b.SlotId&&n.RequiresCourt)),"The draw changed. Reload the schedule.");
    }
    private static DateTime End(PlannerBooking b)=>b.Start.AddMinutes(b.Minutes);
    public static ScheduleProposal Allocate(SetupData s,string eventId,ScheduleProposal proposal,List<PlannerSlot> nodes,string? single,List<PlayingSession>? onlySessions=null)
    {
        var map=nodes.ToDictionary(n=>n.Id);var original=proposal.Placements;var target=nodes.Where(n=>n.RequiresCourt&&!original.Any(b=>b.SlotId==n.Id)&&(single is null||n.Id==single)).ToList();var sessions=(onlySessions??proposal.Sessions).OrderBy(w=>w.Date).ThenBy(w=>w.Start).ToList();
        var errors=Check(s,eventId,proposal,nodes).Where(f=>f.Kind=="E").ToList();TournamentRules.Need(errors.Count==0,errors.FirstOrDefault()?.Message??"Review manual placements.");
        var outside=Outside(s,eventId);
        var officials=nodes.ToDictionary(n=>n.Id,n=>n.MatchId is null?Array.Empty<string>():TournamentRules.OfficialIds(LiveRuntime.Find(s,n.MatchId)));
        var programCounts=s.Entries.Where(e=>s.Programs.Any(p=>p.Id==e.ProgramId&&p.EventId==eventId&&p.Status=="A")).SelectMany(e=>e.ParticipantIds.Select(id=>(Person:id,e.ProgramId))).Distinct().GroupBy(x=>x.Person).ToDictionary(g=>g.Key,g=>g.Count());
        var priorities=nodes.ToDictionary(n=>n.Id,n=>n.Players.Sum(id=>Math.Max(0,programCounts.GetValueOrDefault(id)-1)));
        var watch=System.Diagnostics.Stopwatch.StartNew();
        for(int preference=single is null?0:2;preference<3;preference++)for(int order=0;order<2;order++)
        {
            var placed=original.ToDictionary(b=>b.SlotId);var remaining=target.ToList();var budget=0;var deadline=watch.ElapsedMilliseconds+(single is null?1300:4000);
            while(remaining.Count>0&&budget<2000000&&watch.ElapsedMilliseconds<deadline)
            {
                bool progress=false;var eligible=remaining.Where(n=>n.Dependencies.All(placed.ContainsKey)).OrderByDescending(n=>priorities[n.Id]).ThenByDescending(n=>nodes.Where(x=>x.ProgramId==n.ProgramId).Max(x=>x.Phase=="G"?0:x.Round)-(n.Phase=="G"?0:n.Round)).ThenBy(n=>order==0?n.ProgramId:Reverse(n.ProgramId)).ThenBy(n=>n.Position).ToList();
                foreach(var n in eligible)
                {
                    PlannerBooking? best=null;var dependency=n.Dependencies.Select(id=>End(placed[id])).DefaultIfEmpty(DateTime.MinValue).Max();
                    var windows=n.Final?sessions.OrderByDescending(w=>w.Date==proposal.Sessions.Max(w=>w.Date)).ThenBy(w=>w.Date).ThenBy(w=>w.Start):sessions.AsEnumerable();
                    foreach(var w in windows)
                    {
                        var start=w.Date.ToDateTime(w.Start);if(dependency>start)start=dependency;
                        for(var time=Grid(start,proposal.StartStepMinutes);time.AddMinutes(proposal.Minutes[n.RoundKey])<=w.Date.ToDateTime(w.End)&&best is null&&budget<2000000&&watch.ElapsedMilliseconds<deadline;time=time.AddMinutes(proposal.StartStepMinutes))
                        foreach(var court in (w.Courts??[]).Where(c=>!s.Venue.ClosedCourts.Contains(c)))
                        {
                            budget++;var booking=new PlannerBooking(n.Id,time,court,proposal.Minutes[n.RoundKey],single is not null);var end=End(booking);bool conflict=false;
                            foreach(var other in placed.Values)
                            {
                                var on=map[other.SlotId];var overlap=time<End(other)&&other.Start<end;var known=n.Players.Intersect(on.Players).Any();var possible=n.ProgramId!=on.ProgramId&&n.PossiblePlayers.Intersect(on.PossiblePlayers).Any();
                                if(overlap&&(court==other.Court||known||officials[n.Id].Intersect(officials[on.Id]).Any())){conflict=true;break;}
                                var gap=time>=End(other)?(time-End(other)).TotalMinutes:other.Start>=end?(other.Start-end).TotalMinutes:-1;
                                if(preference<2&&(known||n.Sources.Contains(on.Id))&&gap>=0&&gap<proposal.RestWarningMinutes||preference==0&&possible&&gap<proposal.RestWarningMinutes){conflict=true;break;}
                            }
                            if(conflict||outside.Any(m=>time<End(m.Booking)&&m.Booking.Start<end&&(m.Booking.Court==court||m.Players.Overlaps(n.Players)||m.Officials.Overlaps(officials[n.Id]))))continue;
                            best=booking;break;
                        }
                        if(best is not null)break;
                    }
                    if(best is null)continue;placed.Add(n.Id,best);remaining.Remove(n);progress=true;break;
                }
                if(!progress)break;
            }
            if(remaining.Count==0)return proposal with{Placements=placed.Values.ToList()};
        }
        throw new RuleException(single is null?"No complete allocation found. Extend sessions, add courts or adjust placements. Nothing changed.":$"No {proposal.Minutes[map[single!].RoundKey]}-minute slot available. Extend the session or choose another session.");
    }
    private static string Reverse(string value)=>string.Concat(value.Select(c=>(char)(char.MaxValue-c)));
    public static List<PlannerFinding> Check(SetupData s,string eventId,ScheduleProposal proposal,List<PlannerSlot> nodes,bool complete=false,HashSet<string>? affected=null)
    {
        ValidateConfiguration(s,eventId,proposal,nodes);var findings=new List<PlannerFinding>();var map=nodes.ToDictionary(n=>n.Id);var placed=proposal.Placements.ToDictionary(b=>b.SlotId);var current=Current(s,eventId,nodes).ToDictionary(b=>b.SlotId);
        var counts=new Dictionary<string,int>();
        void Add(string kind,string text,params string[] ids){if(affected is not null&&ids.Length>0&&!ids.Any(affected.Contains))return;var count=counts.GetValueOrDefault(kind);if(count<500){findings.Add(new(kind,text,ids));counts[kind]=count+1;}}
        if(complete&&nodes.Any(n=>n.RequiresCourt&&!placed.ContainsKey(n.Id)))Add("E",$"{nodes.Count(n=>n.RequiresCourt&&!placed.ContainsKey(n.Id))} matches not yet scheduled.");
        if(complete&&Unprepared(s,eventId).Length>0)Add("E","Generate opening draws before confirming the whole tournament: "+string.Join(", ",Unprepared(s,eventId)));
        foreach(var b in proposal.Placements)
        {
            var n=map[b.SlotId];var title=$"{n.ProgramName} · {n.RoundLabel} {n.Position+1}";
            var unchanged=current.TryGetValue(b.SlotId,out var old)&&old.Start==b.Start&&old.Court==b.Court&&old.Minutes==b.Minutes;
            if(s.Venue.ClosedCourts.Contains(b.Court))Add(unchanged?"W":"E",$"{title}: Court {b.Court} is closed. Move this match to an open court.",n.Id);
            if(b.Start.Kind!=DateTimeKind.Unspecified||!unchanged&&Grid(b.Start,proposal.StartStepMinutes)!=b.Start||b.Minutes!=proposal.Minutes[n.RoundKey])Add("E",$"{title}: use {proposal.StartStepMinutes}-minute start-time steps and the saved round duration.",n.Id);
            if(!proposal.Sessions.Any(w=>w.Date==DateOnly.FromDateTime(b.Start)&&b.Start>=w.Date.ToDateTime(w.Start)&&End(b)<=w.Date.ToDateTime(w.End)&&(w.Courts?.Contains(b.Court)??false)))Add("E",$"{title} does not fit a session and court.",n.Id);
            if(n.Dependencies.Any(id=>!placed.ContainsKey(id)||End(placed[id])>b.Start))Add("E",$"{title} must follow the complete preceding round.",[n.Id,..n.Dependencies.Where(id=>!placed.ContainsKey(id)||End(placed[id])>b.Start)]);
            foreach(var source in n.Sources){if(!placed.TryGetValue(source,out var before)){Add("E",$"Assign the earlier round before {title}.",n.Id);continue;}if(End(before)>b.Start)Add("E",$"{title} starts before its preceding match ends.",n.Id,source);else if((b.Start-End(before)).TotalMinutes<proposal.RestWarningMinutes){var speculative=map[source].Phase=="G"&&n.Players.Length==0;Add(speculative?"I":"W",$"{(speculative?"Possible group qualifier":"Advancing player")}: less than {proposal.RestWarningMinutes} minutes rest before {title}.",n.Id,source);}}
            var zone=SetupStore.Zone(s.TimeZone);if(zone.IsInvalidTime(b.Start)||zone.IsAmbiguousTime(b.Start))Add("E",$"{title}: choose a time outside daylight-saving clock changes.",n.Id);
        }
        var list=proposal.Placements;
        var matches=TournamentScheduler.Matches(s).ToDictionary(m=>m.Id);
        var officials=nodes.ToDictionary(n=>n.Id,n=>n.MatchId is null?new HashSet<string>():TournamentRules.OfficialIds(matches[n.MatchId]).ToHashSet());
        var players=nodes.ToDictionary(n=>n.Id,n=>n.Players.ToHashSet());var possiblePlayers=nodes.ToDictionary(n=>n.Id,n=>n.PossiblePlayers.ToHashSet());
        var names=s.Participants.ToDictionary(p=>p.Id,p=>p.Name);
        for(int i=0;i<list.Count;i++)for(int j=i+1;j<list.Count;j++)
        {
            var a=list[i];var b=list[j];var na=map[a.SlotId];var nb=map[b.SlotId];var overlaps=a.Start<End(b)&&b.Start<End(a);var known=players[na.Id].Where(players[nb.Id].Contains).ToArray();var possible=possiblePlayers[na.Id].Overlaps(possiblePlayers[nb.Id]);var gap=a.Start>=End(b)?a.Start-End(b):b.Start>=End(a)?b.Start-End(a):TimeSpan.MinValue;
            if(overlaps&&a.Court==b.Court)Add("E",$"{na.ProgramName} {na.RoundLabel} and {nb.ProgramName} {nb.RoundLabel} overlap on a court or for a known player/official.",na.Id,nb.Id);
            else if(overlaps&&(known.Length>0||officials[na.Id].Overlaps(officials[nb.Id])))Add("W",$"{na.ProgramName} {na.RoundLabel} and {nb.ProgramName} {nb.RoundLabel}: player or official overlap.",na.Id,nb.Id);
            else if(known.Length>0&&gap.TotalMinutes<proposal.RestWarningMinutes)Add("W",$"{string.Join(", ",known.Select(id=>names[id]))}: {Math.Max(0,(int)gap.TotalMinutes)} minutes between matches.",na.Id,nb.Id);
            else if(na.ProgramId!=nb.ProgramId&&possible&&(overlaps||gap.TotalMinutes<proposal.RestWarningMinutes))Add("I",$"Possible qualifier conflict: {na.ProgramName} {na.RoundLabel} / {nb.ProgramName} {nb.RoundLabel}. Recheck when players are known.",na.Id,nb.Id);
        }
        var outside=Outside(s,eventId);
        foreach(var b in list)foreach(var other in outside)if(b.Start<End(other.Booking)&&other.Booking.Start<End(b)&&(b.Court==other.Booking.Court||other.Players.Overlaps(map[b.SlotId].Players)||other.Officials.Overlaps(officials[b.SlotId])))Add("E","Another event already occupies this court, player or official.",b.SlotId);
        var finalDate=proposal.Sessions.Max(w=>w.Date);foreach(var b in list.Where(b=>map[b.SlotId].Final&&DateOnly.FromDateTime(b.Start)!=finalDate))Add("W",$"{map[b.SlotId].ProgramName} final is earlier than the final playing date.",b.SlotId);
        return findings.DistinctBy(f=>f.Kind+f.Message+string.Join('|',f.SlotIds)).ToList();
    }
    private sealed record Reservation(PlannerBooking Booking,HashSet<string> Players,HashSet<string> Officials);
    private static List<Reservation> Outside(SetupData s,string eventId)
    {
        var result=new List<Reservation>();var matches=TournamentScheduler.Matches(s).ToDictionary(m=>m.Id);var represented=new HashSet<string>();
        foreach(var ev in s.Events.Where(e=>e.Id!=eventId))
        {
            var nodes=Slots(s,ev.Id).ToDictionary(n=>n.Id);represented.UnionWith(nodes.Values.Where(n=>n.MatchId is not null).Select(n=>n.MatchId!));
            foreach(var b in Current(s,ev.Id,nodes.Values.ToList())){var n=nodes[b.SlotId];result.Add(new(b,n.Players.ToHashSet(),n.MatchId is null?[]:TournamentRules.OfficialIds(matches[n.MatchId]).ToHashSet()));}
        }
        foreach(var m in matches.Values.Where(m=>!represented.Contains(m.Id)&&m.Start.HasValue&&m.End.HasValue&&m.Status is not (MatchStatus.Bye or MatchStatus.Walkover)&&s.Programs.Any(p=>p.Id==m.ProgramId&&p.EventId!=eventId)))result.Add(new(new(m.Id,m.Start!.Value,m.Court??0,(int)(m.End!.Value-m.Start.Value).TotalMinutes),TournamentRules.Players(s,m),TournamentRules.OfficialIds(m).ToHashSet()));
        return result;
    }
    public static bool Confirmed(EventPlan plan)=>plan.SchedulePlanner is {} state&&(state.HasConfirmed||state.Bookings.Count>0);
    private static void Prune(SchedulePlannerState state,HashSet<string> ids,string reason,bool invalidateUndo=false)
    {
        var removed=state.Bookings.Where(b=>ids.Contains(b.SlotId)).Select(b=>b.SlotId).Distinct().ToArray();
        state.Bookings.RemoveAll(b=>ids.Contains(b.SlotId));
        if(invalidateUndo)foreach(var key in state.WalkoverMoves.Where(pair=>pair.Value.Any(move=>ids.Contains(move.Before.SlotId))).Select(pair=>pair.Key).ToList())state.WalkoverMoves.Remove(key);
        if(removed.Length>0)state.Notices=[$"{reason}: removed {removed.Length} placements. Sessions, estimates and unaffected placements were kept."];
    }
    public static void Synchronize(SetupData s)
    {
        foreach(var plan in s.EventPlans.Where(p=>p.SchedulePlanner is not null))
        {
            var state=plan.SchedulePlanner!;var programs=s.ProgramPlans.Where(p=>s.Programs.Any(x=>x.Id==p.ProgramId&&x.EventId==plan.EventId)).ToList();
            foreach(var key in state.WalkoverMoves.Keys.Where(id=>!programs.Any(p=>p.Matches.Any(m=>m.Id==id&&m.Status==MatchStatus.Walkover))).ToList())state.WalkoverMoves.Remove(key);
            foreach(var p in programs){var fingerprint=Fingerprint(p);if(state.DrawFingerprints.TryGetValue(p.ProgramId,out var old)&&old!=fingerprint){var removed=state.Bookings.Where(b=>b.SlotId.StartsWith(p.ProgramId+"/",StringComparison.Ordinal)).Select(b=>b.SlotId).ToHashSet();Prune(state,removed,"Draw changed for "+s.Programs.Single(x=>x.Id==p.ProgramId).Name,true);}state.DrawFingerprints[p.ProgramId]=fingerprint;}
            var nodes=Slots(s,plan.EventId);var released=nodes.Where(n=>n.Status==MatchStatus.Walkover).Select(n=>n.Id).ToHashSet();state.Bookings.RemoveAll(b=>released.Contains(b.SlotId));
            var valid=nodes.Where(n=>n.RequiresCourt).Select(n=>n.Id).ToHashSet();Prune(state,state.Bookings.Select(b=>b.SlotId).Where(id=>!valid.Contains(id)).ToHashSet(),"Draw slots removed");
            var allIds=nodes.Select(n=>n.Id).ToHashSet();foreach(var key in state.WalkoverMoves.Where(pair=>pair.Value.Any(move=>!allIds.Contains(move.Before.SlotId))).Select(pair=>pair.Key).ToList())state.WalkoverMoves.Remove(key);
            foreach(var b in state.Bookings){var n=nodes.Single(n=>n.Id==b.SlotId);if(n.MatchId is null)continue;var m=LiveRuntime.Find(s,n.MatchId);if(!FixtureResets.Slotted(m)&&m.Status==MatchStatus.Scheduled){m.Start=b.Start;m.End=End(b);m.Court=b.Court;m.DurationMinutes=b.Minutes;}}
        }
    }
    public static void RemoveRounds(SetupData s,EventPlan plan,string[] keys)
    {
        if(plan.SchedulePlanner is not {} state)return;var nodes=Slots(s,plan.EventId);var affected=nodes.Where(n=>keys.Any(k=>k==n.RoundKey||k.StartsWith(n.ProgramId+":G:",StringComparison.Ordinal)||k.StartsWith(n.ProgramId+":K:",StringComparison.Ordinal)&&int.TryParse(k.Split(':').Last(),out var r)&&n.Phase=="K"&&n.Round>=r)).Select(n=>n.Id).ToHashSet();Prune(state,affected,"Schedule reset — "+string.Join(", ",nodes.Where(n=>affected.Contains(n.Id)).Select(n=>n.ProgramName+" "+n.RoundLabel).Distinct()),true);
    }
    public static EarlierSchedule Earlier(SetupData s,string eventId,string matchId)
    {
        var ev=TournamentRules.Event(s,eventId);TournamentRules.Need(Confirmed(ev),"Save the schedule first.");
        var nodes=Slots(s,eventId);var source=nodes.SingleOrDefault(n=>n.MatchId==matchId&&n.Status==MatchStatus.Walkover);
        TournamentRules.Need(source is not null,"Choose an existing walkover in this event.");
        TournamentRules.Need(!ev.SchedulePlanner!.WalkoverMoves.ContainsKey(matchId),"Earlier times have already been applied for this walkover.");
        var match=LiveRuntime.Find(s,matchId);TournamentRules.Need(match.Start.HasValue&&match.End.HasValue&&match.Court.HasValue,"This walkover has no original court booking.");
        var proposal=Configuration(s,eventId,nodes);var court=match.Court!.Value;
        var session=proposal.Sessions.SingleOrDefault(w=>w.Date==DateOnly.FromDateTime(match.Start!.Value)&&match.Start>=w.Date.ToDateTime(w.Start)&&match.End<=w.Date.ToDateTime(w.End)&&w.Courts!.Contains(court));
        TournamentRules.Need(session is not null,"The walkover's original booking no longer fits a session.");
        var map=nodes.ToDictionary(n=>n.Id);var changes=new List<EarlierChange>();var unchanged=new List<EarlierUnchanged>();
        var rows=proposal.Placements.Where(b=>b.Court==court&&b.Start>=match.End&&b.Start<session!.Date.ToDateTime(session.End)&&DateOnly.FromDateTime(b.Start)==session.Date).OrderBy(b=>b.Start).ToList();
        var cursor=match.Start!.Value;var now=LiveRuntime.LocalNow(s);var stopped=false;
        foreach(var row in rows)
        {
            var n=map[row.SlotId];
            if(stopped||n.Fixed){stopped=true;unchanged.Add(new(n.Id,"A called, playing or completed match prevents moving this and later matches."));continue;}
            var placed=proposal.Placements.ToDictionary(b=>b.SlotId);
            if(n.Dependencies.Any(id=>!placed.ContainsKey(id))){unchanged.Add(new(n.Id,"An earlier round is not yet scheduled."));cursor=End(row);continue;}
            var earliest=new[]{cursor,now,session!.Date.ToDateTime(session.Start),n.Dependencies.Select(id=>End(placed[id])).DefaultIfEmpty(DateTime.MinValue).Max()}.Max();
            var start=Grid(earliest,proposal.StartStepMinutes);
            foreach(var occupied in proposal.Placements.Where(b=>b.Court==court&&b.SlotId!=row.SlotId).OrderBy(b=>b.Start))if(start<End(occupied)&&start.AddMinutes(row.Minutes)>occupied.Start)start=Grid(End(occupied),proposal.StartStepMinutes);
            if(start>=row.Start){unchanged.Add(new(n.Id,"Already at the earliest available time after current play and preceding rounds."));cursor=End(row);continue;}
            var next=row with{Start=start,Manual=true};var candidate=proposal with{Placements=proposal.Placements.Select(b=>b.SlotId==n.Id?next:b).ToList()};
            var error=Check(s,eventId,candidate,nodes,false,[n.Id]).FirstOrDefault(f=>f.Kind=="E");
            if(error is not null){unchanged.Add(new(n.Id,error.Message));cursor=End(row);continue;}
            proposal=candidate;changes.Add(new(row,next));cursor=End(next);
        }
        var ids=changes.Select(c=>c.Before.SlotId).ToHashSet();var findings=ids.Count==0?[]:Check(s,eventId,proposal,nodes,false,ids);
        return new(proposal,changes,unchanged,findings);
    }
    public static void ApplyBookings(SetupData s,string eventId,IEnumerable<PlannerBooking> bookings)
    {
        var state=TournamentRules.Event(s,eventId).SchedulePlanner!;var nodes=Slots(s,eventId).ToDictionary(n=>n.Id);
        foreach(var b in bookings)
        {
            var n=nodes[b.SlotId];if(n.MatchId is {} id){var m=LiveRuntime.Find(s,id);m.Start=b.Start;m.End=End(b);m.Court=b.Court;m.DurationMinutes=b.Minutes;}
            state.Bookings.RemoveAll(old=>old.SlotId==b.SlotId);state.Bookings.Add(b);
        }
    }
    public static HashSet<string> RestoreWalkoverTimes(SetupData s,string eventId,string matchId)
    {
        var state=TournamentRules.Event(s,eventId).SchedulePlanner;var ids=new HashSet<string>();
        if(state is null||!state.WalkoverMoves.TryGetValue(matchId,out var moves))return ids;
        var nodes=Slots(s,eventId).ToDictionary(n=>n.Id);var current=Current(s,eventId,nodes.Values.ToList()).ToDictionary(b=>b.SlotId);
        foreach(var move in moves)
        {
            var exists=nodes.TryGetValue(move.After.SlotId,out var n);var same=current.TryGetValue(move.After.SlotId,out var b)&&b.Start==move.After.Start&&b.Court==move.After.Court&&b.Minutes==move.After.Minutes;
            TournamentRules.Need(exists&&same&&!n!.Fixed,$"Cannot restore {n?.ProgramName??"a removed match"} {n?.RoundLabel}: its booking changed or it has been called, started or completed.");ids.Add(move.Before.SlotId);
        }
        ApplyBookings(s,eventId,moves.Select(m=>m.Before));state.WalkoverMoves.Remove(matchId);return ids;
    }
    public static void ValidateRestored(SetupData s,string eventId,HashSet<string> affected)
    {
        var nodes=Slots(s,eventId);var error=Check(s,eventId,Configuration(s,eventId,nodes),nodes,false,affected).FirstOrDefault(f=>f.Kind=="E");
        TournamentRules.Need(error is null,error?.Message??"The original times cannot be restored.");
    }
    public static string? CallProblem(SetupData s,TournamentMatch match)
    {
        var players=TournamentRules.Players(s,match);var officials=TournamentRules.OfficialIds(match).ToHashSet();
        foreach(var other in TournamentScheduler.Matches(s).Where(m=>m.Id!=match.Id&&(MatchStatus.Occupied(m.Status)||s.Venue.Bindings.Any(b=>b.MatchId==m.Id))))
        {
            var person=TournamentRules.Players(s,other).FirstOrDefault(players.Contains);
            if(person is not null)return $"{s.Participants.Single(p=>p.Id==person).Name} is playing or called on Court {other.Court}.";
            var official=TournamentRules.OfficialIds(other).FirstOrDefault(officials.Contains);
            if(official is not null){var name=s.EventPlans.SelectMany(p=>p.Officials).FirstOrDefault(o=>o.Id==official)?.Name??official;return $"{name} is officiating or called on Court {other.Court}.";}
        }
        return null;
    }
    public static void ValidateSwap(PlannerSlot first,PlannerSlot second)
    {
        TournamentRules.Need(first.Minutes==second.Minutes,"Swap matches with the same estimated duration.");
        TournamentRules.Need(first.ProgramId!=second.ProgramId||first.Phase==second.Phase&&first.Round==second.Round,"Swap matches in the same program only within the same stage and round.");
    }

    public static string[] QueueWarnings(SetupData s,TournamentMatch match)
    {
        var messages=RestWarnings(s,match).ToList();if(!match.Start.HasValue||!match.End.HasValue)return messages.ToArray();
        var players=TournamentRules.Players(s,match);var officials=TournamentRules.OfficialIds(match).ToHashSet();
        foreach(var other in TournamentScheduler.Matches(s).Where(m=>m.Id!=match.Id&&m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review))
        {
            if(!other.Start.HasValue||!other.End.HasValue||other.Start>=match.End||match.Start>=other.End)continue;
            var person=TournamentRules.Players(s,other).FirstOrDefault(players.Contains);
            if(person is not null)messages.Add($"Planned overlap: {s.Participants.Single(p=>p.Id==person).Name}, Court {other.Court} at {other.Start:HH:mm}.");
            var official=TournamentRules.OfficialIds(other).FirstOrDefault(officials.Contains);
            if(official is not null)messages.Add($"Planned official overlap: {s.EventPlans.SelectMany(p=>p.Officials).FirstOrDefault(o=>o.Id==official)?.Name??official}, Court {other.Court} at {other.Start:HH:mm}.");
        }
        return messages.Distinct().Take(20).ToArray();
    }
    public static string[] RestWarnings(SetupData s,TournamentMatch match)
    {
        var plan=TournamentRules.Event(s,s.Programs.Single(p=>p.Id==match.ProgramId).EventId);if(plan.SchedulePlanner is not {} planner||!match.Start.HasValue||planner.RestWarningMinutes==0)return [];
        var people=TournamentRules.Players(s,match);var start=match.Start.Value>LiveRuntime.LocalNow(s)?match.Start.Value:LiveRuntime.LocalNow(s);
        return TournamentScheduler.Matches(s).Where(m=>m.Id!=match.Id&&m.Status is not (MatchStatus.Bye or MatchStatus.Walkover)&&m.End.HasValue&&m.Start<match.Start&&TournamentRules.Players(s,m).Overlaps(people)).Select(m=>new{Match=m,Gap=(start-(m.ActualFinishedAt??m.End!.Value)).TotalMinutes}).Where(x=>x.Gap>=0&&x.Gap<planner.RestWarningMinutes).Select(x=>$"Rest warning: {Math.Floor(x.Gap)} minutes after match {x.Match.Number}; preferred gap {planner.RestWarningMinutes} minutes. Admin may keep these times.").Distinct().Take(10).ToArray();
    }
    public static void ValidateCurrentOverride(SetupData s,string eventId,TournamentMatch match)
    {
        var plan=TournamentRules.Event(s,eventId);if(plan.SchedulePlanner is null)return;TournamentRules.Need(!match.Court.HasValue||!s.Venue.ClosedCourts.Contains(match.Court.Value),"Choose an open venue court.");var nodes=Slots(s,eventId);var node=nodes.SingleOrDefault(n=>n.MatchId==match.Id);if(node is null)return;
        var proposal=new ScheduleProposal(EventOperations.Sessions(s,plan),nodes.GroupBy(n=>n.RoundKey).ToDictionary(g=>g.Key,g=>g.First().Minutes),plan.SchedulePlanner.RestWarningMinutes,Current(s,eventId,nodes),plan.SchedulePlanner.StartStepMinutes);
        var problem=Check(s,eventId,proposal,nodes,false,[node.Id]).FirstOrDefault(f=>f.Kind=="E"&&(f.SlotIds.Length==0||f.SlotIds.Contains(node.Id)));TournamentRules.Need(problem is null,problem?.Message??"");
    }
}
