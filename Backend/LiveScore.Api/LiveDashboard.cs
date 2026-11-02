using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LiveScore.Api;
public static class LiveDashboard
{
    private static DateTimeOffset? Fit(SetupData s,TournamentMatch m,DateTimeOffset earliest)
    {
        var eventId=s.Programs.Single(p=>p.Id==m.ProgramId).EventId;var plan=TournamentRules.Event(s,eventId);
        var local=TimeZoneInfo.ConvertTime(earliest,SetupStore.Zone(s.TimeZone)).DateTime;
        foreach(var session in EventOperations.Sessions(s,plan).OrderBy(x=>x.Date).ThenBy(x=>x.Start))
        {
            if(!m.Court.HasValue||!SessionCourts.Allows(session,m.Court.Value))continue;
            var start=session.Date.ToDateTime(session.Start);var end=session.Date.ToDateTime(session.End);var candidate=local>start?local:start;
            if(candidate.AddMinutes(m.DurationMinutes)<=end)return LiveRuntime.Utc(s,candidate);
        }
        return null;
    }
    private static object? EventContext(SetupData s,string? eventId,DateTimeOffset now)
    {
        if(string.IsNullOrEmpty(eventId))return null;
        var ev=s.Events.Single(e=>e.Id==eventId);var plan=TournamentRules.Event(s,eventId);
        var local=TimeZoneInfo.ConvertTime(now,SetupStore.Zone(s.TimeZone)).DateTime;var today=DateOnly.FromDateTime(local);
        var calendar=today<ev.StartDate?DashboardCalendar.Upcoming:today>ev.EndDate?DashboardCalendar.Over:DashboardCalendar.Ongoing;
        var sessions=EventOperations.Sessions(s,plan).OrderBy(x=>x.Date).ThenBy(x=>x.Start).ToList();
        var todaySessions=sessions.Where(x=>x.Date==today).ToList();
        var current=todaySessions.FirstOrDefault(x=>x.Date.ToDateTime(x.Start)<=local&&x.Date.ToDateTime(x.End)>local);
        var next=sessions.FirstOrDefault(x=>x.Date.ToDateTime(x.Start)>local);
        var sessionState=sessions.Count==0?DashboardSession.Missing:current is not null?DashboardSession.Playing:!todaySessions.Any()?DashboardSession.NoSession:local<todaySessions[0].Date.ToDateTime(todaySessions[0].Start)?DashboardSession.Before:next?.Date==today?DashboardSession.Break:DashboardSession.Finished;
        var programs=s.Programs.Where(p=>p.EventId==eventId&&p.Status=="A").ToList();
        var plans=programs.Select(p=>TournamentRules.Program(s,p.Id)).ToList();var matches=plans.SelectMany(p=>p.Matches).ToList();
        var courts=EventOperations.Courts(s,plan);
        return new{calendar,sessionState,day=calendar==DashboardCalendar.Ongoing?today.DayNumber-ev.StartDate.DayNumber+1:0,nextSession=next?.Date.ToDateTime(next.Start),sessionEnd=current?.Date.ToDateTime(current.End),
            completed=plans.Count>0&&plans.All(p=>p.Phase=="C"),unfinished=matches.Count(m=>m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review),awaitingConfirmation=matches.Count(m=>m.Status==MatchStatus.Review),paperMatches=matches.Count(m=>m.Live?.PaperMode==true&&!MatchStatus.Finished(m.Status)),missingFixtures=plans.Count(p=>p.Phase=="P"),
            missingCourts=courts.Length==0,allCourtsClosed=courts.Length>0&&courts.All(c=>s.Venue.ClosedCourts.Contains(c)),missingSessions=sessions.Count==0,missingOfficials=plan.Officials.Count==0,missingPresets=!s.Venue.Presets.Any(p=>p.Status=="A"),noPrograms=programs.Count==0};
    }
    public static object View(SetupData s,LiveRuntime runtime,HttpContext context,string? eventId)
    {
        if(!string.IsNullOrEmpty(eventId))TournamentRules.Need(s.Events.Any(e=>e.Id==eventId),"Choose an imported event.");
        var now=DateTimeOffset.UtcNow;var matches=TournamentScheduler.Matches(s).ToList();
        var available=Enumerable.Range(1,s.CourtCount).ToDictionary(c=>c,_=>now);
        var officials=new Dictionary<string,DateTimeOffset>();
        void Occupy(TournamentMatch m,DateTimeOffset end)
        {
            if(m.Court.HasValue&&available.ContainsKey(m.Court.Value)&&available[m.Court.Value]<end)available[m.Court.Value]=end;
            foreach(var id in TournamentRules.OfficialIds(m))if(!officials.TryGetValue(id,out var old)||old<end)officials[id]=end;
        }
        foreach(var m in matches.Where(m=>m.Status is MatchStatus.Playing or MatchStatus.Review))Occupy(m,(m.Status==MatchStatus.Review||MatchResults.Won(m)&&m.Live?.PaperMode!=true)?now.AddMinutes(1):LiveRuntime.Utc(s,TournamentScheduler.Finish(m)));
        var queue=new List<QueueItem>();
        foreach(var m in matches.Where(m=>m.Status==MatchStatus.Scheduled).OrderBy(m=>s.Venue.Bindings.Any(b=>b.MatchId==m.Id)?0:1).ThenBy(m=>m.Start).ThenBy(m=>m.Number))
        {
            var planned=m.Start.HasValue?LiveRuntime.Utc(s,m.Start.Value):(DateTimeOffset?)null;var estimate=planned.HasValue&&planned>now?planned.Value:now;string waiting="Ready to call";
            if(m.Court.HasValue&&available.TryGetValue(m.Court.Value,out var courtAt)&&courtAt>estimate){estimate=courtAt;waiting="Court occupied";}
            // Player availability and rest at calling time are the organiser's decision.
            foreach(var id in TournamentRules.OfficialIds(m))if(officials.TryGetValue(id,out var officialAt)&&officialAt>estimate){estimate=officialAt;waiting="Official occupied";}
            var fit=m.Court.HasValue&&!s.Venue.ClosedCourts.Contains(m.Court.Value)?Fit(s,m,estimate):null;
            var binding=s.Venue.Bindings.SingleOrDefault(b=>b.MatchId==m.Id);
            if(binding is not null)waiting=binding.TokenHash.Length==0?"Awaiting tablet pairing":"Awaiting umpire start";
            if(!fit.HasValue)waiting=m.Court.HasValue&&s.Venue.ClosedCourts.Contains(m.Court.Value)?"Court closed":"No remaining playing session";
            if(!m.Start.HasValue||!m.Court.HasValue){waiting="Scheduling required";fit=null;}
            if(m.PendingA||m.PendingB){waiting="Awaiting corrected earlier result";fit=null;}
            if(fit.HasValue)Occupy(m,fit.Value.AddMinutes(m.DurationMinutes));
            queue.Add(new(LiveRuntime.Row(s,m),fit,planned.HasValue&&planned<now,planned.HasValue?Math.Max(0,(int)(now-planned.Value).TotalMinutes):0,waiting));
        }
        bool Included(TournamentMatch m)=>string.IsNullOrEmpty(eventId)||s.Programs.Single(p=>p.Id==m.ProgramId).EventId==eventId;
        var eventCourts=string.IsNullOrEmpty(eventId)?Enumerable.Range(1,s.CourtCount).ToArray():EventOperations.Courts(s,TournamentRules.Event(s,eventId));
        var courts=Enumerable.Range(1,s.CourtCount).Where(c=>eventCourts.Contains(c)||matches.Any(m=>m.Court==c&&Included(m)&&(MatchStatus.Occupied(m.Status)||s.Venue.Bindings.Any(b=>b.MatchId==m.Id)))).Select(c=>
        {
            var active=matches.SingleOrDefault(m=>m.Court==c&&MatchStatus.Occupied(m.Status));var binding=s.Venue.Bindings.SingleOrDefault(b=>b.Court==c);
            var current=active??(binding is null?null:matches.SingleOrDefault(m=>m.Id==binding.MatchId));
            var outsideEvent=current is not null&&!Included(current);
            var otherEventName=outsideEvent?s.Events.Single(e=>e.Id==s.Programs.Single(p=>p.Id==current!.ProgramId).EventId).Name:null;
            var last=matches.Where(m=>m.Court==c&&Included(m)&&m.Status is MatchStatus.Completed or MatchStatus.Walkover).OrderByDescending(TournamentScheduler.Finish).FirstOrDefault();
            var status=s.Venue.ClosedCourts.Contains(c)?"X":current?.Status==MatchStatus.Review?"F":current?.Status==MatchStatus.Playing?(MatchResults.Won(current)&&current.Live?.PaperMode!=true?"S":"P"):current is not null?"W":"A";
            return new{number=c,status,outsideEvent,otherEventName,match=current is null||outsideEvent?null:LiveRuntime.Row(s,current),lastMatch=last is null?null:LiveRuntime.Row(s,last),next=queue.Where(q=>q.Match.Court==c&&q.Match.Id!=current?.Id&&q.Match.PlannedStart.HasValue&&q.Match.PlannedEnd.HasValue&&!matches.Single(m=>m.Id==q.Match.Id).PendingA&&!matches.Single(m=>m.Id==q.Match.Id).PendingB&&Included(matches.Single(m=>m.Id==q.Match.Id))).OrderBy(q=>q.Match.PlannedStart).ThenBy(q=>q.Match.Number).FirstOrDefault(),device=new{state=outsideEvent?"N":current?.Live?.PaperMode==true?"M":binding is null?"N":binding.TokenHash.Length==0?"W":runtime.Connected(binding)?"C":"D",name=outsideEvent?"":binding?.DeviceName??current?.Live?.DeviceName??"",ipAddress=outsideEvent?"":binding is null?current?.Live?.DeviceIp??"":runtime.Ip(binding),lastSeen=outsideEvent?null:runtime.LastSeen(binding),pairingPending=!outsideEvent&&binding is not null&&binding.CodeHash.Length>0,pairingExpiresAt=outsideEvent?null:binding?.CodeExpires,codeExpired=!outsideEvent&&binding is not null&&binding.CodeHash.Length>0&&binding.CodeExpires<now}};
        }).ToArray();
        var programRows=s.Programs.Where(p=>string.IsNullOrEmpty(eventId)||p.EventId==eventId).Select(p=>
        {
            var plan=TournamentRules.Program(s,p.Id);var round=plan.Phase=="G"?plan.Matches.Where(m=>m.Phase=="G").ToList():plan.Matches.Where(m=>m.Phase=="K"&&m.Round==plan.LastRound).ToList();
            var waiting=EventOperations.Waiting(s,plan);var confirmed=round.Count(m=>MatchStatus.Finished(m.Status));var ready=waiting is null&&plan.Phase is "G" or "K"&&round.Count>0&&confirmed==round.Count&&(plan.Phase=="G"||round.Count>1);
            return new{p.Id,p.EventId,p.Name,eventName=s.Events.Single(e=>e.Id==p.EventId).Name,phase=plan.Phase,roundLabel=round.FirstOrDefault()?.RoundLabel??"",total=round.Count,confirmed,remaining=round.Count-confirmed,waiting,awaitingConfirmation=round.Count(m=>m.Status==MatchStatus.Review),awaitingSubmission=round.Count(m=>m.Status==MatchStatus.Playing&&MatchResults.Won(m)&&m.Live?.PaperMode!=true),ready,canPrepare=plan.Phase=="P"&&p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D"};
        });
        var port=context.Request.Host.Port;var urls=new List<string>{$"{context.Request.Scheme}://{context.Request.Host}/umpire"};
        try{urls.AddRange(NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&LiveAccess.VenuePeer(a.Address)&&!System.Net.IPAddress.IsLoopback(a.Address)).Select(a=>$"{context.Request.Scheme}://{a.Address}{(port.HasValue?":"+port:"")}/umpire"));}catch(NetworkInformationException){}
        return new{selectedEventId=string.IsNullOrEmpty(eventId)?null:eventId,eventContext=EventContext(s,eventId,now),revision=s.Revision,serverTime=now,serverLocalTime=LiveRuntime.LocalNow(s),timeZone=s.TimeZone,displayTimeZone=TimeZoneInfo.TryConvertWindowsIdToIanaId(s.TimeZone,out var iana)?iana:s.TimeZone,heartbeatSeconds=runtime.HeartbeatSeconds,disconnectSeconds=runtime.DisconnectSeconds,courtCount=s.CourtCount,events=s.Events.Select(e=>new{e.Id,e.Name,e.StartDate,e.EndDate,courts=EventOperations.Courts(s,TournamentRules.Event(s,e.Id))}),courts,queue=queue.Where(q=>Included(matches.Single(m=>m.Id==q.Match.Id))).OrderBy(q=>q.Match.PlannedStart??DateTime.MaxValue).ThenBy(q=>q.Match.Number),programs=programRows,presets=s.Venue.Presets.Where(p=>p.Status=="A"),officials=s.EventPlans.SelectMany(p=>p.Officials.Select(o=>new{p.EventId,o.Id,o.Name,reservations=LiveRuntime.OfficialReservations(s,o.Id).Select(m=>new{matchId=m.Id,court=m.Court}).ToArray()})),tabletUrls=urls.Distinct().ToArray()};
    }
}
public sealed record QueueItem(LiveMatchView Match,DateTimeOffset? EstimatedStart,bool Delayed,int DelayMinutes,string WaitingFor);

public static class DashboardCalendar
{
    public const string Upcoming="U",Ongoing="O",Over="C";
}
public static class DashboardSession
{
    public const string Missing="N",Playing="I",NoSession="D",Before="B",Break="G",Finished="F";
}
