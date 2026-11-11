using System;
using CalWotGMPFix.Common;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace CalWotGMPFix.Features;

// Replaces each mod's own multiplayer boss health scaling with one simple rule:
//   boss health = single-player health * (1 + X% per extra player)
// "Single-player health" is worked out by spawning a throwaway copy of the boss with the player count forced to 1,
// so everything else (expert/master, Revengeance/Death, Infernum, mod configs) is kept exactly as in singleplayer.
// The server and each client compute the same value from the synced player count, so they always agree.
public class BossHealthScaling : GlobalNPC
{
    [ThreadStatic] private static bool computingBaseline;

    // Worm bosses spawn dozens of segments in one go; reuse the single-player value for a few seconds.
    private static readonly System.Collections.Generic.Dictionary<int, (int life, uint tick)> baselineCache = [];

    public override void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment)
    {
        if (computingBaseline || numPlayers <= 1 || !npc.boss)
            return;

        MPFixConfig config = MPFixConfig.Instance;
        if (config is null || !config.OverrideBossHealthScaling)
            return;

        int singlePlayerLife;
        if (baselineCache.TryGetValue(npc.type, out var cached) && Main.GameUpdateCount - cached.tick < 600)
        {
            npc.lifeMax = Scale(cached.life, numPlayers, config);
            return;
        }

        computingBaseline = true;
        try
        {
            NPC sample = new();
            sample.SetDefaults(npc.type, new NPCSpawnParams { playerCountForMultiplayerDifficultyOverride = 1 });
            singlePlayerLife = sample.lifeMax;
        }
        catch (Exception e)
        {
            Mod.Logger.Warn($"Could not work out single-player health for {Lang.GetNPCNameValue(npc.type)}; leaving its own scaling.", e);
            return;
        }
        finally
        {
            computingBaseline = false;
        }

        baselineCache[npc.type] = (singlePlayerLife, Main.GameUpdateCount);
        npc.lifeMax = Scale(singlePlayerLife, numPlayers, config);
    }

    private static int Scale(int singlePlayerLife, int numPlayers, MPFixConfig config)
    {
        float multiplier = 1f + config.BossHealthPerExtraPlayer / 100f * (numPlayers - 1);
        return Math.Max(1, (int)Math.Round(singlePlayerLife * multiplier));
    }

    public override void Unload() => baselineCache.Clear();
}
