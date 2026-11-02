using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TLS.Data;

namespace LiveScore.Api;
public sealed class LiveRuntime(SetupStore store,IDbContextFactory<TLSDbContext> factory,IConfiguration config)
{
    private sealed record Contact(DateTimeOffset At,string Ip);
    private sealed record Attempt(DateTimeOffset At,int Count);
    private readonly ConcurrentDictionary<string,Contact> contacts=new();
    private readonly ConcurrentDictionary<string,Attempt> attempts=new();
    public int HeartbeatSeconds=>Math.Clamp(config.GetValue<int?>("LiveScore:HeartbeatSeconds")??5,1,30);
    public int DisconnectSeconds=>Math.Clamp(config.GetValue<int?>("LiveScore:DisconnectSeconds")??15,HeartbeatSeconds*2,180);
    public DateTimeOffset? LastSeen(TabletBinding? binding)=>binding is not null&&contacts.TryGetValue(binding.Id,out var c)?c.At:null;
    public string Ip(TabletBinding binding)=>contacts.TryGetValue(binding.Id,out var c)?c.Ip:binding.IpAddress;
    public bool Connected(TabletBinding binding)=>LastSeen(binding) is DateTimeOffset at&&DateTimeOffset.UtcNow-at<TimeSpan.FromSeconds(DisconnectSeconds);
    private void Touch(TabletBinding binding,HttpContext context)
    {
        contacts[binding.Id]=new(DateTimeOffset.UtcNow,LiveAccess.Peer(context));
        if(contacts.Count>512)foreach(var old in contacts.Where(x=>DateTimeOffset.UtcNow-x.Value.At>TimeSpan.FromHours(1)))contacts.TryRemove(old.Key,out _);
    }
    public static DateTime LocalNow(SetupData s)=>DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,SetupStore.Zone(s.TimeZone)).DateTime,DateTimeKind.Unspecified);
    internal static IEnumerable<TournamentMatch> OfficialReservations(SetupData s,string officialId)=>TournamentScheduler.Matches(s).Where(m=>(MatchStatus.Occupied(m.Status)||m.Status==MatchStatus.Scheduled&&s.Venue.Bindings.Any(b=>b.MatchId==m.Id))&&TournamentRules.OfficialIds(m).Contains(officialId));
    public static DateTimeOffset Utc(SetupData s,DateTime local)=>new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),SetupStore.Zone(s.TimeZone)));
    public static TournamentMatch Find(SetupData s,string id)=>TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==id)??throw new RuleException("Match not found.",404);
    public static TabletBinding Authenticate(SetupData s,string token)
    {
        var binding=s.Venue.Bindings.SingleOrDefault(b=>LiveAccess.Matches(b.TokenHash,token)&&b.TokenExpires>DateTimeOffset.UtcNow);
        if(binding is null)throw new RuleException("Tablet session is closed, expired or replaced. Ask the organiser for a new access code.",401);
        var match=Find(s,binding.MatchId);
        if(match.Court!=binding.Court||match.Status is not (MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review)||match.Live?.PaperMode==true)throw new RuleException("This tablet session is no longer active.",401);
        return binding;
    }
    public object Heartbeat(string token,HttpContext context)
    {
        using var db=factory.CreateDbContext();
        var json=db.SystemSettings.AsNoTracking().Where(x=>x.Id==1).Select(x=>x.LiveVenueJson).Single();
        var venue=JsonSerializer.Deserialize<LiveVenue>(json,JsonDefaults.Options)!;
        var binding=venue.Bindings.SingleOrDefault(b=>LiveAccess.Matches(b.TokenHash,token)&&b.TokenExpires>DateTimeOffset.UtcNow);
        if(binding is null)throw new RuleException("Tablet session is closed, expired or replaced.",401);
        Touch(binding,context);return new{serverTime=DateTimeOffset.UtcNow,sessionId=binding.Id,heartbeatSeconds=HeartbeatSeconds};
    }
    public object PairTablet(PairTabletRequest request,HttpContext context)
    {
        var peer=LiveAccess.Peer(context);var now=DateTimeOffset.UtcNow;
        if(attempts.Count>1024)foreach(var old in attempts.Where(x=>now-x.Value.At>TimeSpan.FromMinutes(1)))attempts.TryRemove(old.Key,out _);
        var attempt=attempts.AddOrUpdate(peer,new Attempt(now,1),(_,old)=>now-old.At>TimeSpan.FromMinutes(1)?new(now,1):old with{Count=old.Count+1});
        if(attempt.Count>10)throw new RuleException("Too many pairing attempts. Wait one minute before trying again.",429);
        TournamentRules.Need(request.Code is {Length:8}&&request.DeviceId is {Length:>=8 and <=128}&&!request.DeviceId.Any(char.IsControl)&&!string.IsNullOrWhiteSpace(request.DeviceName)&&request.DeviceName.Length<=60&&!request.DeviceName.Any(char.IsControl),"Enter an eight-character access code and a tablet name (maximum 60 characters).");
        var token=LiveAccess.Token();string id="";
        var s=store.ChangeRealtime("TABLET_PAIR",s=>
        {
            var binding=s.Venue.Bindings.SingleOrDefault(b=>b.CodeHash==LiveAccess.Hash(request.Code.ToUpperInvariant())&&b.CodeExpires>now);
            if(binding is null)throw new RuleException("Access code is invalid, expired or already used. Ask the organiser for a new code.",401);
            var m=Find(s,binding.MatchId);TournamentRules.Need(m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review,"The match is no longer available.");
            TournamentRules.Need(!s.Venue.Bindings.Any(b=>b.Id!=binding.Id&&b.DeviceId==request.DeviceId&&b.TokenExpires>now&&b.TokenHash.Length>0),"This tablet is already assigned to another match. Release its previous assignment first.");
            binding.CodeHash="";binding.TokenHash=LiveAccess.Hash(token);binding.TokenExpires=now.AddHours(24);binding.DeviceId=request.DeviceId;binding.DeviceName=request.DeviceName.Trim();binding.IpAddress=peer;id=binding.Id;
            m.Live??=new();m.Live.DeviceName=binding.DeviceName;m.Live.DeviceIp=peer;
            return new{matchId=m.Id,sessionId=id,deviceName=binding.DeviceName,ipAddress=peer};
        });
        Touch(s.Venue.Bindings.Single(b=>b.Id==id),context);
        return new{token,match=TabletView(s,token)};
    }
    public object PairCourt(PairCourtRequest request,HttpContext context)
    {
        TournamentRules.Need(request.Reason is not null,"Reason cannot be null.");
        var code=LiveAccess.Code();string bindingId="";
        var s=store.Change(request.Revision,"COURT_ASSIGN",s=>
        {
            var m=Find(s,request.MatchId);ResultDependencies.Ready(m);TournamentRules.Need(m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review,"Only queued, playing or submitted matches can be assigned.");
            TournamentRules.Need(m.Start.HasValue&&m.End.HasValue&&m.Court.HasValue,"Schedule the match before assigning a tablet.");
            TournamentRules.Need(request.Court>=1&&request.Court<=s.CourtCount&&!s.Venue.ClosedCourts.Contains(request.Court),"Choose an open venue court.");
            TournamentRules.Need(!MatchStatus.Occupied(m.Status)||request.Court==m.Court,"An in-progress match cannot be moved to another court.");
            var p=s.Programs.Single(p=>p.Id==m.ProgramId);var plan=TournamentRules.Event(s,p.EventId);
            TournamentRules.Need(p.Status=="A"&&p.Sport.Equals("Badminton",StringComparison.OrdinalIgnoreCase)&&p.EntryType is "S" or "D","Only active badminton singles/doubles can use the tablet.");
            
            TournamentRules.Need(plan.Officials.Any(o=>o.Id==request.UmpireId),"Choose an official from this event.");
            var old=s.Venue.Bindings.SingleOrDefault(b=>b.Court==request.Court&&b.MatchId!=m.Id);
            TournamentRules.Need(!TournamentScheduler.Matches(s).Any(other=>other.Id!=m.Id&&other.Court==request.Court&&MatchStatus.Occupied(other.Status)),"This court has a match in progress or awaiting result confirmation.");
            if(old is not null){var otherMatch=Find(s,old.MatchId);TournamentRules.Need(s.Programs.Single(x=>x.Id==otherMatch.ProgramId).EventId==p.EventId,"This court is assigned to another event. Release its waiting assignment from that event first.");}
            TournamentRules.Need(old is null,"This court already has a called match. Choose an available court or release its waiting assignment first.");
            TournamentRules.Need(request.Reason.Length<=300,"Reason must be at most 300 characters.");
            TournamentRules.Need(m.Status==MatchStatus.Scheduled||request.Reason.Trim().Length>0,"Provide the reason for replacing the tablet.");
            TournamentRules.Need(m.Live?.PaperMode!=true,"Finish and reconcile the paper result before issuing another tablet code.");
            TournamentRules.Need(!OfficialReservations(s,request.UmpireId).Any(other=>other.Id!=m.Id),"This umpire is assigned to another called or active match. Choose an available umpire.");
            TournamentRules.Need(EventOperations.Courts(s,plan).Contains(request.Court),"Choose a court selected in Event Details.");
            if(m.Status==MatchStatus.Scheduled)TournamentRules.Need(!string.IsNullOrWhiteSpace(request.PresetId),"Choose a game preset before generating the pairing code.");GamePresets.Assign(s,m,request.PresetId);
            if(MatchStatus.Occupied(m.Status))TournamentRules.Need(request.ReviewAvailable is null||request.ReviewAvailable==m.ReviewAvailable,"Review availability is locked after the match starts.");
            else m.ReviewAvailable=request.ReviewAvailable??(m.Court==request.Court&&m.ReviewAvailable);
            TournamentRules.Need(!m.Rules.ChallengesEnabled||m.ReviewAvailable,"Confirm review availability for this match assignment, or choose a preset without challenges.");
            SessionCourts.Authorize(s,plan,m,request.Court,request.CourtOverrideReason);
            var serviceJudge=request.ServiceJudgeId is null?m.ServiceJudgeId:string.IsNullOrWhiteSpace(request.ServiceJudgeId)?null:request.ServiceJudgeId;
            var lineJudges=request.LineJudgeIds??m.LineJudgeIds;
            if(MatchStatus.Occupied(m.Status))TournamentRules.Need(request.UmpireId==m.UmpireId&&serviceJudge==m.ServiceJudgeId&&lineJudges.Order().SequenceEqual(m.LineJudgeIds.Order()),"Officials are locked after the match starts. Tablet replacement keeps the assigned officials.");
            m.Court=request.Court;m.UmpireId=request.UmpireId;m.ServiceJudgeId=serviceJudge;m.LineJudgeIds=lineJudges;
            TournamentRules.Need(TournamentRules.OfficialIds(m).All(id=>plan.Officials.Any(o=>o.Id==id)),"Choose officials from this event.");
            foreach(var id in TournamentRules.OfficialIds(m))TournamentRules.Need(!OfficialReservations(s,id).Any(other=>other.Id!=m.Id),"An official is assigned to another called or active match. Choose available officials.");
            TournamentRules.Need(TournamentRules.OfficialIds(m).Distinct().Count()==TournamentRules.OfficialIds(m).Count(),"One official cannot hold multiple roles in a match.");
            var existing=s.Venue.Bindings.SingleOrDefault(b=>b.MatchId==m.Id&&b.Court==request.Court);
            s.Venue.Bindings.RemoveAll(b=>(b.MatchId==m.Id||b.Court==request.Court)&&b!=existing);
            existing??=new(){Id=Guid.NewGuid().ToString("N"),Court=request.Court,MatchId=m.Id};
            if(!s.Venue.Bindings.Contains(existing))s.Venue.Bindings.Add(existing);
            bindingId=existing.Id;existing.CodeHash=LiveAccess.Hash(code);existing.CodeExpires=DateTimeOffset.UtcNow.AddMinutes(3);
            m.Live??=new();m.Live.PaperMode=false;
        },auditView:s=>Audit(s,request.MatchId,request.Court,request.Reason));
        return new{revision=s.Revision,code,expiresAt=s.Venue.Bindings.Single(b=>b.Id==bindingId).CodeExpires,tabletUrl=$"{context.Request.Scheme}://{context.Request.Host}/umpire",match=Row(s,Find(s,request.MatchId))};
    }
    public object RenewPairing(RenewPairingRequest request,HttpContext context)
    {
        var code=LiveAccess.Code();
        var saved=store.Change(request.Revision,"TABLET_INVITATION_RENEW",s=>
        {
            var m=Find(s,request.MatchId);
            TournamentRules.Need((m.Status is MatchStatus.Scheduled or MatchStatus.Playing or MatchStatus.Review)&&m.Live?.PaperMode!=true,"This match cannot receive a tablet invitation.");
            if((m.Live?.Version??0)!=request.Version)throw new RuleException("The match changed. Refresh before generating a code.",409);
            var binding=s.Venue.Bindings.SingleOrDefault(b=>b.MatchId==m.Id);
            TournamentRules.Need(binding is not null&&binding.Court==m.Court&&!s.Venue.ClosedCourts.Contains(binding.Court),"Call the match before generating a tablet invitation.");
            TournamentRules.Need(!TournamentScheduler.Matches(s).Any(o=>o.Id!=m.Id&&o.Court==m.Court&&MatchStatus.Occupied(o.Status)),"This court is occupied by another match.");
            foreach(var id in TournamentRules.OfficialIds(m))TournamentRules.Need(!OfficialReservations(s,id).Any(o=>o.Id!=m.Id),"An official is assigned to another match.");
            binding!.CodeHash=LiveAccess.Hash(code);binding.CodeExpires=DateTimeOffset.UtcNow.AddMinutes(3);
        },auditView:s=>Audit(s,request.MatchId,null,"Renew tablet invitation"));
        return new{revision=saved.Revision,code,expiresAt=saved.Venue.Bindings.Single(b=>b.MatchId==request.MatchId).CodeExpires,tabletUrl=$"{context.Request.Scheme}://{context.Request.Host}/umpire",match=Row(saved,Find(saved,request.MatchId))};
    }
    public object TabletView(SetupData s,string token)
    {
        var binding=Authenticate(s,token);return new{serverTime=DateTimeOffset.UtcNow,heartbeatSeconds=HeartbeatSeconds,sessionId=binding.Id,match=Row(s,Find(s,binding.MatchId))};
    }
    public object Command(string token,TabletCommand request,HttpContext context)
    {
        TournamentRules.Need(request.OperationId!=Guid.Empty&&request.Action is "START" or "POINT" or "UNDO" or "NEXT" or "SUBMIT" or "SHUTTLE" or "CARD" or "CHALLENGE" or "SETUP" or "PAPER","Choose a valid tablet action with a unique operation ID.");
        var hash=LiveAccess.Hash(JsonSerializer.Serialize(request,JsonDefaults.Options));string commandMatchId="";
        var s=store.ChangeRealtime("TABLET_SCORE",s=>
        {
            var binding=Authenticate(s,token);var m=Find(s,binding.MatchId);commandMatchId=m.Id;var live=m.Live??=new();
            var receipt=live.Receipts.SingleOrDefault(r=>r.OperationId==request.OperationId);
            if(receipt is not null){if(receipt.Hash!=hash)throw new RuleException("An operation ID cannot be reused with different data.",409);return null;}
            if(live.Version!=request.Version)throw new RuleException("Score changed. Reload the confirmed score before entering another action.",409);
            binding.IpAddress=LiveAccess.Peer(context);live.DeviceIp=binding.IpAddress;
            var before=new{m.Status,live.A,live.B,live.Version,games=m.Games.ToArray(),live.AwaitingNextGame,serving=BadmintonServing.Copy(live.Serving),details=ResultValidation.Copy(live.Details)};
            if(request.Action=="START")
            {
                TournamentRules.Need(m.Status==MatchStatus.Scheduled&&live.StartedAtUtc is null,"The match has already started or finished.");CheckStart(s,m,request.ConfirmEarlyStart);
                BadmintonServing.Setup(s,m,request.Serving);live.RallyHistoryComplete=true;if(m.Rules.CountShuttles)live.Details.ShuttlesUsed??=0;
                m.Status=MatchStatus.Playing;live.StartedAtUtc=DateTimeOffset.UtcNow;live.StartedLocal=LocalNow(s);live.TimeZone=s.TimeZone;
            }
            else
            {
                TournamentRules.Need(m.Status==MatchStatus.Playing&&!live.PaperMode,"Only a playing match can receive tablet scores.");
                if(request.Action=="POINT")
                {
                    TournamentRules.Need(!live.AwaitingNextGame&&!MatchResults.Won(m),"Finish the current game or start the next game before scoring.");
                    TournamentRules.Need(request.Side is "A" or "B","Choose side A or B.");Remember(m);
                    BadmintonServing.Point(s,m,request.Side);
                    TournamentRules.Need(live.A<=m.Rules.PointCap&&live.B<=m.Rules.PointCap,"Score exceeds the match point cap.");
                    if(MatchResults.GameWon(live.A,live.B,m.Rules)){m.Games.Add(new(live.A,live.B));live.AwaitingNextGame=true;live.IntervalUntil=MatchResults.Won(m)?null:DateTimeOffset.UtcNow.AddSeconds(m.Rules.BetweenGamesSeconds);}
                }
                else if(request.Action is "SHUTTLE" or "CARD" or "CHALLENGE")
                {
                    var game=Math.Max(1,m.Games.Count+(live.AwaitingNextGame?0:1));
                    if(request.Action=="SHUTTLE")
                    {TournamentRules.Need(m.Rules.CountShuttles,"Shuttle tracking is disabled for this match.");live.Details.ShuttlesUsed=(live.Details.ShuttlesUsed??0)+1;}
                    else if(request.Action=="CARD")
                    {TournamentRules.Need(m.Rules.CardsEnabled,"Card tracking is disabled for this match.");live.Details.Cards.Add(new(request.ParticipantId??"",request.CardType??"",game,request.Remark));}
                    else
                    {TournamentRules.Need(m.Rules.ChallengesEnabled&&m.ReviewAvailable,"Challenges are not available for this match assignment.");TournamentRules.Need(request.Outcome!="U"||live.Details.Challenges.Count(c=>c.Side==request.Side&&c.Game==game&&c.Outcome=="U")<m.Rules.ChallengesPerGame,"No challenges remain for this side in the current game.");live.Details.Challenges.Add(new(request.Side??"",game,request.Outcome??""));}
                    ResultValidation.Details(s,m,live.Details);
                }
                else if(request.Action=="UNDO")
                {
                    TournamentRules.Need(live.History.Count>0,"No scoring action can be undone.");var previous=live.History[^1];live.History.RemoveAt(live.History.Count-1);live.A=previous.A;live.B=previous.B;live.AwaitingNextGame=previous.AwaitingNextGame;m.Games=previous.Games;live.Serving=BadmintonServing.Copy(previous.Serving);live.IntervalUntil=previous.IntervalUntil;
                    if(previous.RallyCount is int count&&count<live.Rallies.Count)live.Rallies.RemoveRange(count,live.Rallies.Count-count);
                }
                else if(request.Action=="NEXT")
                {
                    TournamentRules.Need(live.AwaitingNextGame&&!MatchResults.Won(m),"There is no next game to start.");Remember(m);live.A=0;live.B=0;BadmintonServing.Setup(s,m,request.Serving,true);live.AwaitingNextGame=false;live.IntervalUntil=null;
                }
                else if(request.Action=="SETUP")
                {
                    TournamentRules.Need(!live.AwaitingNextGame&&!MatchResults.Won(m),"Confirm the next game's positions when starting that game.");
                    TournamentRules.Need(live.Serving is null||!string.IsNullOrWhiteSpace(request.Remark)&&request.Remark.Length<=300,"Enter a reason for correcting serving positions.");
                    Remember(m);BadmintonServing.Setup(s,m,request.Serving);
                }
                else if(request.Action=="PAPER")
                {
                    TournamentRules.Need(!string.IsNullOrWhiteSpace(request.Remark)&&request.Remark.Length<=300,"Enter a reason for continuing on paper.");
                    live.PaperMode=true;s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
                }
                else
                {
                    m.WinnerId=MatchResults.Winner(m,m.Games);m.Status=MatchStatus.Review;live.ResultSource="T";live.SubmittedAtUtc=DateTimeOffset.UtcNow;m.ActualFinishedAt=LocalNow(s);
                }
            }
            live.Version++;live.Receipts.Add(new(request.OperationId,hash,live.Version));if(live.Receipts.Count>128)live.Receipts.RemoveAt(0);
            return new{matchId=m.Id,operationId=request.OperationId,action=request.Action,reason=request.Remark,before,after=new{m.Status,live.A,live.B,live.Version,games=m.Games.ToArray(),serving=live.Serving,details=ResultValidation.Copy(live.Details)}};
        });
        if(request.Action=="PAPER")return new{serverTime=DateTimeOffset.UtcNow,heartbeatSeconds=HeartbeatSeconds,sessionId="",match=Row(s,Find(s,commandMatchId))};
        Touch(Authenticate(s,token),context);return TabletView(s,token);
    }
    private static void Remember(TournamentMatch m)
    {
        var live=m.Live!;live.History.Add(new(live.A,live.B,live.AwaitingNextGame,m.Games.ToList(),BadmintonServing.Copy(live.Serving),live.IntervalUntil,live.Rallies.Count));if(live.History.Count>50)live.History.RemoveAt(0);
    }
    private static void CheckStart(SetupData s,TournamentMatch m,bool confirmEarlyStart)
    {
        ResultDependencies.Ready(m);TournamentRules.Need(m.RulesAssigned,"Assign a saved game preset before starting.");EventOperations.CheckStart(s,m);
        var now=LocalNow(s);var eventId=s.Programs.Single(p=>p.Id==m.ProgramId).EventId;var plan=TournamentRules.Event(s,eventId);var ev=s.Events.Single(e=>e.Id==eventId);
        var sessions=EventOperations.Sessions(s,plan).ToList();
        var active=sessions.Any(w=>w.Date.ToDateTime(w.Start)<=now&&now<w.Date.ToDateTime(w.End));
        var early=m.Start>now;
        TournamentRules.Need(DateOnly.FromDateTime(now)>=ev.StartDate&&DateOnly.FromDateTime(now)<=ev.EndDate,"The match must start within the event dates.");
        if(early){
            TournamentRules.Need(m.Start!.Value.Date==now.Date,"An early start must be on the scheduled playing date.");
            TournamentRules.Need(confirmEarlyStart,"This match is starting earlier than scheduled. Confirm the early start to continue.");
        }
        TournamentRules.Need(active||early,"The match must start within an active playing session.");
        var earlyCourt=early&&!active&&sessions.Any(w=>w.Date.ToDateTime(w.Start)<=m.Start&&m.Start<w.Date.ToDateTime(w.End)&&SessionCourts.Allows(w,m.Court??0));
        TournamentRules.Need(m.Court.HasValue&&!s.Venue.ClosedCourts.Contains(m.Court.Value),"This court is closed.");
        TournamentRules.Need(SessionCourts.Available(s,plan,m.Court!.Value,now)||earlyCourt||SessionCourts.ExceptionApplies(m,now),"This court is unavailable in the current session. Ask the organiser to select an available court or record a court exception when assigning the match.");
        var officials=TournamentRules.OfficialIds(m).ToHashSet();
        foreach(var other in TournamentScheduler.Matches(s).Where(o=>o.Id!=m.Id&&o.Status!="BY"))
        {
            if(MatchStatus.Occupied(other.Status)||s.Venue.Bindings.Any(b=>b.MatchId==other.Id))TournamentRules.Need(other.Court!=m.Court&&!TournamentRules.OfficialIds(other).Any(officials.Contains),$"Match {other.Number} is still occupying this court or an official.");
        }
    }
    public SetupData Confirm(MatchControlRequest request)=>store.Change(request.Revision,"LIVE_RESULT_CONFIRM",s=>
    {
        var m=Find(s,request.MatchId);TournamentRules.Need(m.Status==MatchStatus.Review&&m.Live?.Version==request.Version,"The submitted result changed. Refresh before confirming.");
        m.WinnerId=MatchResults.Winner(m,m.Games);m.Status=MatchStatus.Completed;m.Live!.Version++;m.Live.History.Clear();m.Live.Receipts.Clear();CompleteProgram(s,m);s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
    },auditView:s=>Audit(s,request.MatchId,null,request.Reason));
    public static void CompleteProgram(SetupData s,TournamentMatch m)
    {
        var plan=s.ProgramPlans.Single(p=>p.ProgramId==m.ProgramId);ResultDependencies.Refresh(plan);if(plan.Format=="RR"&&plan.Matches.All(x=>MatchStatus.Finished(x.Status)))plan.Phase="C";else if(m.Phase=="K"&&m.Round==plan.LastRound&&MatchStatus.Finished(m.Status)&&plan.Matches.Count(x=>x.Phase=="K"&&x.Round==plan.LastRound)==1)plan.Phase="C";
    }
    public object Resume(ResumeTabletRequest request,HttpContext context)
    {
        var code=LiveAccess.Code();var bindingId=Guid.NewGuid().ToString("N");
        var saved=store.Change(request.Revision,"LIVE_PAPER_RESUME",s=>
        {
            var m=Find(s,request.MatchId);var live=m.Live;
            TournamentRules.Need(m.Status==MatchStatus.Playing&&live?.PaperMode==true,"Only a playing paper-mode match can resume on a tablet.");
            if(live!.Version!=request.Version)throw new RuleException("Score changed. Reload before resuming.",409);
            TournamentRules.Need(m.RulesAssigned&&m.Court.HasValue&&!s.Venue.ClosedCourts.Contains(m.Court.Value),"Keep the saved preset and an open assigned court before resuming.");
            var plan=TournamentRules.Event(s,s.Programs.Single(p=>p.Id==m.ProgramId).EventId);
            TournamentRules.Need(plan.Officials.Any(o=>o.Id==m.UmpireId),"The match needs its assigned umpire before resuming.");
            TournamentRules.Need(!OfficialReservations(s,m.UmpireId!).Any(o=>o.Id!=m.Id),"The umpire is occupied by another match.");
            TournamentRules.Need(!s.Venue.Bindings.Any(b=>b.MatchId!=m.Id&&b.Court==m.Court)&&!TournamentScheduler.Matches(s).Any(o=>o.Id!=m.Id&&MatchStatus.Occupied(o.Status)&&(o.Court==m.Court||TournamentRules.OfficialIds(o).Intersect(TournamentRules.OfficialIds(m)).Any())),"The court or officials are occupied by another match.");
            TournamentRules.Need(request.Games is not null&&request.Games.Count>=m.Games.Count&&request.Games.Count<=m.Rules.GamesToWin*2-2,"Retain recorded completed games and enter only games before the match-winning game. Use Enter Paper Result if the match has finished.");
            var winsA=0;var winsB=0;
            foreach(var game in request.Games!)
            {
                TournamentRules.Need(game is not null&&MatchResults.GameWon(game.A,game.B,m.Rules),"A completed game does not satisfy the saved scoring rules.");
                if(game!.A>game.B)winsA++;else winsB++;
                TournamentRules.Need(winsA<m.Rules.GamesToWin&&winsB<m.Rules.GamesToWin,"The match is already finished. Use Enter Paper Result.");
            }
            var hi=Math.Max(request.ScoreA,request.ScoreB);var margin=Math.Abs((long)request.ScoreA-request.ScoreB);
            TournamentRules.Need(request.ScoreA>=0&&request.ScoreB>=0&&hi<m.Rules.PointCap&&!(hi>=m.Rules.PointsToWin&&margin>=m.Rules.WinBy),"Enter an unfinished current-game score within the saved scoring rules. Move a completed game to Completed Games.");
            if(request.ScoreA==0&&request.ScoreB==0&&request.Games.Count>0)TournamentRules.Need(request.Serving is not null&&request.Serving.ServerSide==(request.Games[^1].A>request.Games[^1].B?"A":"B"),"The previous game's winning side serves first.");
            m.Games=request.Games.ToList();live.A=request.ScoreA;live.B=request.ScoreB;
            BadmintonServing.Setup(s,m,request.Serving);
            live.Resumptions.Add(new(m.Games.Count+1,live.A,live.B,live.Rallies.Count,DateTimeOffset.UtcNow));live.RallyHistoryComplete=false;
            live.History.Clear();live.Receipts.Clear();live.AwaitingNextGame=false;live.IntervalUntil=null;live.PaperMode=false;live.SubmittedAtUtc=null;live.Version++;
            m.WinnerId=null;m.ActualFinishedAt=null;
            s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
            s.Venue.Bindings.Add(new(){Id=bindingId,Court=m.Court!.Value,MatchId=m.Id,CodeHash=LiveAccess.Hash(code),CodeExpires=DateTimeOffset.UtcNow.AddMinutes(3)});
        },auditView:s=>Audit(s,request.MatchId,null,"Resume from paper scores"));
        return new{revision=saved.Revision,code,expiresAt=saved.Venue.Bindings.Single(b=>b.Id==bindingId).CodeExpires,tabletUrl=$"{context.Request.Scheme}://{context.Request.Host}/umpire",match=Row(saved,Find(saved,request.MatchId))};
    }
    public SetupData Paper(MatchControlRequest request)=>store.Change(request.Revision,"LIVE_PAPER",s=>
    {
        var m=Find(s,request.MatchId);TournamentRules.Need(m.Status==MatchStatus.Playing&&m.Live?.Version==request.Version,"Only the current playing match can switch to paper.");
        TournamentRules.Need(!string.IsNullOrWhiteSpace(request.Reason)&&request.Reason.Length<=300,"Enter a reason for switching to paper.");m.Live!.PaperMode=true;m.Live.Version++;s.Venue.Bindings.RemoveAll(b=>b.MatchId==m.Id);
    },auditView:s=>Audit(s,request.MatchId,null,request.Reason));
    public SetupData Close(CourtControlRequest request)=>store.Change(request.Revision,"COURT_AVAILABILITY",s=>
    {
        TournamentRules.Need(request.Court>=1&&request.Court<=s.CourtCount,"Choose a venue court.");
        TournamentRules.Need(!request.Closed||!s.Venue.Bindings.Any(b=>b.Court==request.Court)&&!TournamentScheduler.Matches(s).Any(m=>m.Court==request.Court&&MatchStatus.Occupied(m.Status)),"Release the waiting assignment or finish the active match before closing this court.");
        s.Venue.ClosedCourts.Remove(request.Court);if(request.Closed)s.Venue.ClosedCourts.Add(request.Court);
    },auditView:s=>Audit(s,null,request.Court,""));
    public SetupData Cancel(CancelCourtRequest request)=>store.Change(request.Revision,"COURT_RELEASE_WAITING",s=>
    {
        TournamentRules.Need(request.Court>=1&&request.Court<=s.CourtCount,"Choose a venue court.");var binding=s.Venue.Bindings.SingleOrDefault(b=>b.Court==request.Court);TournamentRules.Need(binding is not null&&Find(s,binding.MatchId).Status==MatchStatus.Scheduled,"Only an unstarted call can be cancelled. Started matches retain their scores and assignment.");
        var m=Find(s,binding!.MatchId);m.UmpireId=null;if(m.Live is not null){m.Live.DeviceName="";m.Live.DeviceIp="";}s.Venue.Bindings.Remove(binding);
    },auditView:s=>Audit(s,null,request.Court,""));
    private static object Audit(SetupData s,string? matchId,int? court,string reason)
    {
        var match=matchId is null?null:TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==matchId);
        var number=court??match?.Court;
        return new{reason,match,live=match?.Live,bindings=s.Venue.Bindings.Where(b=>b.MatchId==matchId||b.Court==number).ToArray(),closedCourts=s.Venue.ClosedCourts.ToArray()};
    }
    public static LiveMatchView Row(SetupData s,TournamentMatch m)
    {
        var p=s.Programs.Single(p=>p.Id==m.ProgramId);var ev=s.Events.Single(e=>e.Id==p.EventId);var plan=TournamentRules.Event(s,p.EventId);var live=m.Live;var last=m.Games.LastOrDefault();
        string Name(string? id)=>id is null?"BYE":string.Join(" / ",s.Entries.Single(e=>e.Id==id).ParticipantIds.Select(id=>s.Participants.Single(p=>p.Id==id).Name));
        return new(){EarlyStartMinutes=m.Status==MatchStatus.Scheduled&&m.Start.HasValue?Math.Max(0,(int)Math.Ceiling((m.Start.Value-LocalNow(s)).TotalMinutes)):0,Serving=live?.Serving,ServerId=BadmintonServing.Server(s,m),ReceiverId=BadmintonServing.Receiver(s,m),IntervalUntil=live?.IntervalUntil,Rallies=live?.Rallies??[],Resumptions=live?.Resumptions??[],TimeZone=s.TimeZone,RallyHistoryComplete=live?.RallyHistoryComplete??false,ReviewAvailable=m.ReviewAvailable,PresetId=m.PresetId,RulesAssigned=m.RulesAssigned,ResultDetails=m.ResultDetails,Players=new[]{"A","B"}.SelectMany(side=>BadmintonServing.Members(s,m,side).Select(id=>new MatchPlayer(id,s.Participants.Single(p=>p.Id==id).Name,side))).ToArray(),Id=m.Id,EventId=ev.Id,EventName=ev.Name,ProgramId=p.Id,ProgramName=p.Name,Number=m.Number,RoundLabel=m.RoundLabel,Status=m.Status,EntryA=m.EntryA,EntryB=m.EntryB,NameA=m.PendingA?"Awaiting earlier result":Name(m.EntryA),NameB=m.PendingB?"Awaiting earlier result":Name(m.EntryB),Court=m.Court,PlannedStart=m.Start,PlannedEnd=m.End,DurationMinutes=m.DurationMinutes,UmpireId=m.UmpireId,ServiceJudgeId=m.ServiceJudgeId,LineJudgeIds=m.LineJudgeIds,UmpireName=plan.Officials.SingleOrDefault(o=>o.Id==m.UmpireId)?.Name??"—",ScoreA=MatchStatus.Finished(m.Status)?last?.A??0:live?.A??0,ScoreB=MatchStatus.Finished(m.Status)?last?.B??0:live?.B??0,Games=m.Games,GamesWonA=m.Games.Count(g=>g.A>g.B),GamesWonB=m.Games.Count(g=>g.B>g.A),Rules=m.Rules,Version=live?.Version??0,StartedAt=live?.StartedAtUtc,FinishedAt=live?.SubmittedAtUtc,AwaitingNextGame=live?.AwaitingNextGame??false,MatchWon=MatchResults.Won(m),CanUndo=(live?.History.Count??0)>0,PaperMode=live?.PaperMode??false,DeviceName=live?.DeviceName??"",DeviceIp=live?.DeviceIp??""};
    }
}
public sealed class LiveMatchView
{
    public int EarlyStartMinutes {get;set;}
    public ServingState? Serving {get;set;}public string ServerId {get;set;}="";public string ReceiverId {get;set;}="";public DateTimeOffset? IntervalUntil {get;set;}public List<RallyRecord> Rallies {get;set;}=[];public List<ResumePoint> Resumptions {get;set;}=[];public string TimeZone {get;set;}="Asia/Singapore";public bool RallyHistoryComplete {get;set;}public bool ReviewAvailable {get;set;}
    public string? PresetId {get;set;}public bool RulesAssigned {get;set;}public ResultDetails ResultDetails {get;set;}=new();public MatchPlayer[] Players {get;set;}=[];public string Id {get;set;}="";public string EventId {get;set;}="";public string EventName {get;set;}="";public string ProgramId {get;set;}="";public string ProgramName {get;set;}="";public int Number {get;set;}public string RoundLabel {get;set;}="";public string Status {get;set;}="";public string EntryA {get;set;}="";public string? EntryB {get;set;}public string NameA {get;set;}="";public string NameB {get;set;}="";public int? Court {get;set;}public DateTime? PlannedStart {get;set;}public DateTime? PlannedEnd {get;set;}public int DurationMinutes {get;set;}public string? ServiceJudgeId {get;set;}public string[] LineJudgeIds {get;set;}=[];public string? UmpireId {get;set;}public string UmpireName {get;set;}="";public int ScoreA {get;set;}public int ScoreB {get;set;}public List<GameScore> Games {get;set;}=[];public int GamesWonA {get;set;}public int GamesWonB {get;set;}public BadmintonRules Rules {get;set;}=new();public long Version {get;set;}public DateTimeOffset? StartedAt {get;set;}public DateTimeOffset? FinishedAt {get;set;}public bool AwaitingNextGame {get;set;}public bool MatchWon {get;set;}public bool CanUndo {get;set;}public bool PaperMode {get;set;}public string DeviceName {get;set;}="";public string DeviceIp {get;set;}="";
}

public sealed record MatchPlayer(string Id,string Name,string Side="");
