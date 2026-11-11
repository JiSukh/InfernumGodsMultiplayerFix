using System;
using System.Linq;
using System.Reflection;
using CalWotGMPFix.Common;
using Luminance.Core.Graphics;
using Microsoft.Xna.Framework;
using MonoMod.Utils;
using NoxusBoss.Assets;
using NoxusBoss.Content.NPCs.Bosses.Avatar.SecondPhaseForm;
using NoxusBoss.Content.Particles;
using NoxusBoss.Core.World.GameScenes.RiftEclipse;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Avatar of Emptiness - Frost Screen Smash
//
// 1. Bug fix (always on): ReceiveExtraAI never reads the telegraph list, so every field after it
//    (the AI state stack) is read from the wrong bytes on clients. See ILPatches.FixTelegraphReadLoop.
//
// 2. WotG skips this attack in multiplayer. With the read fixed, we let it run, but make the
//    server the only one that rolls the random telegraphs. Clients draw the telegraphs the server
//    sent instead of rolling their own (which showed the "safe" red twinkle in the wrong place).
public static class AvatarFrostSmashFix
{
    public const string ReceiveFixName = "Avatar network read fix";
    public const string FrostSmashName = "Avatar Frost Screen Smash in MP";

    public static bool ReceiveFixApplied { get; private set; }

    // How many synced telegraph sets this client has already drawn.
    private static int clientTelegraphsShown;

    public static void Apply(Mod mod)
    {
        Type avatar = typeof(AvatarOfEmptiness);

        // --- 1. ReceiveExtraAI read fix ---
        try
        {
            MethodInfo receive = avatar.GetMethod(nameof(AvatarOfEmptiness.ReceiveExtraAI), BindingFlags.Public | BindingFlags.Instance);
            using (DynamicMethodDefinition dmd = new(receive))
            {
                // Dry run on a copy first so a mismatch never leaves a half-applied hook.
                new MonoMod.Cil.ILContext(dmd.Definition).Invoke(ILPatches.FixTelegraphReadLoop);
            }
            MonoModHooks.Modify(receive, ILPatches.FixTelegraphReadLoop);
            ReceiveFixApplied = true;
        }
        catch (Exception e)
        {
            mod.Logger.Error("Avatar ReceiveExtraAI fix failed, Frost Screen Smash will stay disabled in MP.", e);
            ReceiveFixApplied = false;
        }
        CalWotGMPFix.MarkFix(ReceiveFixName, ReceiveFixApplied);

        // --- 2. Re-enable the attack in MP ---
        bool smashApplied = false;
        if (ReceiveFixApplied)
        {
            try
            {
                MethodInfo decision = typeof(AvatarFrostSmashFix).GetMethod(nameof(ShouldSkipFrostSmash), BindingFlags.Public | BindingFlags.Static);

                var lambdas = avatar.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Prepend(avatar)
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    .Where(m => m.Name.Contains("<LoadState_FrostScreenSmash>") && m.GetMethodBody() is not null)
                    .Where(m =>
                    {
                        using DynamicMethodDefinition dmd = new(m);
                        return ILPatches.IsNetModeOnlyLambda(dmd.Definition.Body);
                    })
                    .ToList();

                if (lambdas.Count != 1)
                    throw new Exception($"Expected exactly one multiplayer-skip lambda, found {lambdas.Count}.");

                MonoModHooks.Modify(lambdas[0], il =>
                {
                    if (!ILPatches.ReplaceNetModeLambda(il, decision))
                        throw new Exception("Multiplayer-skip lambda changed shape.");
                });

                MethodInfo telegraphs = avatar.GetMethod(nameof(AvatarOfEmptiness.DoBehavior_FrostScreenSmash_TwinkleTelegraphs), BindingFlags.Public | BindingFlags.Instance);
                MonoModHooks.Add(telegraphs, TwinkleTelegraphsDetour);

                smashApplied = true;
            }
            catch (Exception e)
            {
                mod.Logger.Error("Could not re-enable Avatar Frost Screen Smash in MP.", e);
            }
        }
        CalWotGMPFix.MarkFix(FrostSmashName, smashApplied);
    }

    // Replaces "() => Main.netMode != NetmodeID.SinglePlayer". True = skip the attack.
    public static bool ShouldSkipFrostSmash()
    {
        if (Main.netMode == NetmodeID.SinglePlayer)
            return false;

        return !ReceiveFixApplied || !(MPFixConfig.Instance?.EnableFrostScreenSmash ?? false);
    }

    private static void TwinkleTelegraphsDetour(Action<AvatarOfEmptiness> orig, AvatarOfEmptiness self)
    {
        // Server and singleplayer run WotG's code as-is: they roll the telegraphs and the server syncs them.
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            orig(self);
            return;
        }

        // Client: same visuals as the original, but telegraphs come from the server's synced list.
        NPC npc = self.NPC;
        npc.Opacity = 0f;
        self.ZPosition = 50f;

        int castDelay = AvatarOfEmptiness.FrostScreenSmash_TelegraphCastDelay;
        int waitTime = AvatarOfEmptiness.FrostScreenSmash_TelegraphWaitTime;
        int timer = self.AITimer;
        if ((timer - castDelay) % waitTime == 1 && timer >= castDelay)
        {
            SoundEngine.PlaySound(GennedAssets.Sounds.Avatar.Chirp with { Volume = 1.75f });
            ScreenShakeSystem.StartShake(4f);
        }

        var sets = self.TwinkleSmashDetails;
        if (clientTelegraphsShown > sets.Count)
            clientTelegraphsShown = sets.Count;

        while (clientTelegraphsShown < sets.Count)
        {
            SpawnTelegraph(self, sets[clientTelegraphsShown], waitTime);
            clientTelegraphsShown++;
        }

        RiftEclipseFogShaderData.FogDrawIntensityOverride = AvatarOfEmptiness.FrostScreenSmash_MaxFrostIntensity;
        self.PerformStandardLimbUpdates();
    }

    private static void SpawnTelegraph(AvatarOfEmptiness self, AvatarOfEmptiness.TwinkleSmashTelegraphSet set, int waitTime)
    {
        NPC npc = self.NPC;
        for (int i = 0; i < set.FrostOffsets.Count; i++)
        {
            Vector2 offset = set.FrostOffsets[i];
            Color color = AvatarOfEmptiness.FrostScreenSmash_CyanSparkleColor;
            color.R += (byte)Main.rand.Next(30);
            if (i == set.DangerousTwinkleIndex)
                color = AvatarOfEmptiness.FrostScreenSmash_RedSparkleColor;

            TwinkleParticle twinkle = new(npc.Center + offset, Vector2.Zero, color, waitTime - Main.rand.Next(24), 6, Vector2.One * 2f, Color.White * 0.5f,
                new TwinkleParticle.LockOnDetails(offset, () => self.Target.Center));
            twinkle.Spawn();
        }

        SoundEngine.PlaySound(GennedAssets.Sounds.Common.Twinkle with { MaxInstances = 5, PitchVariance = 0.16f, Pitch = -0.26f });
    }
}
