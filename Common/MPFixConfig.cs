using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CalWotGMPFix.Common;

public enum BossTargeting
{
    InfernumDefault, // stick to one player until they die
    Alternate,       // next player each attack
    ClassWeighted    // random each attack, weighted by the class each player is using
}

// Server-side so the host's settings are synced to everyone who joins.
public class MPFixConfig : ModConfig
{
    public static MPFixConfig Instance => Terraria.ModLoader.ModContent.GetInstance<MPFixConfig>();

    public override ConfigScope Mode => ConfigScope.ServerSide;

    [Header("BugFixes")]
    [DefaultValue(true)]
    public bool EnableFrostScreenSmash { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableShockwavePush { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableInfernumDuplicateFix { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableInfernumCrashGuard { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableProvidenceArena { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableWotGDuplicateFix { get; set; } = true;

    [DefaultValue(true)]
    public bool EnableWotGSyncSmoothing { get; set; } = true;

    [Header("Gameplay")]
    [DefaultValue(true)]
    public bool EnableSharedHazards { get; set; } = true;

    [DefaultValue(BossTargeting.ClassWeighted)]
    [DrawTicks]
    public BossTargeting TargetingMode { get; set; } = BossTargeting.ClassWeighted;

    // Relative chances for ClassWeighted. With a melee (7) and a summoner (3) player, melee gets 70% of attacks.
    [Range(0, 20)] [DefaultValue(7)] [Slider]
    public int MeleeWeight { get; set; } = 7;

    [Range(0, 20)] [DefaultValue(5)] [Slider]
    public int RangedWeight { get; set; } = 5;

    [Range(0, 20)] [DefaultValue(4)] [Slider]
    public int MagicWeight { get; set; } = 4;

    [Range(0, 20)] [DefaultValue(5)] [Slider]
    public int RogueWeight { get; set; } = 5;

    [Range(0, 20)] [DefaultValue(3)] [Slider]
    public int SummonerWeight { get; set; } = 3;

    [Range(0, 20)] [DefaultValue(5)] [Slider]
    public int OtherClassWeight { get; set; } = 5;

    // 0 = unlimited (normal respawning). Otherwise, once you've died this many times during a boss fight
    // you stay dead and spectate until the fight ends.
    [Range(0, 20)] [DefaultValue(1)] [Slider]
    public int BossFightLives { get; set; } = 1;

    // Replace each mod's own multiplayer boss health scaling with: single-player health * (1 + X% per extra player).
    [DefaultValue(true)]
    public bool OverrideBossHealthScaling { get; set; } = true;

    [Range(0, 200)] [Increment(5)] [DefaultValue(50)] [Slider]
    public int BossHealthPerExtraPlayer { get; set; } = 50;

    [Header("Diagnostics")]
    [DefaultValue(true)]
    public bool EnableBossTrace { get; set; } = true;
}

// Per-player settings (each player's own choice).
public class MPFixClientConfig : ModConfig
{
    public static MPFixClientConfig Instance => Terraria.ModLoader.ModContent.GetInstance<MPFixClientConfig>();

    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(true)]
    public bool SpectateWhenDead { get; set; } = true;

    [DefaultValue(true)]
    public bool ShowTargetMarker { get; set; } = true;
}
