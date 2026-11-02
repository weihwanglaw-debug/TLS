namespace TLS.Data;

public partial class SbaRanking
{
    public int SbaRankingId { get; set; }
    public string RankingType { get; set; } = null!;
    public string Player1SbaId { get; set; } = null!;
    public string Player1Name { get; set; } = null!;
    public string? Player1Club { get; set; }
    public DateOnly? Player1DateOfBirth { get; set; }
    public string? Player2SbaId { get; set; }
    public string? Player2Name { get; set; }
    public string? Player2Club { get; set; }
    public DateOnly? Player2DateOfBirth { get; set; }
    public int? YearOfBirth { get; set; }
    public int AccumulatedScore { get; set; }
    public int Ranking { get; set; }
    public int Tournaments { get; set; }
    public DateTime UpdatedAt { get; set; }
}



public class BadmintonClub
{
    public int     ClubId        { get; set; }
    public string  Name          { get; set; } = null!;
    public string? ContactNumber { get; set; }
    public string? Email         { get; set; }
    public string? Address       { get; set; }
    public string? Country       { get; set; }
    public bool    IsActive      { get; set; }
    public DateTime CreatedAt    { get; set; }
    public DateTime? UpdatedAt   { get; set; }
}

