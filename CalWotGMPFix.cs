using System;
using System.Collections.Generic;
using CalWotGMPFix.Fixes;
using Terraria.ModLoader;

namespace CalWotGMPFix;

public class CalWotGMPFix : Mod
{
    // Versions this build was written and tested against. Other versions still load, but patches
    // that fail to find their target code switch themselves off instead of crashing.
    public static readonly Version TestedWotGVersion = new(1, 2, 31);

    public static CalWotGMPFix Instance { get; private set; }

    // Name -> whether it applied. Shown in chat on world entry so both players can confirm.
    public static readonly Dictionary<string, bool> FixStatus = [];

    public static Version WotGVersion { get; private set; }

    public override void Load()
    {
        Instance = this;
        FixStatus.Clear();

        if (ModLoader.TryGetMod("NoxusBoss", out Mod wotg))
            WotGVersion = wotg.Version;

        if (WotGVersion != TestedWotGVersion)
            Logger.Warn($"Wrath of the Gods is v{WotGVersion}, this patch was written for v{TestedWotGVersion}. Patches will verify their targets before applying.");

        AvatarFrostSmashFix.Apply(this);
        MarkFix("Mars shockwave push (MP)", true); // GlobalProjectile, no patching needed
        WotGDuplicateFix.Apply();
        WotGClientFixes.Apply(this);

        if (ModLoader.HasMod("InfernumMode"))
        {
            InfernumMPFix.Apply(this);
            InfernumSharedMechanics.Apply(this);
            InfernumTargetAlternation.Apply(this);
        }
    }

    public override void PostSetupContent()
    {
        WotGDuplicateFix.PostSetupContent();
        if (ModLoader.HasMod("InfernumMode"))
            InfernumMPFix.PostSetupContent();
    }

    public override void Unload()
    {
        if (ModLoader.HasMod("InfernumMode"))
            InfernumMPFix.Unload();
        Instance = null;
        FixStatus.Clear();
    }

    public static void MarkFix(string name, bool applied)
    {
        FixStatus[name] = applied;
        Instance?.Logger.Info($"[Fix] {name}: {(applied ? "applied" : "NOT applied")}");
    }
}
