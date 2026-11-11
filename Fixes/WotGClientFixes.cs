using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CalWotGMPFix.Common;
using NoxusBoss.Content.NPCs.Bosses.Avatar.SecondPhaseForm;
using NoxusBoss.Content.NPCs.Bosses.Draedon;
using NoxusBoss.Content.NPCs.Bosses.NamelessDeity;
using NoxusBoss.Content.NPCs.Bosses.NamelessDeity.Projectiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Fixes found in Loa's first WotG test session (11 Nov 2026).
//
// 1. Nameless Deity crash: ConstellationProjectileRenderer reads ConstellationProjectile.ConstellationShape, which
//    is only filled in by the projectile's AI. On a client, a clock/sword constellation synced from the server
//    can be drawn before its AI has run once (or after its AI bailed early), so the shape is null and the game
//    crashes with a NullReferenceException in UpdateVertexBuffer. We fill in any missing shapes before drawing,
//    and if the renderer still fails we skip drawing the stars for that frame instead of crashing.
//
// 2. Sync rewinds (Avatar, Nameless Deity, Mars): every time the server syncs one of these bosses it overwrites
//    the client's attack timer with the server's, which is a few frames behind (network delay). The client then
//    replays those frames, so anything triggered on an exact frame (sounds, screen shakes, looping sounds,
//    flashes) can fire twice or stack up. If the attack hasn't changed and the client is only slightly ahead,
//    we keep the client's timer.
public static class WotGClientFixes
{
    public const string ConstellationFixName = "WotG Nameless constellation crash fix";
    public const string SyncSmoothingName = "WotG boss sync smoothing";

    // Largest rewind we smooth over (frames). Bigger gaps are real desyncs and the server's value wins.
    public const int MaxRewindFrames = 30;

    private static FieldInfo shapeField;
    private static FieldInfo activeStarCountField;
    private static readonly Dictionary<Type, PropertyInfo> shapeGetters = [];
    private static bool loggedRendererError;

    public static void Apply(Mod mod)
    {
        // --- 1. Constellation crash ---
        bool constellationApplied = false;
        try
        {
            shapeField = typeof(ConstellationProjectile).GetField(nameof(ConstellationProjectile.ConstellationShape), BindingFlags.Public | BindingFlags.Instance);
            Type renderer = typeof(NamelessDeityBoss).Assembly.GetType("NoxusBoss.Content.NPCs.Bosses.NamelessDeity.SpecificEffectManagers.ConstellationProjectileRenderer", true);
            activeStarCountField = renderer.GetField("ActiveStarCount", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            MethodInfo update = renderer.GetMethod("UpdateVertexBuffer", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            On_Main.DrawProjectiles += FillMissingShapes; // added after WotG's hook, so it runs first
            MonoModHooks.Add(update, GuardUpdateVertexBuffer);
            constellationApplied = shapeField is not null;
        }
        catch (Exception e)
        {
            mod.Logger.Error("Nameless constellation crash fix failed to apply.", e);
        }
        CalWotGMPFix.MarkFix(ConstellationFixName, constellationApplied);

        // --- 2. Sync smoothing ---
        bool smoothingApplied = false;
        try
        {
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance;
            MonoModHooks.Add(typeof(AvatarOfEmptiness).GetMethod(nameof(AvatarOfEmptiness.ReceiveExtraAI), Inst), ReceiveAvatar);
            MonoModHooks.Add(typeof(NamelessDeityBoss).GetMethod(nameof(NamelessDeityBoss.ReceiveExtraAI), Inst), ReceiveNameless);
            MonoModHooks.Add(typeof(MarsBody).GetMethod(nameof(MarsBody.ReceiveExtraAI), Inst), ReceiveMars);
            smoothingApplied = true;
        }
        catch (Exception e)
        {
            mod.Logger.Error("WotG sync smoothing failed to apply.", e);
        }
        CalWotGMPFix.MarkFix(SyncSmoothingName, smoothingApplied);
    }

    // ---------------- 1. Constellation crash ----------------

    private static void FillMissingShapes(On_Main.orig_DrawProjectiles orig, Main self)
    {
        if (shapeField is not null)
        {
            foreach (Projectile projectile in Main.ActiveProjectiles)
            {
                if (projectile.ModProjectile is not ConstellationProjectile constellation || shapeField.GetValue(constellation) is not null)
                    continue;

                try
                {
                    Type type = constellation.GetType();
                    if (!shapeGetters.TryGetValue(type, out PropertyInfo getter))
                        shapeGetters[type] = getter = type.GetProperty("constellationShape", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    shapeField.SetValue(constellation, getter?.GetValue(constellation));
                }
                catch
                {
                    // Leave it; GuardUpdateVertexBuffer stops the crash.
                }
            }
        }
        orig(self);
    }

    private static void GuardUpdateVertexBuffer(Action orig)
    {
        try
        {
            orig();
        }
        catch (NullReferenceException e)
        {
            activeStarCountField?.SetValue(null, 0); // draw no constellation stars this frame
            if (!loggedRendererError)
            {
                loggedRendererError = true;
                CalWotGMPFix.Instance.Logger.Warn("[WotG fix] Constellation renderer hit missing data; skipped drawing stars for that frame instead of crashing.", e);
            }
        }
    }

    // ---------------- 2. Sync smoothing ----------------

    private static bool Smoothing => Main.netMode == NetmodeID.MultiplayerClient && (MPFixConfig.Instance?.EnableWotGSyncSmoothing ?? false);

    private static void ReceiveAvatar(Action<AvatarOfEmptiness, BinaryReader> orig, AvatarOfEmptiness self, BinaryReader reader)
    {
        if (!Smoothing) { orig(self, reader); return; }
        AvatarOfEmptiness.AvatarAIType state = self.CurrentState;
        int time = self.AITimer;
        orig(self, reader);
        if (self.CurrentState == state)
            KeepClientTime(ref self.AITimer, time);
    }

    private static void ReceiveNameless(Action<NamelessDeityBoss, BinaryReader> orig, NamelessDeityBoss self, BinaryReader reader)
    {
        if (!Smoothing) { orig(self, reader); return; }
        NamelessDeityBoss.NamelessAIType state = self.CurrentState;
        int time = self.AITimer;
        orig(self, reader);
        if (self.CurrentState == state)
            KeepClientTime(ref self.AITimer, time);
    }

    private static void ReceiveMars(Action<MarsBody, BinaryReader> orig, MarsBody self, BinaryReader reader)
    {
        if (!Smoothing) { orig(self, reader); return; }
        MarsBody.MarsAIType state = self.CurrentState;
        int time = self.AITimer;
        orig(self, reader);
        if (self.CurrentState == state)
            KeepClientTime(ref self.AITimer, time);
    }

    private static void KeepClientTime(ref int timer, int clientTime)
    {
        if (clientTime > timer && clientTime - timer <= MaxRewindFrames)
            timer = clientTime;
    }
}
