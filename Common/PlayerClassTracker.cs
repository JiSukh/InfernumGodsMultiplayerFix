using Terraria;
using Terraria.ModLoader;

namespace CalWotGMPFix.Common;

public enum PlayerClass { Melee, Ranged, Magic, Rogue, Summoner, Other }

// Works out which class a player is currently fighting as, from the weapon they last held. The held item is
// synced to the server, so this works there too. Remembering the last weapon means switching to a pickaxe or
// healing potion for a moment doesn't change your class.
public class PlayerClassTracker : ModPlayer
{
    public PlayerClass CurrentClass = PlayerClass.Other;

    public override void PostUpdate() => Refresh();

    private void Refresh()
    {
        Item held = Player.HeldItem;
        if (held is null || held.IsAir || held.damage <= 0 || held.DamageType is null)
            return;
        CurrentClass = Classify(held.DamageType);
    }

    public static PlayerClass Classify(DamageClass dc)
    {
        // Summon first: whips count as both summon and melee-speed.
        if (dc.CountsAsClass(DamageClass.Summon))
            return PlayerClass.Summoner;
        if (dc.Name.Contains("Rogue") || dc.FullName.Contains("Rogue"))
            return PlayerClass.Rogue;
        if (dc.CountsAsClass(DamageClass.Melee))
            return PlayerClass.Melee;
        if (dc.CountsAsClass(DamageClass.Ranged))
            return PlayerClass.Ranged;
        if (dc.CountsAsClass(DamageClass.Magic))
            return PlayerClass.Magic;
        return PlayerClass.Other; // Thorium bard/healer, generic, etc.
    }

    public static int WeightFor(Player player)
    {
        MPFixConfig c = MPFixConfig.Instance;
        PlayerClassTracker tracker = player.GetModPlayer<PlayerClassTracker>();
        tracker.Refresh();
        return tracker.CurrentClass switch
        {
            PlayerClass.Melee => c.MeleeWeight,
            PlayerClass.Ranged => c.RangedWeight,
            PlayerClass.Magic => c.MagicWeight,
            PlayerClass.Rogue => c.RogueWeight,
            PlayerClass.Summoner => c.SummonerWeight,
            _ => c.OtherClassWeight,
        };
    }
}
