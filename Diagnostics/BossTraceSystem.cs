using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CalWotGMPFix.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Diagnostics;

// Once a second, logs every active boss on this machine to client.log / server.log as a "[BossTrace]" line.
// After a test session, line up the host's server.log with each player's client.log by time:
// if position, life or attack state disagree for the same boss, that's a desync worth fixing.
public class BossTraceSystem : ModSystem
{
    private static readonly Dictionary<Type, PropertyInfo> stateProperties = [];
    private int lastLoggedSecond = -1;

    public override void PostUpdateNPCs()
    {
        if (!(MPFixConfig.Instance?.EnableBossTrace ?? false) || Main.netMode == NetmodeID.SinglePlayer)
            return;

        int second = (int)(Main.GameUpdateCount / 60);
        if (second == lastLoggedSecond)
            return;
        lastLoggedSecond = second;

        string side = Main.netMode == NetmodeID.Server ? "SERVER" : $"CLIENT({Main.LocalPlayer?.name})";
        string time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        foreach (NPC npc in Main.ActiveNPCs)
        {
            // Skip worm body/tail segments (DoG's body is flagged as a boss); the head is logged.
            if (!npc.boss || (npc.realLife >= 0 && npc.realLife != npc.whoAmI))
                continue;

            string name = npc.ModNPC is null ? $"Terraria/{NPCID.Search.GetName(npc.type)}" : $"{npc.ModNPC.Mod.Name}/{npc.ModNPC.Name}";
            string ai = string.Join(",", npc.ai.Select(f => f.ToString("0.##", CultureInfo.InvariantCulture)));
            CalWotGMPFix.Instance.Logger.Info(
                $"[BossTrace] {time} {side} {name} slot={npc.whoAmI} life={npc.life}/{npc.lifeMax} " +
                $"pos={Fmt(npc.Center)} vel={Fmt(npc.velocity)} target={npc.target} ai=[{ai}] state={GetState(npc)}" +
                (hasInfernum ? InfernumTraceHelper.ExtraAI(npc) : ""));
        }

        if (ClientSpawnBlocker.BlockedProjectiles > 0 || ClientSpawnBlocker.BlockedNPCs > 0)
        {
            CalWotGMPFix.Instance.Logger.Info($"[BossTrace] {time} {side} blocked {ClientSpawnBlocker.BlockedProjectiles} duplicate projectile(s) and {ClientSpawnBlocker.BlockedNPCs} ghost NPC(s) this second");
            ClientSpawnBlocker.BlockedProjectiles = 0;
            ClientSpawnBlocker.BlockedNPCs = 0;
        }
    }

    private static bool hasInfernum => ModLoader.HasMod("InfernumMode");

    private static string Fmt(Vector2 v) => $"({v.X.ToString("0", CultureInfo.InvariantCulture)},{v.Y.ToString("0", CultureInfo.InvariantCulture)})";

    // WotG bosses use Luminance state machines with a public CurrentState property; log it if present.
    private static string GetState(NPC npc)
    {
        if (npc.ModNPC is null)
            return "-";

        Type type = npc.ModNPC.GetType();
        if (!stateProperties.TryGetValue(type, out PropertyInfo prop))
        {
            prop = type.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance);
            stateProperties[type] = prop;
        }

        try
        {
            object value = prop?.GetValue(npc.ModNPC);
            return value?.ToString() ?? "-";
        }
        catch
        {
            return "?";
        }
    }
}

[JITWhenModsEnabled("InfernumMode")]
internal static class InfernumTraceHelper
{
    // First 12 Infernum ExtraAI slots: attack timers/sub-states live here for most reworked bosses.
    public static string ExtraAI(NPC npc)
    {
        if (!InfernumMode.InfernumMode.CanUseCustomAIs || !InfernumMode.Core.OverridingSystem.NPCBehaviorOverride.Registered(npc.type))
            return "";
        float[] extra = InfernumMode.Utilities.Infernum(npc).ExtraAI;
        return " infernumAI=[" + string.Join(",", extra.Take(12).Select(f => f.ToString("0.##", CultureInfo.InvariantCulture))) + "]";
    }
}
