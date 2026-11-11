using System;
using System.Collections.Generic;
using System.Reflection;
using InfernumMode.Content.Buffs;
using CalWotGMPFix.Common;
using InfernumMode;
using InfernumMode.Content.BehaviorOverrides.BossAIs.BrimstoneElemental;
using InfernumMode.Content.BehaviorOverrides.BossAIs.CalamitasShadow;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Crabulon;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Cultist;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Polterghast;
using InfernumMode.Content.BehaviorOverrides.BossAIs.ProfanedGuardians;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Ravager;
using InfernumMode.Content.BehaviorOverrides.BossAIs.SupremeCalamitas;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Severity 4 (Loa chose "everyone"): several Infernum mechanics only act on the player the boss is targeting,
// because the code says "target.X = ...". In multiplayer, boss AI runs on every client, but only the
// targeted player's own game can move or hurt that player, so everyone else could ignore the mechanic.
//
// Each hook below runs Infernum's code unchanged, then (on a client whose player is NOT the target) applies
// the same rule to that client's own player, using the boss state the server already syncs.
[JITWhenModsEnabled("InfernumMode")]
public static class InfernumSharedMechanics
{
    public const string FixName = "Infernum hazards apply to every player";

    private delegate bool OrigPreAI<T>(T self, NPC npc);
    private delegate void OrigRoseBurst(NPC npc, Player target, bool pissedOff, ref float attackTimer, ref float frameType);
    private delegate void OrigThreeRefs(NPC npc, Player target, ref float a, ref float b, ref float c);
    private delegate void OrigClawSlam(NPC npc, Player target, ref float attackTimer, ref bool usingClaws, bool enraged);

    public static void Apply(Mod mod)
    {
        var applied = new List<string>();
        void Try(string name, Action hook)
        {
            try { hook(); applied.Add(name); }
            catch (Exception e) { mod.Logger.Error($"[{FixName}] could not hook {name}.", e); }
        }

        const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags Stat = BindingFlags.Public | BindingFlags.Static;

        Try("Cultist border", () => MonoModHooks.Add(typeof(CultistBehaviorOverride).GetMethod("PreAI", Inst, [typeof(NPC)]),
            (Func<OrigPreAI<CultistBehaviorOverride>, CultistBehaviorOverride, NPC, bool>)CultistBorder));
        Try("Ravager walls", () => MonoModHooks.Add(typeof(RavagerBodyBehaviorOverride).GetMethod("PreAI", Inst, [typeof(NPC)]),
            (Func<OrigPreAI<RavagerBodyBehaviorOverride>, RavagerBodyBehaviorOverride, NPC, bool>)RavagerWalls));
        Try("Profaned Guardians fire border", () => MonoModHooks.Add(typeof(AttackerGuardianBehaviorOverride).GetMethod("PreAI", Inst, [typeof(NPC)]),
            (Func<OrigPreAI<AttackerGuardianBehaviorOverride>, AttackerGuardianBehaviorOverride, NPC, bool>)GuardianFireBorder));
        Try("Brimstone Elemental rose ring", () => MonoModHooks.Add(typeof(BrimstoneElementalBehaviorOverride).GetMethod(nameof(BrimstoneElementalBehaviorOverride.DoBehavior_BrimstoneRoseBurst), Stat),
            (RoseBurstHook)RoseRing));
        Try("Calamitas Clone madness circle", () => MonoModHooks.Add(typeof(CalamitasShadowBehaviorOverride).GetMethod(nameof(CalamitasShadowBehaviorOverride.DoBehavior_ConvergingBookEnergy), Stat),
            (ThreeRefsHook)CalCloneMadness));
        Try("Polterghast madness circle", () => MonoModHooks.Add(typeof(PolterghastBehaviorOverride).GetMethod(nameof(PolterghastBehaviorOverride.DoBehavior_DesperationAttack), Stat),
            (ThreeRefsHook)PolterghastMadness));
        Try("Crabulon claw slam", () => MonoModHooks.Add(typeof(CrabulonBehaviorOverride).GetMethod(nameof(CrabulonBehaviorOverride.DoBehavior_ClawSlamMushroomWaves), Stat),
            (ClawSlamHook)CrabulonSlam));
        Try("SCal leave-arena kill", () => MonoModHooks.Add(typeof(SupremeCalamitasBehaviorOverride).GetMethod(nameof(SupremeCalamitasBehaviorOverride.DoBehavior_DesperationPhase), Stat),
            (ThreeRefsHook)SCalArenaKill));

        mod.Logger.Info($"[{FixName}] hooked: {string.Join(", ", applied)}");
        CalWotGMPFix.MarkFix(FixName, applied.Count == 8);
    }

    private delegate void RoseBurstHook(OrigRoseBurst orig, NPC npc, Player target, bool pissedOff, ref float attackTimer, ref float frameType);
    private delegate void ThreeRefsHook(OrigThreeRefs orig, NPC npc, Player target, ref float a, ref float b, ref float c);
    private delegate void ClawSlamHook(OrigClawSlam orig, NPC npc, Player target, ref float attackTimer, ref bool usingClaws, bool enraged);

    // The local player, if this is a multiplayer client whose player is in the fight but is not the boss's target.
    private static bool TryGetLocalNonTarget(NPC npc, out Player player)
    {
        player = null;
        if (Main.netMode != NetmodeID.MultiplayerClient || !(MPFixConfig.Instance?.EnableSharedHazards ?? false))
            return false;
        if (!npc.active || npc.target == Main.myPlayer)
            return false;

        Player local = Main.LocalPlayer;
        if (!local.active || local.dead || local.ghost)
            return false;

        player = local;
        return true;
    }

    // Cultist: players are kept inside a 3472-wide border around where the fight started, and hurt + pushed
    // back if they touch its edge (CultistBehaviorOverride.PreAI).
    private static bool CultistBorder(OrigPreAI<CultistBehaviorOverride> orig, CultistBehaviorOverride self, NPC npc)
    {
        bool result = orig(self, npc);
        if (npc.type != NPCID.CultistBoss || !TryGetLocalNonTarget(npc, out Player player))
            return result;

        float initialXPosition = npc.Infernum().ExtraAI[8];
        bool dying = npc.Infernum().ExtraAI[6] == 2f;
        if (initialXPosition == 0f || dying)
            return result;

        float left = initialXPosition - CultistBehaviorOverride.BorderWidth / 2f + 30f;
        float right = initialXPosition + CultistBehaviorOverride.BorderWidth / 2f - 30f;
        player.Center = Vector2.Clamp(player.Center, new Vector2(left + player.width * 0.5f, -100f), new Vector2(right - player.width * 0.5f, Main.maxTilesY * 16f + 100f));
        if (player.Center.X <= left + player.width * 0.5f + 16f || player.Center.X >= right - player.width * 0.5f - 16f)
        {
            player.velocity = (new Vector2(initialXPosition, player.Center.Y) - player.Center).SafeNormalize(Vector2.Zero) * 10f;
            player.Hurt(PlayerDeathReason.ByCustomReason($"{player.name} was repelled by celestial forces."), 200, 0);
        }
        return result;
    }

    // Ravager: in phase 2 two walls close the arena around where the target stood (ExtraAI[8] = arena centre X).
    private static bool RavagerWalls(OrigPreAI<RavagerBodyBehaviorOverride> orig, RavagerBodyBehaviorOverride self, NPC npc)
    {
        bool result = orig(self, npc);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return result;

        float arenaCenterX = npc.Infernum().ExtraAI[8];
        if (arenaCenterX == 0f)
            return result;

        float left = arenaCenterX - RavagerBodyBehaviorOverride.ArenaBorderOffset + 28f;
        float right = arenaCenterX + RavagerBodyBehaviorOverride.ArenaBorderOffset - 28f;
        player.Center = Vector2.Clamp(player.Center, new Vector2(left, -100f), new Vector2(right, Main.maxTilesY * 16f + 100f));
        return result;
    }

    // Profaned Guardians: while the fire border is visible, players further than 1250 from the commander are pulled back in.
    private static bool GuardianFireBorder(OrigPreAI<AttackerGuardianBehaviorOverride> orig, AttackerGuardianBehaviorOverride self, NPC npc)
    {
        bool result = orig(self, npc);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return result;

        float fireBorderOpacity = npc.Infernum().ExtraAI[GuardianComboAttackManager.FireBorderInterpolantIndex];
        if (fireBorderOpacity > 0f && player.Center.Distance(npc.Center) > 1250f)
            player.Center = Vector2.Lerp(player.Center, npc.Center + npc.Center.DirectionTo(player.Center) * 1250f, 0.2f);
        return result;
    }

    // Brimstone Elemental: once the rose ring has grown, touching its edge pushes you back inside and hurts.
    private static void RoseRing(OrigRoseBurst orig, NPC npc, Player target, bool pissedOff, ref float attackTimer, ref float frameType)
    {
        orig(npc, target, pissedOff, ref attackTimer, ref frameType);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return;

        float[] extraAI = npc.Infernum().ExtraAI;
        Vector2 circleCenter = new(extraAI[2], extraAI[3]);
        float circleRadius = extraAI[4];
        float ringRadius = BrimstoneElementalBehaviorOverride.RoseCircleRadius;
        if (circleCenter.X == 0f || circleRadius < ringRadius - 80f || player.WithinRange(circleCenter, circleRadius - 8f))
            return;

        player.Center = circleCenter + (player.Center - circleCenter).SafeNormalize(Vector2.Zero) * (ringRadius - 10f);
        player.Hurt(PlayerDeathReason.ByCustomReason($"{player.name} was violently pricked by roses."), Main.rand.Next(120, 135), 0);
    }

    // Calamitas Clone: during the book attack, leaving the black circle (radius ExtraAI[4]) gives Madness.
    private static void CalCloneMadness(OrigThreeRefs orig, NPC npc, Player target, ref float attackTimer, ref float armRotation, ref float frameVariant)
    {
        orig(npc, target, ref attackTimer, ref armRotation, ref frameVariant);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return;

        const int bookAppearTime = 30;
        float blackCutoutRadius = npc.Infernum().ExtraAI[4];
        if (attackTimer >= bookAppearTime && !player.WithinRange(npc.Center, blackCutoutRadius + 12f))
            player.AddBuff(ModContent.BuffType<Madness>(), 8);
    }

    // Polterghast: in the desperation attack, being more than 690 from Polterghast gives Madness until the final burst.
    private static void PolterghastMadness(OrigThreeRefs orig, NPC npc, Player target, ref float attackTimer, ref float vignetteInterpolant, ref float radiusDecreaseFactor)
    {
        orig(npc, target, ref attackTimer, ref vignetteInterpolant, ref radiusDecreaseFactor);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return;

        const int finalAttackStartTime = 102 + 540 + 540; // attackDelay + vortexSpiralTime + spiritFlameTime
        float soulBurstDelay = npc.Infernum().ExtraAI[0];
        if (attackTimer < finalAttackStartTime + soulBurstDelay && !npc.WithinRange(player.Center, PolterghastBehaviorOverride.MinGhostCircleRadius + 90f))
            player.AddBuff(ModContent.BuffType<Madness>(), 8);
    }

    // Crabulon: the claw slam hurts anyone within 100 of either claw on the slam frame.
    private static void CrabulonSlam(OrigClawSlam orig, NPC npc, Player target, ref float attackTimer, ref bool usingClaws, bool enraged)
    {
        orig(npc, target, ref attackTimer, ref usingClaws, enraged);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return;

        const int slamFrame = 72 + 32 + 28; // clawMoveTime + clawPressTime + clawSlamTime
        if (attackTimer != slamFrame)
            return;

        Vector2 clawOffset = new(npc.Infernum().ExtraAI[CrabulonBehaviorOverride.DetachedHandOffsetXIndex], npc.Infernum().ExtraAI[CrabulonBehaviorOverride.DetachedHandOffsetYIndex]);
        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 clawCenter = npc.Center + CrabulonBehaviorOverride.GetBaseClawOffset(npc, i == 1) + clawOffset * new Vector2(-i, 1f);
            if (player.WithinRange(clawCenter, 100f))
                player.Hurt(PlayerDeathReason.ByNPC(npc.whoAmI), npc.damage, i);
        }
    }

    // Supreme Calamitas: leaving the arena during the gigablast bullet hell (desperation ExtraAI[4] == 2) kills you.
    private static void SCalArenaKill(OrigThreeRefs orig, NPC npc, Player target, ref float frameType, ref float frameChangeSpeed, ref float attackTimer)
    {
        orig(npc, target, ref frameType, ref frameChangeSpeed, ref attackTimer);
        if (!TryGetLocalNonTarget(npc, out Player player))
            return;

        Rectangle arena = npc.Infernum().Arena;
        if (npc.Infernum().ExtraAI[4] == 2f && !arena.IsEmpty && !player.Hitbox.Intersects(arena))
            player.KillMe(PlayerDeathReason.ByNPC(npc.whoAmI), 1000000, 1);
    }
}
