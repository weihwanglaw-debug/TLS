namespace LiveScore.Api;

public sealed class ResultDetails
{
    public int? ShuttlesUsed { get; set; }
    public string Remark { get; set; } = "";
    public List<MatchCard> Cards { get; set; } = [];
    public List<MatchChallenge> Challenges { get; set; } = [];
}
public sealed record MatchCard(string ParticipantId,string Type,int Game,string Remark="");
public sealed record MatchChallenge(string Side,int Game,string Outcome);
public sealed record ResultCorrection(DateTimeOffset At,string Reason,List<GameScore> PreviousGames,string? PreviousWinner,ResultDetails PreviousDetails);

public static class ResultValidation
{
    public static void Details(SetupData s,TournamentMatch m,ResultDetails details)
    {
        TournamentRules.Need(details is not null&&details.ShuttlesUsed is null or >=0 and <=999,"Shuttle usage must be blank or 0–999.");
        TournamentRules.Need(details.Remark is not null&&details.Remark.Length<=2000,"Remark must be at most 2000 characters.");
        TournamentRules.Need(details.Cards is {Count:<=100}&&details.Challenges is {Count:<=100},"Too many card or challenge records.");
        var players=TournamentRules.Players(s,m);var maxGames=m.Rules.GamesToWin*2-1;
        foreach(var card in details.Cards)
            TournamentRules.Need(card is not null&&players.Contains(card.ParticipantId)&&card.Type is "Y" or "R" or "B"&&card.Game>=1&&card.Game<=maxGames&&card.Remark is not null&&card.Remark.Length<=300,"Choose a match player, card type and valid game number.");
        foreach(var challenge in details.Challenges)
            TournamentRules.Need(challenge is not null&&challenge.Side is "A" or "B"&&challenge.Game>=1&&challenge.Game<=maxGames&&challenge.Outcome is "S" or "U" or "C","Choose a valid challenge side, game and outcome.");
        TournamentRules.Need(details.Challenges.GroupBy(c=>(c.Side,c.Game)).All(g=>g.Count(c=>c.Outcome=="U")<=m.Rules.ChallengesPerGame),"Unsuccessful challenges exceed the saved allowance for a game.");
    }
    public static ResultDetails Copy(ResultDetails details)=>System.Text.Json.JsonSerializer.Deserialize<ResultDetails>(System.Text.Json.JsonSerializer.Serialize(details,JsonDefaults.Options),JsonDefaults.Options)!;
}
