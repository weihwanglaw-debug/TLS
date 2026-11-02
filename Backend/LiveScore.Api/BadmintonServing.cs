namespace LiveScore.Api;

public sealed class ServingState
{
    public string ServerSide {get;set;}="A";
    public string RightA {get;set;}="";
    public string RightB {get;set;}="";
    public string LeftSide {get;set;}="A";
    public bool DecidingEndsChanged {get;set;}
}
public sealed record ServingSetup(string ServerSide,string ServerId,string ReceiverId,string LeftSide);
public sealed record RallyRecord(int Game,int BeforeA,int BeforeB,int A,int B,string WinnerSide,string ServerId,string ReceiverId,string LeftSide,DateTimeOffset At);
public static class BadmintonServing
{
    public static string[] Members(SetupData s,TournamentMatch m,string side)=>s.Entries.SingleOrDefault(e=>e.Id==(side=="A"?m.EntryA:m.EntryB))?.ParticipantIds??[];
    public static ServingState? Copy(ServingState? p)=>p is null?null:new(){ServerSide=p.ServerSide,RightA=p.RightA,RightB=p.RightB,LeftSide=p.LeftSide,DecidingEndsChanged=p.DecidingEndsChanged};
    public static string Other(string side)=>side=="A"?"B":"A";
    public static string Player(SetupData s,TournamentMatch m,string side,bool right)
    {
        var p=m.Live?.Serving;if(p is null)return "";
        var members=Members(s,m,side);var id=side=="A"?p.RightA:p.RightB;
        return right||members.Length==1?id:members.Single(x=>x!=id);
    }
    public static string Server(SetupData s,TournamentMatch m)=>m.Live?.Serving is not ServingState p?"":Player(s,m,p.ServerSide,(p.ServerSide=="A"?m.Live.A:m.Live.B)%2==0);
    public static string Receiver(SetupData s,TournamentMatch m)=>m.Live?.Serving is not ServingState p?"":Player(s,m,Other(p.ServerSide),(p.ServerSide=="A"?m.Live.A:m.Live.B)%2==0);
    public static void Setup(SetupData s,TournamentMatch m,ServingSetup? setup,bool next=false)
    {
        TournamentRules.Need(setup is not null,"Confirm the server, receiver and court ends before scoring.");
        TournamentRules.Need(setup.ServerSide is "A" or "B"&&setup.LeftSide is "A" or "B","Choose valid serving and left-court sides.");
        var live=m.Live!;var a=Members(s,m,"A");var b=Members(s,m,"B");
        TournamentRules.Need(a.Length is 1 or 2&&b.Length==a.Length,"Serving positions require matching singles or doubles entries.");
        var servers=setup.ServerSide=="A"?a:b;var receivers=setup.ServerSide=="A"?b:a;
        TournamentRules.Need(servers.Contains(setup.ServerId)&&receivers.Contains(setup.ReceiverId),"Select a server and receiver from opposite sides.");
        if(next)
        {
            var last=m.Games.Last();TournamentRules.Need(setup.ServerSide==(last.A>last.B?"A":"B"),"The previous game's winning side serves first.");
            if(live.Serving is not null)TournamentRules.Need(setup.LeftSide==Other(live.Serving.LeftSide),"Change court ends for the next game.");
        }
        var even=(setup.ServerSide=="A"?live.A:live.B)%2==0;
        string Right(string[] members,string selected)=>even||members.Length==1?selected:members.Single(x=>x!=selected);
        live.Serving=new(){ServerSide=setup.ServerSide,LeftSide=setup.LeftSide,RightA=Right(a,setup.ServerSide=="A"?setup.ServerId:setup.ReceiverId),RightB=Right(b,setup.ServerSide=="B"?setup.ServerId:setup.ReceiverId),DecidingEndsChanged=m.Games.Count==m.Rules.GamesToWin*2-2&&Math.Max(live.A,live.B)>=m.Rules.DecidingEndsAt};
    }
    public static void Point(SetupData s,TournamentMatch m,string winner)
    {
        var live=m.Live!;TournamentRules.Need(live.Serving is not null,"Confirm the current serving positions before adding a point.");
        var p=live.Serving;var server=Server(s,m);var receiver=Receiver(s,m);var beforeA=live.A;var beforeB=live.B;
        if(p.ServerSide==winner)
        {
            var members=Members(s,m,winner);
            if(members.Length==2){if(winner=="A")p.RightA=members.Single(id=>id!=p.RightA);else p.RightB=members.Single(id=>id!=p.RightB);}
        }
        p.ServerSide=winner;if(winner=="A")live.A++;else live.B++;
        live.Rallies.Add(new(m.Games.Count+1,beforeA,beforeB,live.A,live.B,winner,server,receiver,p.LeftSide,DateTimeOffset.UtcNow));
        if(m.Games.Count==m.Rules.GamesToWin*2-2&&!p.DecidingEndsChanged&&Math.Max(live.A,live.B)>=m.Rules.DecidingEndsAt)
        {p.LeftSide=Other(p.LeftSide);p.DecidingEndsChanged=true;}
        if(Math.Max(beforeA,beforeB)<m.Rules.IntervalAt&&Math.Max(live.A,live.B)>=m.Rules.IntervalAt)live.IntervalUntil=DateTimeOffset.UtcNow.AddSeconds(m.Rules.IntervalSeconds);
    }
}
