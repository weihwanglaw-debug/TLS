using LiveScore.Api;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
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
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddMinutes(30)),TimeOnly.FromDateTime(current.AddHours(2)),[2])];StartCheck(true,false,"early start must use allocated court");
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddMinutes(-30)),TimeOnly.FromDateTime(current.AddMinutes(20)),[2]),new(today,TimeOnly.FromDateTime(current.AddMinutes(30)),TimeOnly.FromDateTime(current.AddHours(2)),[1])];StartCheck(true,false,"early confirmation cannot bypass active-session court restrictions");
startPlan.Sessions.RemoveAt(0);startMatch.PendingA=true;StartCheck(true,false,"early start cannot bypass prerequisite results");startMatch.PendingA=false;
var busyMatch=new TournamentMatch{Id="BUSY",ProgramId="P",EntryA="C",EntryB="D",Status="IP",Court=1};startData.ProgramPlans[0].Matches.Add(busyMatch);StartCheck(true,false,"occupied court blocks early start");busyMatch.Court=2;busyMatch.UmpireId="U";StartCheck(true,false,"occupied umpire blocks early start");startData.ProgramPlans[0].Matches.Remove(busyMatch);
startMatch.Start=startMatch.Start!.Value.AddDays(1);startMatch.End=startMatch.End!.Value.AddDays(1);StartCheck(true,false,"tomorrow cannot start today");
startMatch.Start=current.AddMinutes(-30);startMatch.End=current.AddMinutes(10);StartCheck(false,false,"late start still requires active session");
startPlan.Sessions=[new(today,TimeOnly.FromDateTime(current.AddHours(-1)),TimeOnly.FromDateTime(current.AddHours(2)),[1])];StartCheck(false,true,"normal start needs no early confirmation");
startData.Events=[new("E","Event","Hall",today.AddDays(1),today.AddDays(2))];StartCheck(true,false,"event date restriction remains");
Console.WriteLine($"PASS: {checks} total focused rules and early-start checks");

var courtData=new SetupData{CourtCount=3};
var courtPlan=new EventPlan{Sessions=[morning,afternoon,new(new DateOnly(2030,1,2),new TimeOnly(9,0),new TimeOnly(13,0),[3])]};
var queued=new TournamentMatch{Court=2,Start=new DateTime(2030,1,1,14,0,0)};
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,8,0,0)).SequenceEqual(new[]{1,2}),"prepare upcoming session courts");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).SequenceEqual(new[]{1}),"active session takes precedence over planned courts");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,13,0,0)).SequenceEqual(new[]{1,2}),"session end is exclusive and upcoming preparation remains allowed");
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,18,0,0)).Length==0,"expired session cannot receive new calls");
courtData.Venue.ClosedCourts=[2];Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,14,0,0)).SequenceEqual(new[]{1}),"closed courts excluded from shared eligibility");
queued.CourtExceptionCourt=3;queued.CourtExceptionUntil=new(2030,1,1,13,0,0);queued.CourtExceptionReason="Existing authorised exception";
Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).SequenceEqual(new[]{1,3}),"legacy explicit exception retains its target court");
queued.Start=new(2030,1,2,10,0,0);Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,14,0,0)).SequenceEqual(new[]{3}),"future-day preparation uses planned session allocation");
queued.Start=null;Check(SessionCourts.EligibleCourts(courtData,courtPlan,queued,new(2030,1,1,10,0,0)).Length==0,"unscheduled match requires scheduling");
Console.WriteLine($"PASS: {checks} total focused rules checks");
