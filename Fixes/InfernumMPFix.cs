using System;
using System.Collections.Generic;
using System.Reflection;
using CalamityMod.Projectiles.Boss;
using CalWotGMPFix.Common;
using InfernumMode.Content.BehaviorOverrides.BossAIs.AdultEidolonWyrm;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Providence;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Yharon;
using InfernumMode.Core.GlobalInstances;
using InfernumMode.Core.GlobalInstances.Systems;
using InfernumMode;
using InfernumMode.Core.OverridingSystem;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Infernum Mode multiplayer fixes.
//
// 1. Duplicate projectiles. Infernum boss AI runs on the server AND on every client. Most attacks wrap their
//    projectile spawns in "if (Main.netMode != NetmodeID.MultiplayerClient)", but ~25 don't (Plantera seeds,
//    Queen Bee bees, Empress lances, Hive Mind clots, SCal darts, Ravager pillars, Twins fireballs, ...).
//    On a client those calls create a client-owned copy that is synced to everyone, so with a host and one
//    friend each of those attacks fires three times. ClientSpawnBlocker stops a client from spawning Infernum's
//    (EntitySource_WorldEvent) projectiles while it is running a server-owned Infernum boss or projectile;
//    the server's copy still arrives as normal.
//
// 2. Ghost enemies. Same cause: King Slime's death slimes/Ninja, Duke Fishron's Sharkrons and the Profaned
//    Guardians' healer crystal are spawned by code that also runs on clients, giving each client its own
//    unsynced copy that only it can see.
//
// 3. Crash guard. Several Infernum AIs throw on clients when synced data hasn't arrived yet (e.g. Golem
//    indexing Main.npc[-1], King Slime's DeathSlash removing from an empty list). An exception in the update
//    loop can take the game or the server down, which kicks everyone. This catches and logs those instead.
[JITWhenModsEnabled("InfernumMode")]
public static class InfernumMPFix
{
    public const string DuplicateFixName = "Infernum duplicate projectile fix";
    public const string GhostNPCFixName = "Infernum ghost enemy fix";
    public const string CrashGuardName = "Infernum crash guard";
    public const string ProvidenceArenaName = "Infernum Providence arena in MP";

    private static Mod infernum;
    private static readonly HashSet<int> clientOnlyTypes = [];
    private static readonly Dictionary<string, int> caughtErrors = [];

    public static void Apply(Mod mod)
    {
        infernum = ModLoader.GetMod("InfernumMode");

        // --- 1. Duplicate projectiles + 2. ghost NPCs (rules for ClientSpawnBlocker) ---
        ClientSpawnBlocker.RegisterPendingSpawnAction(typeof(ProjectileSpawnManagementSystem), "preSyncAction");
        ClientSpawnBlocker.AddProjectileRule(ShouldBlockProjectile);
        ClientSpawnBlocker.AddNPCRule(ShouldBlockNPC);
        CalWotGMPFix.MarkFix(DuplicateFixName, true);
        CalWotGMPFix.MarkFix(GhostNPCFixName, true);

        // --- 3. Crash guard ---
        bool guardApplied = false;
        try
        {
            MethodInfo npcPreAI = typeof(GlobalNPCOverrides).GetMethod(nameof(GlobalNPCOverrides.PreAI), BindingFlags.Public | BindingFlags.Instance, [typeof(NPC)]);
            MonoModHooks.Add(npcPreAI, GuardNPCPreAI);
            On_Projectile.AI += GuardProjectileAI;
            guardApplied = true;
        }
        catch (Exception e)
        {
            mod.Logger.Error("Infernum crash guard failed to apply.", e);
        }
        CalWotGMPFix.MarkFix(CrashGuardName, guardApplied);

        // --- 4. Providence arena (InfernumProvidenceArenaPlayer) ---
        CalWotGMPFix.MarkFix(ProvidenceArenaName, true);
    }

    // Called after content loads, when projectile IDs exist.
    public static void PostSetupContent()
    {
        // Projectiles Infernum deliberately spawns only on one client (visual effects for the targeted player).
        clientOnlyTypes.Clear();
        clientOnlyTypes.Add(ModContent.ProjectileType<YharonFlameExplosion>());
        clientOnlyTypes.Add(ModContent.ProjectileType<HorizontalRayTerminus>());
        clientOnlyTypes.Add(ModContent.ProjectileType<StrongProfanedCrack>());
        clientOnlyTypes.Add(ModContent.ProjectileType<FlareBomb>());
    }

    public static void Unload()
    {
        infernum = null;
        caughtErrors.Clear();
    }

    public static bool IsInfernumNPC(NPC npc) =>
        npc.ModNPC?.Mod == infernum || (InfernumMode.InfernumMode.CanUseCustomAIs && NPCBehaviorOverride.Registered(npc.type));

    public static bool IsInfernumProjectile(Projectile projectile) =>
        projectile.ModProjectile?.Mod == infernum || (InfernumMode.InfernumMode.CanUseCustomAIs && ProjectileBehaviorOverride.Registered(projectile.type));

    // True while this client is running a server-owned Infernum boss or projectile.
    private static bool InServerOwnedInfernumCode()
    {
        NPC npc = ClientSpawnBlocker.UpdatingNPC;
        if (npc is not null)
            return IsInfernumNPC(npc); // Clients never own NPCs.

        Projectile projectile = ClientSpawnBlocker.UpdatingProjectile;
        return projectile is not null && projectile.owner != Main.myPlayer && IsInfernumProjectile(projectile);
    }

    private static bool ShouldBlockProjectile(IEntitySource source, int type)
    {
        if (!(MPFixConfig.Instance?.EnableInfernumDuplicateFix ?? false))
            return false;

        // Infernum's NewProjectileBetter always uses a bare EntitySource_WorldEvent.
        if (source is not EntitySource_WorldEvent { Context: null } || clientOnlyTypes.Contains(type))
            return false;

        return InServerOwnedInfernumCode();
    }

    // A client never legitimately spawns an NPC in multiplayer: it would exist only on that client.
    private static bool ShouldBlockNPC(IEntitySource source, int type) =>
        (MPFixConfig.Instance?.EnableInfernumDuplicateFix ?? false) && InServerOwnedInfernumCode();

    // ---------------- Crash guard ----------------

    private static bool GuardNPCPreAI(Func<GlobalNPCOverrides, NPC, bool> orig, GlobalNPCOverrides self, NPC npc)
    {
        if (!(MPFixConfig.Instance?.EnableInfernumCrashGuard ?? false))
            return orig(self, npc);

        // Golem stores its fists/heads as NPC indices in ExtraAI[0..3]. On a client those can be -1 until the
        // server's sync arrives, and Golem then reads Main.npc[-1] every frame (seen in your Feb 2025 logs).
        if (npc.type == NPCID.Golem && Main.netMode == NetmodeID.MultiplayerClient && InfernumMode.InfernumMode.CanUseCustomAIs && !RepairGolemParts(npc))
            return false;

        try
        {
            return orig(self, npc);
        }
        catch (Exception e)
        {
            Report($"NPC {Lang.GetNPCNameValue(npc.type)} (type {npc.type})", e);
            return false; // Skip this frame's AI; on clients the server's next sync corrects the state.
        }
    }

    private static bool RepairGolemParts(NPC golem)
    {
        float[] extraAI = golem.Infernum().ExtraAI;
        int[][] partTypes =
        [
            [NPCID.GolemFistLeft, ModContent.NPCType<InfernumMode.Content.BehaviorOverrides.BossAIs.Golem.GolemFistLeft>()],
            [NPCID.GolemFistRight, ModContent.NPCType<InfernumMode.Content.BehaviorOverrides.BossAIs.Golem.GolemFistRight>()],
            [NPCID.GolemHead],
            [NPCID.GolemHeadFree],
        ];

        for (int slot = 0; slot < partTypes.Length; slot++)
        {
            int index = (int)extraAI[slot];
            if (index >= 0 && index < Main.maxNPCs)
                continue;

            int found = -1;
            foreach (int type in partTypes[slot])
            {
                found = NPC.FindFirstNPC(type);
                if (found >= 0)
                    break;
            }

            if (found < 0)
                return false; // Part hasn't synced to this client yet; skip AI this frame.
            extraAI[slot] = found;
        }
        return true;
    }

    private static void GuardProjectileAI(On_Projectile.orig_AI orig, Projectile self)
    {
        if (!(MPFixConfig.Instance?.EnableInfernumCrashGuard ?? false) || !IsInfernumProjectile(self))
        {
            orig(self);
            return;
        }

        try
        {
            orig(self);
        }
        catch (Exception e)
        {
            Report($"Projectile {self.ModProjectile?.Name ?? self.type.ToString()}", e);
        }
    }

    private static void Report(string where, Exception e)
    {
        string side = Main.netMode == NetmodeID.Server ? "SERVER" : Main.netMode == NetmodeID.MultiplayerClient ? "CLIENT" : "SP";
        string key = $"{where}|{e.GetType().Name}|{e.TargetSite?.Name}";
        caughtErrors.TryGetValue(key, out int count);
        caughtErrors[key] = count + 1;

        // Full stack trace the first time, then every 600th repeat so the log doesn't flood.
        if (count == 0)
            CalWotGMPFix.Instance.Logger.Error($"[InfernumGuard] {side} caught exception in {where}. The game kept running.", e);
        else if (count % 600 == 0)
            CalWotGMPFix.Instance.Logger.Warn($"[InfernumGuard] {side} {where}: {e.GetType().Name} repeated {count} times.");
    }
}

// Infernum keeps the player inside Providence's arena, but only when Main.netMode == SinglePlayer, so in
// multiplayer nobody is held in. Here each client holds in its own player, using the arena rectangle the
// server already syncs (npc.Infernum().Arena), under the same "within 9600 units of Providence" rule.
[JITWhenModsEnabled("InfernumMode")]
public class InfernumProvidenceArenaPlayer : ModPlayer
{
    public override bool IsLoadingEnabled(Mod mod) => ModLoader.HasMod("InfernumMode");

    public override void PostUpdate()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || Player.whoAmI != Main.myPlayer || Player.dead)
            return;
        if (!(MPFixConfig.Instance?.EnableProvidenceArena ?? false) || !InfernumMode.InfernumMode.CanUseCustomAIs)
            return;

        int providenceIndex = CalamityMod.NPCs.CalamityGlobalNPC.holyBoss;
        if (providenceIndex < 0 || providenceIndex >= Main.maxNPCs)
            return;

        NPC providence = Main.npc[providenceIndex];
        if (!providence.active || providence.type != ModContent.NPCType<CalamityMod.NPCs.Providence.Providence>() || !NPCBehaviorOverride.Registered(providence.type))
            return;

        Microsoft.Xna.Framework.Rectangle arena = providence.Infernum().Arena;
        if (arena.IsEmpty || InfernumMode.Core.GlobalInstances.Systems.WorldSaveSystem.ProvidenceArena.IsEmpty || !providence.WithinRange(Player.Center, 9600f))
            return;

        if (Player.position.X < arena.Left)
            Player.position.X = arena.Left;
        if (Player.position.X + Player.width > arena.Right)
            Player.position.X = arena.Right - Player.width;
        if (Player.position.Y < arena.Top)
            Player.position.Y = arena.Top;
        if (Player.position.Y + Player.height > arena.Bottom)
            Player.position.Y = arena.Bottom - Player.height;
    }
}
