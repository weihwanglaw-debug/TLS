using LiveScore.Api;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
Check(TrsIntegration.TransferError(403,"{\"message\":\"Integration key is invalid.\"}")=="Integration key is invalid.","TRS error message is preserved");
Check(TrsIntegration.TransferError(400,"{\"detail\":\"Event is not ready.\"}")=="Event is not ready.","TRS problem details are preserved");
Check(TrsIntegration.TransferError(502,"<html>Proxy error</html>")=="TRS returned HTTP 502.","non-JSON TRS errors retain HTTP status without rendering HTML");
TournamentMatch M(string a,string b,string w,params GameScore[] games)=>new(){Id=a+b,ProgramId="P",EntryA=a,EntryB=b,WinnerId=w,Status="C",GroupId="G",Games=games.ToList()};
var g=new DrawGroup("G","Group A",["A","B","C","D"]);var p=new ProgramPlan{ProgramId="P",Format="GK",Phase="G",Groups=[g],AdvancePerGroup=1,SeedsReviewed=true};
p.Matches=[M("A","B","A",new(21,19),new(21,19)),M("B","C","B",new(21,0),new(21,0)),M("C","A","C",new(21,0),new(21,0)),M("A","D","A",new(21,19),new(21,19)),M("B","D","B",new(21,0),new(21,0)),M("C","D","C",new(21,19),new(19,21),new(21,19))];
Check(TournamentRules.Standings(p,g).Select(r=>r.EntryId).SequenceEqual(new[]{"A","B","C","D"}),"three-way reduces to two: head-to-head");
Check(BadmintonStandings.Pending(p,g).Length==0,"resolved ranking");
g=new("G","Group A",["A","B","C"]);p.Groups=[g];p.Matches=[M("A","B","A",new(21,10),new(21,10)),M("B","C","B",new(21,10),new(21,10)),M("C","A","C",new(21,10),new(21,10))];
Check(BadmintonStandings.Pending(p,g).Single().Length==3,"fully tied has no artificial winner");
var d=new SetupData();d.Programs.Add(new("P","E","Singles","Badminton","S",1,1,0,100,"O",2,100,"E"));d.ProgramPlans.Add(p);
try{TournamentRules.CreateRound(d,p);Check(false,"unresolved qualifier accepted");}catch(RuleException){checks++;}
BadmintonStandings.Resolve(d,new(0,"P","G",["C","A","B"],"Referee draw"));
Check(TournamentRules.Standings(p,g)[0].EntryId=="C"&&BadmintonStandings.Pending(p,g).Length==0,"manual resolution");
p.AdvancePerGroup=2;var qualifiers=TournamentRules.CreateRound(d,p);Check(qualifiers.Single().EntryA=="C"&&qualifiers.Single().EntryB=="A","manual order progresses exactly once");
p.Matches[0].Games=[new(21,9),new(21,10)];Check(TournamentRules.Standings(p,g)[0].EntryId=="A","changed result invalidates decision");
Console.WriteLine($"PASS: {checks} ranking checks");
foreach(var count in new[]{2,3,4,5,8}){
 var groups=Enumerable.Range(0,count).Select(i=>new DrawGroup("G"+i,"Group "+i,["W"+i,"R"+i])).ToList();
 var matches=Enumerable.Range(0,count).Select(i=>new TournamentMatch{EntryA="W"+i,EntryB="R"+i}).ToList();QualificationPlacement.Separate(matches,groups);
 Check(matches.All(m=>!groups.Any(g=>g.EntryIds.Contains(m.EntryA)&&g.EntryIds.Contains(m.EntryB!))),"same group rematch");Check(matches.Select(m=>m.EntryB).Distinct().Count()==count,"duplicate qualifier");
}
Console.WriteLine($"PASS: {checks} ranking/qualification checks");
var resultMatch=new TournamentMatch{EntryA="A",EntryB="B"};
Check(OfficialResults.Winner(resultMatch,new(0,"M",[new(21,15),new(12,8)],Outcome:"R",OfficialWinner:"A",Reason:"B retired"))=="A","retirement preserves partial score");
Check(OfficialResults.Winner(resultMatch,new(0,"M",[new(4,8)],Outcome:"D",OfficialWinner:"A",Reason:"Referee decision"))=="A","disqualification official winner");
Check(OfficialResults.Winner(resultMatch,new(0,"M",[new(21,0),new(21,0)],Outcome:"M",OfficialWinner:"B",Reason:"Administrative correction"))=="B","official winner override");
try{OfficialResults.Winner(resultMatch,new(0,"M",[new(21,0),new(21,0)],Outcome:"N",OfficialWinner:"B",Reason:"Correction"));Check(false,"normal winner mismatch");}catch(RuleException){checks++;}
Console.WriteLine($"PASS: {checks} rules checks");
var parent=new TournamentMatch{Id="S1",ProgramId="P",Phase="K",Round=1,Number=1,EntryA="A",EntryB="B",WinnerId="A",Status="C"};
var sibling=new TournamentMatch{Id="S2",ProgramId="P",Phase="K",Round=1,Number=2,EntryA="C",EntryB="D",WinnerId="C",Status="C"};
var final=new TournamentMatch{Id="F",ProgramId="P",Phase="K",Round=2,Number=3,EntryA="A",EntryB="C",Status="SC"};
var dependencies=new ProgramPlan{ProgramId="P",Phase="K",LastRound=2,Matches=[parent,sibling,final]};var depData=new SetupData{ProgramPlans=[dependencies]};
var affected=ResultDependencies.Affected(dependencies,parent,"B");Check(affected.Count==1,"only dependent final affected");parent.WinnerId="B";
ResultDependencies.Apply(depData,dependencies,parent,new(0,"S1",[],Outcome:"M",OfficialWinner:"B",Reason:"Correction",DownstreamHandling:"U"),affected);
Check(final.EntryA=="B"&&final.EntryB=="C"&&!final.PendingA,"safe dependent updates");
final.Status="IP";try{ResultDependencies.Apply(depData,dependencies,parent,new(0,"S1",[],DownstreamHandling:"U"),affected);Check(false,"started silently changed");}catch(RuleException){checks++;}
ResultDependencies.Apply(depData,dependencies,parent,new(0,"S1",[],DownstreamHandling:"K"),affected);Check(final.Status=="IP"&&final.DependencyOverride,"retain explicit");
ResultDependencies.Apply(depData,dependencies,parent,new(0,"S1",[],DownstreamHandling:"R"),affected);Check(final.Status=="SC"&&!final.DependencyOverride,"replay explicit");
Console.WriteLine($"PASS: {checks} rules and dependency checks");

var morning=new PlayingSession(new DateOnly(2030,1,1),new TimeOnly(9,0),new TimeOnly(13,0),[1]);
var afternoon=new PlayingSession(new DateOnly(2030,1,1),new TimeOnly(14,0),new TimeOnly(18,0),[1,2]);
Check(SessionCourts.Allows(morning,1)&&!SessionCourts.Allows(morning,2)&&SessionCourts.Allows(afternoon,2),"session court availability");
var exceptionMatch=new TournamentMatch{Court=2,CourtExceptionCourt=2,CourtExceptionUntil=new DateTime(2030,1,1,13,0,0),CourtExceptionReason="Referee opened court"};
Check(SessionCourts.ExceptionApplies(exceptionMatch,new DateTime(2030,1,1,10,0,0)),"active court exception");
Check(!SessionCourts.ExceptionApplies(exceptionMatch,new DateTime(2030,1,1,13,0,0)),"court exception expires");
exceptionMatch.Court=3;Check(!SessionCourts.ExceptionApplies(exceptionMatch,new DateTime(2030,1,1,10,0,0)),"court exception cannot move courts");
Console.WriteLine($"PASS: {checks} rules, dependency and court checks");

var one=new DrawGroup("G","Group A",["A","B","C","D"]);
var two=new DrawGroup("H","Group B",["E","F"]);
var standingsPlan=new ProgramPlan{SeedsReviewed=true,ProgramId="P",Format="GK",Phase="G",AdvancePerGroup=1,Groups=[one,two]};
standingsPlan.Matches=[M("A","B","A",new(21,10),new(21,10)),M("A","C","A",new(21,10),new(21,10)),M("A","D","A",new(21,10),new(21,10)),M("B","C","B",new(21,10),new(21,10)),M("C","D","C",new(21,10),new(21,10)),M("D","B","D",new(21,10),new(21,10))];
var second=M("E","F","E",new(21,10),new(21,10));second.GroupId="H";standingsPlan.Matches.Add(second);
Check(TournamentRules.Standings(standingsPlan,one)[0].EntryId=="A","normal clear winner");
Check(BadmintonStandings.Pending(standingsPlan,one).Single().Length==3,"lower three-way unresolved");
d.ProgramPlans=[standingsPlan];var oneEach=TournamentRules.CreateRound(d,standingsPlan);
Check(oneEach.Count==1&&new[]{oneEach[0].EntryA,oneEach[0].EntryB}.ToHashSet().SetEquals(new[]{"A","E"}),"nonqualifying tie does not block one qualifier per group");
standingsPlan.AdvancePerGroup=2;standingsPlan.Phase="G";standingsPlan.LastRound=0;
try{TournamentRules.CreateRound(d,standingsPlan);Check(false,"qualification boundary tie accepted");}catch(RuleException){checks++;}
BadmintonStandings.Resolve(d,new(0,"P","G",["A","D","B","C"],"Referee draw"));
var twoEach=TournamentRules.CreateRound(d,standingsPlan);Check(twoEach.SelectMany(m=>new[]{m.EntryA,m.EntryB}).Distinct().Count()==4,"resolved multiple qualifiers no duplicate or missing entry");
var retirementRow=M("A","B","A",new(21,15),new(12,8));retirementRow.ResultKind="R";var retirementPlan=new ProgramPlan{Groups=[new("G","Group A",["A","B"])],Matches=[retirementRow]};
Check(TournamentRules.Standings(retirementPlan,retirementPlan.Groups[0])[0].GamesFor==1,"unfinished retirement game is not counted as a completed game");
Console.WriteLine($"PASS: {checks} total focused rules checks");

// Exercise the start guard without modifying stored tournament records.
var startData=new SetupData{TimeZone="UTC"};var current=LiveRuntime.LocalNow(startData);
// Choose a venue offset with daytime hours, keeping these checks independent of wall-clock hour.
startData.TimeZone="Etc/GMT"+(current.Hour>=12?"+":"-")+Math.Abs(current.Hour-12);if(current.Hour==12)startData.TimeZone="UTC";
current=LiveRuntime.LocalNow(startData);var today=DateOnly.FromDateTime(current);
startData.Events=[new("E","Event","Hall",today,today.AddDays(1))];startData.Programs=[new("P","E","Singles","Badminton","S",1,1,0,100,"O",2,100,"E")];
var startMatch=new TournamentMatch{Id="M",ProgramId="P",EntryA="A",EntryB="B",Status="SC",Court=1,UmpireId="U",RulesAssigned=true,Start=current.AddMinutes(30),End=current.AddMinutes(70)};
var startPlan=new EventPlan{EventId="E",Sessions=[new(today,TimeOnly.FromDateTime(current.AddMinutes(30)),TimeOnly.FromDateTime(current.AddHours(2)),[1])]};
startData.EventPlans=[startPlan];startData.ProgramPlans=[new(){ProgramId="P",Matches=[startMatch]}];
var guard=typeof(LiveRuntime).GetMethod("CheckStart",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
void StartCheck(bool confirm,bool allowed,string label){bool passed=true;try{guard.Invoke(null,[startData,startMatch,confirm]);}catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is RuleException){passed=false;}Check(passed==allowed,label);}
StartCheck(false,false,"early start requires explicit confirmation");StartCheck(true,true,"confirmed early start before session opens");
startData.Venue.ClosedCourts=[1];StartCheck(true,false,"early start cannot use closed court");startData.Venue.ClosedCourts=[];
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddMinutes(30)),TimeOnly.FromDateTime(current.AddHours(2)),[2])];StartCheck(true,true,"same-day early start may use any open court");
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddMinutes(-30)),TimeOnly.FromDateTime(current.AddMinutes(20)),[2]),new(today,TimeOnly.FromDateTime(current.AddMinutes(30)),TimeOnly.FromDateTime(current.AddHours(2)),[1])];StartCheck(true,true,"same-day admin assignment is independent of session court allocations");
startPlan.Sessions.RemoveAt(0);startMatch.PendingA=true;StartCheck(true,false,"early start cannot bypass prerequisite results");startMatch.PendingA=false;
var busyMatch=new TournamentMatch{Id="BUSY",ProgramId="P",EntryA="C",EntryB="D",Status="IP",Court=1};startData.ProgramPlans[0].Matches.Add(busyMatch);StartCheck(true,false,"occupied court blocks early start");busyMatch.Court=2;busyMatch.UmpireId="U";StartCheck(true,false,"occupied umpire blocks early start");startData.ProgramPlans[0].Matches.Remove(busyMatch);
startMatch.Start=startMatch.Start!.Value.AddDays(1);startMatch.End=startMatch.End!.Value.AddDays(1);StartCheck(true,false,"tomorrow cannot start today");
startMatch.Start=current.AddMinutes(-30);startMatch.End=current.AddMinutes(10);StartCheck(false,true,"same-day late start does not require active session");
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddHours(-1)),TimeOnly.FromDateTime(current.AddHours(2)),[1])];StartCheck(false,true,"normal start needs no early confirmation");
startData.Events=[new("E","Event","Hall",today.AddDays(1),today.AddDays(2))];StartCheck(true,false,"event date restriction remains");
Console.WriteLine($"PASS: {checks} total focused rules and early-start checks");

var courtData=new SetupData{CourtCount=3};
var courtPlan=new EventPlan{Sessions=[morning,afternoon,new(new DateOnly(2030,1,2),new TimeOnly(9,0),new TimeOnly(13,0),[3])]};
var queued=new TournamentMatch{Court=2,Start=new DateTime(2030,1,1,14,0,0)};
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,8,0,0)).SequenceEqual(new[]{1,2,3}),"same-day preparation uses open master courts");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).SequenceEqual(new[]{1,2,3}),"active session does not restrict same-day courts");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,13,0,0)).SequenceEqual(new[]{1,2,3}),"session boundaries do not block same-day calls");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,18,0,0)).SequenceEqual(new[]{1,2,3}),"late same-day calls are allowed");
courtData.Venue.ClosedCourts=[2];Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,14,0,0)).SequenceEqual(new[]{1,3}),"closed courts excluded from shared eligibility");
queued.CourtExceptionCourt=3;queued.CourtExceptionUntil=new(2030,1,1,13,0,0);queued.CourtExceptionReason="Existing authorised exception";
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).SequenceEqual(new[]{1,3}),"legacy explicit exception retains its target court");
queued.Start=new(2030,1,2,10,0,0);Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,14,0,0)).SequenceEqual(new[]{3}),"future-day preparation uses planned session allocation");
queued.Start=null;Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).Length==0,"unscheduled match requires scheduling");
Console.WriteLine($"PASS: {checks} total focused rules checks");

// Changing database IDs must not change qualifier slots or BYE recipients.
foreach(var groupCount in new[]{2,3,4})foreach(var advance in new[]{1,2})
{
    string? baseline=null;
    foreach(var variant in new[]{0,1,2})
    {
        var qp=new ProgramPlan{ProgramId="Q",Format="GK",Phase="G",AdvancePerGroup=advance,SeedsReviewed=true};
        for(var i=0;i<groupCount;i++)
        {
            var winner=variant==0?$"Z{i}":variant==1?$"A{groupCount-i}":$"M{(i+1)%groupCount}";
            var runner=$"R{variant}{i}";var gid=$"G{i}";
            qp.Groups.Add(new(gid,$"Group {(char)('A'+i)}",[winner,runner]));
            qp.Matches.Add(new(){Id=gid,ProgramId="Q",Phase="G",GroupId=gid,EntryA=winner,EntryB=runner,WinnerId=winner,Status="C",Games=[new(21,0),new(21,0)]});
        }
        if(groupCount==3&&advance==1&&variant==0)
        {
            var seeded=System.Text.Json.JsonSerializer.Deserialize<ProgramPlan>(System.Text.Json.JsonSerializer.Serialize(qp))!;
            var top=seeded.Groups[2].EntryIds[0];seeded.Seeds[top]=1;
            Check(TournamentRules.CreateRound(new SetupData(),seeded).Single(m=>m.Status=="BY").EntryA==top,"sporting seed precedes group order for BYE allocation");
        }
        var before=System.Text.Json.JsonSerializer.Serialize(qp.Matches);
        var rows=TournamentRules.CreateRound(new SetupData(),qp);
        var signature=string.Join("|",rows.Select(m=>qp.QualificationOrigins[m.EntryA]+":"+(m.EntryB is null?"BYE":qp.QualificationOrigins[m.EntryB])));
        baseline??=signature;Check(signature==baseline,$"group-origin placement independent of IDs: {groupCount}/{advance}/{variant}");
        Check(rows.SelectMany(m=>new[]{m.EntryA,m.EntryB}).Where(id=>id is not null).Distinct().Count()==groupCount*advance,"every qualifier appears exactly once");
        Check(System.Text.Json.JsonSerializer.Serialize(qp.Matches)==before,"group results untouched by qualifier generation");
    }
}
Console.WriteLine($"PASS: {checks} total focused rules and qualifier placement checks");
{
    var publicationData = new SetupData {
        Events = [new("EV000001", "Open", "Hall", new(2026,11,3), new(2026,11,3))],
        Programs = [new("PUBLISH-P", "EV000001", "Singles", "Badminton", "S", 1, 1, 0, 100, "O", 2, 32, "E")],
        Participants = [new("U1", "EV000001", "Alice", new(2000,1,1), "F", "a@example.com", "+6581234567"), new("U2", "EV000001", "Beth", new(2000,1,1), "F", "b@example.com", "+6581234568")],
        Entries = [new("E1", "EV000001", "PUBLISH-P", ["U1"]), new("E2", "EV000001", "PUBLISH-P", ["U2"])],
        ImportIdentities = new() { ["event"] = new("EV000001","event","event:71","EV000001"), ["program"] = new("EV000001","program","program:72","PUBLISH-P"), ["entry"] = new("EV000001","entry","entry:91","E1") },
        ProgramPlans = [new() { ProgramId = "PUBLISH-P", Matches = [new() { Id = "LIVE1", ProgramId = "PUBLISH-P", Phase = "K", Round = 1, RoundLabel = "Final", EntryA = "E1", EntryB = "E2", Status = "IP", Games = [new(21,19)], Live = new() { A=5, B=7 } }] }]
    };
    publicationData.ProgramPlans[0].Matches[0].OfficialReason="Private medical information";
    var publication = TrsPublication.Snapshot(publicationData,"EV000001",null,true);
    Check(publication.Matches[0].Remark=="","private administrator reasons never enter publication snapshots");
    Check(publication.EventId==71 && publication.ProgramId is null && publication.Matches.Single().ProgramId==72,"live publication original TRS event/program identity");
    Check(publication.Matches[0].ScoreA==5 && publication.Matches[0].ScoreB==7 && publication.Matches[0].Games.Single()==new GameScore(21,19),"live publication aligns current and previous scores");
    Check(publication.Matches[0].Team1.Id=="entry:91" && publication.Matches[0].Team1.Players.Single()=="Alice","live publication describes original entry and players");
    Check(TrsPublication.Snapshot(publicationData,"EV000001","PUBLISH-P",false).Matches.Length==0,"unconfirmed live matches never enter official result snapshots");
    publicationData.Programs.Add(publicationData.Programs[0] with { Id = "PENDING", Name = "Doubles" });
    publicationData.ImportIdentities.Add("pending", new("EV000001", "program", "T02", "PENDING"));
    var match = publicationData.ProgramPlans[0].Matches[0];
    match.Status = "C";
    match.WinnerId = "E1";
    match.Start = new DateTime(2026, 11, 3, 14, 0, 0);
    var eventResult = TrsPublication.Snapshot(publicationData, "EV000001", null, false);
    Check(eventResult.Programs?.Length == 2 && eventResult.Programs.Any(p => p.Reference == "T02" && !p.CanLink),
        "full results include pending workbook programs and preserve their references");
    Check(eventResult.Matches is [{ ProgramId: 72, RoundLabel: "Final", MatchDate: "2026-11-03" }],
        "event publication carries the program, round and date of confirmed results");
}
Console.WriteLine($"PASS: {checks} including TRS publication checks");

{
    var correction = new ResultCorrection(DateTimeOffset.UtcNow,"Cleared incorrect result",[new(21,9)],"E1",new());
    var cleared = new TournamentMatch { Id="CLEARED",ProgramId="RESET-P",Phase="G",Round=1,Status="SC",Start=new(2026,11,4,10,0,0),End=new(2026,11,4,11,0,0),Court=1,Live=new() { Corrections=[correction] } };
    var resetData = new SetupData { Events=[new("RESET-E","Reset test","Hall",new(2026,11,4),new(2026,11,4))],Programs=[new("RESET-P","RESET-E","Singles","Badminton","S",1,1,0,100,"O",2,32,"E")],ProgramPlans=[new() { ProgramId="RESET-P",Matches=[cleared] }] };
    Check(FixtureResets.Protection(resetData,[cleared])=="","cleared correction history does not block schedule reset");
    Check(!GamePresets.Locked(cleared),"history never locks scoring preset changes");
    FixtureResets.ResetSchedule(resetData,new(0,"RESET-E",new(2026,11,4)));
    Check(cleared.Start is null&&cleared.End is null&&cleared.Court is null,"cleared match schedule can be removed");
    Check(cleared.Live!.Corrections.Count==0,"schedule reset removes operational correction history");
    FixtureResets.RequireScheduleReset(resetData,"RESET-E",[cleared]);
    Check(!FixtureResets.StartedOrScored(cleared),"cleared match remains eligible for draw reset");
    foreach(var status in new[]{"IP","RC","C","WO"}) { cleared.Status=status;Check(FixtureResets.StartedOrScored(cleared),"active or recorded match remains reset protected: "+status); }
    cleared.Status="SC";cleared.Live.StartedAtUtc=DateTimeOffset.UtcNow;Check(FixtureResets.StartedOrScored(cleared),"start timestamp still blocks reset");
    cleared.Live.StartedAtUtc=null;cleared.Games=[new(21,9)];Check(FixtureResets.StartedOrScored(cleared),"recorded games still block reset");
    cleared.Games=[];resetData.Venue.Bindings=[new() { MatchId=cleared.Id }];Check(FixtureResets.Protection(resetData,[cleared])=="Release waiting tablet assignments first","waiting tablet binding still blocks reset");
}
Console.WriteLine($"PASS: {checks} including cleared-result reset checks");

{
    var resetDay=DateOnly.FromDateTime(DateTime.Today);var time=resetDay.ToDateTime(new(10,0));
    var group=new TournamentMatch{Id="G",ProgramId="P",Phase="G",Round=1,GroupId="A",EntryA="A",EntryB="B",Status="C",WinnerId="A",Games=[new(21,9)],ResultKind="D",OfficialReason="old",Start=time,End=time.AddHours(1),Court=1,Live=new(){Version=9,A=21,StartedLocal=time,Resumptions=[new(1,2,3,0,DateTimeOffset.UtcNow)],Corrections=[new(DateTimeOffset.UtcNow,"old",[],"A",new())]}};
    var knockout=new TournamentMatch{Id="K",ProgramId="P",Phase="K",Round=1,EntryA="A",EntryB="C",Status="IP",Live=new(){StartedLocal=time}};
    var other=new TournamentMatch{Id="OTHER",ProgramId="Q",Phase="K",Round=1,EntryA="D",EntryB="E",Status="SC"};
    var session=new PlayingSession(resetDay,new(10,0),new(18,0),[1,2],["G","K","OTHER"],new(){{"P",2},{"Q",1}});
    var ep=new EventPlan{EventId="E",Sessions=[session]};
    var pp=new ProgramPlan{ProgramId="P",Phase="K",LastRound=1,Matches=[group,knockout],RankingDecisions=[new("A","old",["A","B"],"old",DateTimeOffset.UtcNow)],QualificationOrigins=new(){{"A","A1"}}};
    var data=new SetupData{CourtCount=2,Events=[new("E","Reset","Hall",resetDay,resetDay)],Programs=[new("P","E","Singles","Badminton","S",1,1,0,100,"O",2,32,"E"),new("Q","E","Other","Badminton","S",1,1,0,100,"O",2,32,"E")],ProgramPlans=[pp,new(){ProgramId="Q",Matches=[other]}],EventPlans=[ep],Venue=new(){Bindings=[new(){MatchId="G"},new(){MatchId="K"},new(){MatchId="OTHER"}]}};
    try{FixtureResets.ResetSchedule(data,new(0,"E",resetDay));Check(false,"completed reset allowed");}catch(RuleException){Check(group.Status=="C"&&pp.Matches.Count==2,"blocked reset preserves results and draws");}
    FixtureResets.ResetResult(data,group);FixtureResets.ResetResult(data,knockout);data.Venue.Bindings.RemoveAll(b=>b.MatchId!="OTHER");knockout.Start=time.AddHours(2);knockout.End=time.AddHours(3);knockout.Court=2;
    FixtureResets.ResetSchedule(data,new(0,"E",resetDay));
    Check(pp.Matches.SequenceEqual([group,knockout])&&pp.Phase=="K","date reset preserves all draw rounds and phase");
    Check(group.Start is null&&knockout.Start is null&&group.Status=="SC","eligible bookings are cleared");
    Check(data.Venue.Bindings.Single().MatchId=="OTHER","unaffected tablet reservations stay");
    Check(pp.RankingDecisions.Count==1&&pp.QualificationOrigins.Count==1,"schedule reset preserves ranking and qualifier records");
    try{FixtureResets.ResetSchedule(data,new(0,"E",resetDay));Check(false,"empty reset written");}catch(RuleException){Check(data.ProgramPlans[1].Matches.Single()==other,"empty range has no changes");}
    group.Start=time;group.End=time.AddHours(1);group.Court=1;
    Check(SessionCourts.EligibleCourts(data,ep,group,time.AddHours(12)).SequenceEqual([1,2]),"late same-day play permits open courts after session end");
    data.Venue.ClosedCourts=[2];Check(SessionCourts.EligibleCourts(data,ep,group,time.AddHours(12)).SequenceEqual([1]),"same-day flexibility retains closed-court restriction");
}
Console.WriteLine($"PASS: {checks} including complete reset lifecycle checks");

{
    var day=new DateOnly(2026,11,10);var now=new DateTimeOffset(2026,11,10,6,0,0,TimeSpan.Zero);
    var displayData=new SetupData{TimeZone="Asia/Singapore",CourtCount=8};
    foreach(var (id,start,end) in new[]{("E",day,day.AddDays(1)),("OTHER",day,day.AddDays(1)),("OLD",day.AddDays(-2),day.AddDays(-1)),("FUTURE",day.AddDays(1),day.AddDays(2))})
    {
        displayData.Events.Add(new(id,id+" event","Hall",start,end));
        displayData.Programs.Add(new(id+"-P",id,"Singles","Badminton","S",1,1,0,100,"O",2,32,"E"));
        displayData.Participants.AddRange([new(id+"-A",id,"Alice "+id,new(2000,1,1),"F","a@example.com","1"),new(id+"-B",id,"Beth "+id,new(2000,1,1),"F","b@example.com","2")]);
        displayData.Entries.AddRange([new(id+"-EA",id,id+"-P",[id+"-A"]),new(id+"-EB",id,id+"-P",[id+"-B"])]);
        displayData.ProgramPlans.Add(new(){ProgramId=id+"-P",Phase="K",LastRound=1});
    }
    TournamentMatch DisplayMatch(string id,string ev,int court,string status,DateOnly date,int hour)=>new(){Id=id,ProgramId=ev+"-P",Number=hour,Phase="K",Round=1,EntryA=ev+"-EA",EntryB=ev+"-EB",Status=status,WinnerId=status=="C"?ev+"-EA":null,Court=court,Start=date.ToDateTime(new(hour,0)),End=date.ToDateTime(new(hour,30)),ActualFinishedAt=status=="C"?date.ToDateTime(new(hour,20)):null,Games=status=="C"?[new(21,9),new(21,10)]:[],Live=new(){A=3,B=2},OfficialReason="Private official reason"};
    void AddDisplay(TournamentMatch m)=>displayData.ProgramPlans.Single(p=>p.ProgramId==m.ProgramId).Matches.Add(m);
    var earlier=DisplayMatch("earlier","E",1,"C",day,10);var latest=DisplayMatch("latest","E",1,"C",day,11);latest.WinnerId="E-EB";
    var walkover=DisplayMatch("walkover","E",2,"WO",day,9);walkover.WinnerId="E-EB";
    AddDisplay(earlier);AddDisplay(latest);AddDisplay(walkover);
    AddDisplay(DisplayMatch("prior-day","E",3,"C",day.AddDays(-1),11));
    AddDisplay(DisplayMatch("past-event","OLD",4,"C",day,11));
    AddDisplay(DisplayMatch("future-event","FUTURE",5,"C",day,11));
    var unscheduled=DisplayMatch("no-slot","E",6,"C",day,11);unscheduled.Start=null;AddDisplay(unscheduled);
    AddDisplay(DisplayMatch("bye","E",7,"BY",day,11));
    var jsonOptions=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    System.Text.Json.JsonElement[] DisplayRows(DateTimeOffset? at=null)=>System.Text.Json.JsonSerializer.SerializeToElement(LiveEndpoints.Display(displayData,at??now),jsonOptions).GetProperty("matches").EnumerateArray().ToArray();
    var unchanged=System.Text.Json.JsonSerializer.Serialize(displayData);var rows=DisplayRows();
    Check(rows.Select(r=>r.GetProperty("id").GetString()).SequenceEqual(new[]{"latest","walkover"}),"display selects latest result per court and excludes prior-day, unrelated-date events, unscheduled and BYE rows");
    Check(rows[0].GetProperty("winner").GetString()=="B","display uses authoritative official winner rather than game counts");
    Check(rows[1].GetProperty("winner").GetString()=="B"&&rows[1].GetProperty("games").GetArrayLength()==0,"walkover retains winner without fabricated game scores");
    Check(System.Text.Json.JsonSerializer.Serialize(displayData)==unchanged,"display projection never stores history or changes match data");
    Check(!System.Text.Json.JsonSerializer.Serialize(LiveEndpoints.Display(displayData,now)).Contains("Private official reason"),"display does not expose private result reasons");
    var waiting=DisplayMatch("waiting","OTHER",1,"SC",day,12);AddDisplay(waiting);
    displayData.Venue.Bindings.Add(new(){MatchId=waiting.Id,Court=1});
    Check(DisplayRows()[0].GetProperty("id").GetString()=="latest","waiting call leaves last confirmed result visible until scoring starts");
    waiting.Status="IP";
    Check(DisplayRows()[0].GetProperty("id").GetString()=="waiting","playing match from another event takes priority on the same physical court");
    waiting.Status="RC";
    Check(DisplayRows()[0].GetProperty("status").GetString()=="RC","submitted match keeps priority over the previous confirmed result");
    waiting.Status="SC";
    var overnight=DisplayMatch("overnight","OLD",8,"IP",day.AddDays(-1),23);AddDisplay(overnight);
    Check(DisplayRows().Last().GetProperty("id").GetString()=="overnight","already playing overnight matches stay visible despite completed-result day/event filters");
    Check(DisplayRows(now.AddHours(10)).Select(r=>r.GetProperty("id").GetString()).SequenceEqual(new[]{"overnight"}),"completed results expire at venue midnight, not UTC midnight");
    displayData.ProgramPlans.Single(p=>p.ProgramId=="E-P").Matches.Remove(latest);
    Check(DisplayRows()[0].GetProperty("id").GetString()=="earlier","deleting the latest result removes it immediately and exposes only the earlier valid result");
    try{FixtureResets.ResetSchedule(displayData,new(0,"E",day));Check(false,"completed display result reset");}catch(RuleException){checks++;}
    FixtureResets.ResetResult(displayData,earlier);FixtureResets.ResetResult(displayData,walkover);
    FixtureResets.ResetSchedule(displayData,new(0,"E",day));
    Check(DisplayRows().Select(r=>r.GetProperty("id").GetString()).SequenceEqual(new[]{"overnight"}),"schedule reset removes completed cards without retaining a cached winner");
}
Console.WriteLine($"PASS: {checks} including audience completed-result display checks");

// Warm-up is a saved rule only; the running countdown stays on the tablet.
{
    var legacy=System.Text.Json.JsonSerializer.Deserialize<BadmintonRules>("{}")!;
    Check(legacy.WarmupSeconds==120,"legacy rules default to two-minute warm-up");
    foreach(var seconds in new[]{0,1,120,600})
    {
        var rules=new BadmintonRules{WarmupSeconds=seconds};TournamentRules.ValidateRules(rules);
        Check(GamePresets.Copy(rules).WarmupSeconds==seconds,"preset copy retains warm-up seconds: "+seconds);
    }
    foreach(var seconds in new[]{-1,601})
    {
        try{TournamentRules.ValidateRules(new(){WarmupSeconds=seconds});Check(false,"invalid warm-up accepted: "+seconds);}
        catch(RuleException){checks++;}
    }
}
Console.WriteLine($"PASS: {checks} including configurable warm-up checks");
checks+=PlannerChecks.Run();
