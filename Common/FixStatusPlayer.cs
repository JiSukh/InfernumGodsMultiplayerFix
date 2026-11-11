using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Common;

// Prints which fixes loaded when you join a world, so both players can check they match.
public class FixStatusPlayer : ModPlayer
{
    public override void OnEnterWorld()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;

        string fixes = string.Join(", ", CalWotGMPFix.FixStatus.Select(kv => $"{kv.Key}: {(kv.Value ? "ON" : "FAILED")}"));
        Main.NewText($"[MP Fix v{Mod.Version}] WotG v{CalWotGMPFix.WotGVersion} | {fixes}", new Color(120, 200, 255));

        if (CalWotGMPFix.WotGVersion != CalWotGMPFix.TestedWotGVersion)
            Main.NewText($"[MP Fix] Built for WotG v{CalWotGMPFix.TestedWotGVersion}. Some fixes may be off.", Color.Orange);
    }
}
