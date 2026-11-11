using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CalWotGMPFix.Common;
using InfernumMode.Content.BehaviorOverrides.BossAIs.CalamitasShadow;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Deerclops;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Draedon.Ares;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Draedon.ArtemisAndApollo;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Draedon.Thanatos;
using InfernumMode.Content.BehaviorOverrides.BossAIs.EmpressOfLight;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Golem;
using InfernumMode.Content.BehaviorOverrides.BossAIs.KingSlime;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Leviathan;
using InfernumMode.Content.BehaviorOverrides.BossAIs.MoonLord;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Perforators;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Plantera;
using InfernumMode.Content.BehaviorOverrides.BossAIs.ProfanedGuardians;
using InfernumMode.Content.BehaviorOverrides.BossAIs.QueenBee;
using InfernumMode.Content.BehaviorOverrides.BossAIs.QueenSlime;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Ravager;
using InfernumMode.Content.BehaviorOverrides.BossAIs.SupremeCalamitas;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Twins;
using InfernumMode.Content.BehaviorOverrides.BossAIs.Yharon;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Severity 5: these Infernum bosses pick a target at the start of the fight and only switch if that player
// dies or gets 4000+ units away. Here, whenever one of them starts a new attack, the server picks a new
// target among the players in the fight: either the next player in turn (Alternate) or a weighted random
// pick by the class each player is using (ClassWeighted, e.g. melee 7 vs summoner 3 = 70% / 30%).
// Bosses whose minions/parts copy the main body's target (Moon Lord's hands, Golem's fists, Leviathan's
// combo, the Guardians, SCal's brothers...) follow automatically.
// Providence and DoG have no "select next attack" step, so they switch every TimedSwitchSeconds instead.
[JITWhenModsEnabled("InfernumMode")]
public static class InfernumTargetAlternation
{
    public const string FixName = "Infernum bosses alternate targets";

    // Players further than this from the boss aren't considered "in the fight". Kept below Infernum's own
    // 4000-unit retarget threshold so Infernum doesn't immediately switch back.
    public const float FightRange = 3500f;

    private static readonly Dictionary<int, uint> lastSwitchTick = [];
    private static FieldInfo twinsTargetField;

    public static void Apply(Mod mod)
    {
        // Static "pick the next attack" methods, each taking the boss NPC.
        (Type type, string method)[] targets =
        [
            (typeof(KingSlimeBehaviorOverride), "SelectNextAttack"),
            (typeof(QueenBeeBehaviorOverride), "SelectNextAttack"),
            (typeof(DeerclopsBehaviorOverride), "SelectNextAttack"),
            (typeof(QueenSlimeBehaviorOverride), "SelectNextAttack"),
            (typeof(PlanteraBehaviorOverride), "SelectNextAttack"),
            (typeof(GolemBodyBehaviorOverride), "SelectNextAttackState"),
            (typeof(EmpressOfLightBehaviorOverride), "SelectNextAttack"),
            (typeof(MoonLordCoreBehaviorOverride), "SelectNextAttack"),
            (typeof(PerforatorHiveBehaviorOverride), "SelectNextAttack"),
            (typeof(CalamitasShadowBehaviorOverride), "SelectNextAttack"),
            (typeof(LeviathanComboAttackManager), "SelectNextAttackBase"),
            (typeof(LeviathanBehaviorOverride), "SelectNextAttack"),
            (typeof(AnahitaBehaviorOverride), "SelectNextAttack"),
            (typeof(RavagerBodyBehaviorOverride), "SelectNextAttack"),
            (typeof(GuardianComboAttackManager), "SelectNewAttack"),
            (typeof(YharonBehaviorOverride), "SelectNextAttack"),
            (typeof(ApolloBehaviorOverride), "SelectNextAttack"),
            (typeof(ThanatosHeadBehaviorOverride), "SelectNextAttack"),
            (typeof(AresBodyBehaviorOverride), "SelectNextAttack"),
            (typeof(SupremeCalamitasBehaviorOverride), "SelectNextAttack"),
            (typeof(SepulcherHeadBehaviorOverride), "SelectNextAttack"),
        ];

        MethodInfo onAttackSelected = typeof(InfernumTargetAlternation).GetMethod(nameof(OnAttackSelected), BindingFlags.Public | BindingFlags.Static);
        int hooked = 0;
        foreach ((Type type, string name) in targets)
        {
            try
            {
                MethodInfo method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(m => m.Name == name && m.GetParameters().Any(p => p.ParameterType == typeof(NPC)));
                int npcArg = Array.FindIndex(method.GetParameters(), p => p.ParameterType == typeof(NPC));
                MonoModHooks.Modify(method, il => ILPatches.CallBeforeEveryReturn(il, onAttackSelected, npcArg));
                hooked++;
            }
            catch (Exception e)
            {
                mod.Logger.Warn($"[{FixName}] could not hook {type.Name}.{name}: {e.Message}");
            }
        }

        // The Twins share one static target index and pick attacks together.
        try
        {
            twinsTargetField = typeof(TwinsAttackSynchronizer).GetField("_targetIndex", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            MethodInfo twinsSelect = typeof(TwinsAttackSynchronizer).GetMethod("SelectNextAttack", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo onTwins = typeof(InfernumTargetAlternation).GetMethod(nameof(OnTwinsAttackSelected), BindingFlags.Public | BindingFlags.Static);
            MonoModHooks.Modify(twinsSelect, il => ILPatches.CallBeforeEveryReturn(il, onTwins, -1));
            hooked++;
        }
        catch (Exception e)
        {
            mod.Logger.Warn($"[{FixName}] could not hook the Twins: {e.Message}");
        }

        mod.Logger.Info($"[{FixName}] hooked {hooked}/{targets.Length + 1} attack-selection methods.");
        CalWotGMPFix.MarkFix(FixName, hooked == targets.Length + 1);
    }

    // Returns the next living player in the fight after `current`, or -1 if nobody else is eligible.
    private static int NextPlayer(int current, Microsoft.Xna.Framework.Vector2 bossCenter)
    {
        for (int offset = 1; offset < Main.maxPlayers; offset++)
        {
            int i = (current + offset + Main.maxPlayers) % Main.maxPlayers;
            Player p = Main.player[i];
            if (p.active && !p.dead && !p.ghost && p.WithinRange(bossCenter, FightRange))
                return i == current ? -1 : i;
        }
        return -1;
    }

    private static bool Enabled => Main.netMode == NetmodeID.Server && (MPFixConfig.Instance?.TargetingMode ?? BossTargeting.InfernumDefault) != BossTargeting.InfernumDefault;

    // Picks the boss's next target according to the configured mode. -1 = keep the current target.
    private static int ChooseTarget(int current, Microsoft.Xna.Framework.Vector2 bossCenter)
    {
        if (MPFixConfig.Instance.TargetingMode == BossTargeting.Alternate)
            return NextPlayer(current, bossCenter);

        // ClassWeighted: weighted random over everyone in the fight (the current target can be picked again).
        int totalWeight = 0;
        Span<int> weights = stackalloc int[Main.maxPlayers];
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            Player p = Main.player[i];
            if (!p.active || p.dead || p.ghost || !p.WithinRange(bossCenter, FightRange))
                continue;
            weights[i] = PlayerClassTracker.WeightFor(p);
            totalWeight += weights[i];
        }
        if (totalWeight <= 0)
            return -1;

        int roll = Main.rand.Next(totalWeight);
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return i == current ? -1 : i;
        }
        return -1;
    }

    public static void OnAttackSelected(NPC npc)
    {
        if (!Enabled || npc is null || !npc.active)
            return;

        // Combo bosses can run two selection methods in the same frame; only switch once.
        if (lastSwitchTick.TryGetValue(npc.whoAmI, out uint tick) && tick == Main.GameUpdateCount)
            return;

        int next = ChooseTarget(npc.target, npc.Center);
        if (next < 0)
            return;

        npc.target = next;
        npc.netUpdate = true;
        lastSwitchTick[npc.whoAmI] = Main.GameUpdateCount;
    }

    public static void OnTwinsAttackSelected()
    {
        if (!Enabled || twinsTargetField is null)
            return;

        int retinazer = NPC.FindFirstNPC(NPCID.Retinazer);
        int spazmatism = NPC.FindFirstNPC(NPCID.Spazmatism);
        NPC anchor = retinazer >= 0 ? Main.npc[retinazer] : spazmatism >= 0 ? Main.npc[spazmatism] : null;
        if (anchor is null)
            return;

        int current = (int)twinsTargetField.GetValue(null);
        int next = ChooseTarget(current < 0 ? 0 : current, anchor.Center);
        if (next < 0)
            return;

        twinsTargetField.SetValue(null, next);
        // Syncing either twin also sends Infernum's Twins packet, which carries the shared target.
        foreach (int index in new[] { retinazer, spazmatism })
        {
            if (index < 0)
                continue;
            Main.npc[index].target = next;
            Main.npc[index].netUpdate = true;
        }
    }
}

// Providence and the Devourer of Gods don't have a "select next attack" step, so on the server they switch
// to the next player in the fight on a timer instead.
[JITWhenModsEnabled("InfernumMode")]
public class InfernumTimedTargetSwitch : ModSystem
{
    public const int TimedSwitchSeconds = 10;

    public override bool IsLoadingEnabled(Mod mod) => ModLoader.HasMod("InfernumMode");

    public override void PostUpdateNPCs()
    {
        if (Main.netMode != NetmodeID.Server || (MPFixConfig.Instance?.TargetingMode ?? BossTargeting.InfernumDefault) == BossTargeting.InfernumDefault || !InfernumMode.InfernumMode.CanUseCustomAIs)
            return;
        if (Main.GameUpdateCount % (TimedSwitchSeconds * 60) != 0)
            return;

        int providence = ModContent.NPCType<CalamityMod.NPCs.Providence.Providence>();
        int dog = ModContent.NPCType<CalamityMod.NPCs.DevourerofGods.DevourerofGodsHead>();
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.type == providence || npc.type == dog)
                InfernumTargetAlternation.OnAttackSelected(npc);
        }
    }
}
