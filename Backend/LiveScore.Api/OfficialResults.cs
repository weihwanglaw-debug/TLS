namespace LiveScore.Api;
public static class OfficialResults
{
    public static string Winner(TournamentMatch match,ResultRequest request)
    {
        if(request.Outcome is null)return request.WalkoverWinner??MatchResults.Winner(match,request.Games);
        TournamentRules.Need(request.Outcome is "N" or "W" or "R" or "D" or "M","Choose a valid official result type.");
        TournamentRules.Need(!string.IsNullOrWhiteSpace(request.Reason)&&request.Reason.Length<=300,"Enter a reason for the official result.");
        TournamentRules.Need(request.OfficialWinner==match.EntryA||request.OfficialWinner==match.EntryB&&match.EntryB is not null,"Choose an official winner from this match.");
        TournamentRules.Need(request.Games is not null&&request.Games.Count<=match.Rules.GamesToWin*2-1&&request.Games.All(g=>g is not null&&g.A>=0&&g.B>=0&&g.A<=match.Rules.PointCap&&g.B<=match.Rules.PointCap),"Enter valid scores within the saved score cap.");
        if(request.Outcome=="W")TournamentRules.Need(request.Games!.Count==0,"Walkover has no played game scores. Choose retirement for a match that started.");
        if(request.Outcome=="N")TournamentRules.Need(MatchResults.Winner(match,request.Games!)==request.OfficialWinner,"The calculated winner differs. Choose Manual / Admin decision to override it.");
        if(request.Outcome=="R")for(int i=0;i<request.Games!.Count-1;i++)TournamentRules.Need(MatchResults.GameWon(request.Games[i].A,request.Games[i].B,match.Rules),"Only the last retirement game may be unfinished.");
        return request.OfficialWinner!;
    }
}
