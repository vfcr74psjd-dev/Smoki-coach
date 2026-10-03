namespace Sm0kiSoloCoach;

public sealed class GameSnapshot
{
    public string PlayerName { get; set; } = "";
    public string Team { get; set; } = "";
    public string Map { get; set; } = "";
    public int? Round { get; set; }
    public string RoundPhase { get; set; } = "";
    public string RoundWinTeam { get; set; } = "";
    public int? CtScore { get; set; }
    public int? TScore { get; set; }
    public int? Health { get; set; }
    public int? Armor { get; set; }
    public bool? Helmet { get; set; }
    public int? Money { get; set; }
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    public string Weapon { get; set; } = "";
    public string PrimaryWeapon { get; set; } = "";
    public string BombState { get; set; } = "";
    public float? BombPositionX { get; set; }
    public float? BombPositionY { get; set; }
    public float? BombPositionZ { get; set; }
    public bool HasBombPosition =>
        BombPositionX.HasValue &&
        BombPositionY.HasValue &&
        BombPositionZ.HasValue;
    public float? PositionX { get; set; }
    public float? PositionY { get; set; }
    public float? PositionZ { get; set; }
    public bool HasPosition => PositionX.HasValue && PositionY.HasValue && PositionZ.HasValue;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class RoundRecord
{
    public int Round { get; set; }
    public string Side { get; set; } = "";
    public int KillsTotal { get; set; }
    public int DeathsTotal { get; set; }
    public int MoneyEnd { get; set; }
    public int KillsRound { get; set; }
    public int DeathsRound { get; set; }
    public bool? Won { get; set; }
    public bool Survived { get; set; }
    public string WeaponEnd { get; set; } = "";
    public string Intent { get; set; } = "";
    public string PositionPlan { get; set; } = "";
    public bool BombPlanted { get; set; }
    public string BombSite { get; set; } = "";
    public double? BombPlantSeconds { get; set; }
}
