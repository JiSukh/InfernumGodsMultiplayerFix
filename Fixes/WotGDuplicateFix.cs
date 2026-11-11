using System.Collections.Generic;
using CalWotGMPFix.Common;
using NoxusBoss.Content.NPCs.Bosses.Avatar.Projectiles;
using NoxusBoss.Content.NPCs.Bosses.Avatar.SecondPhaseForm;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Avatar of Emptiness - two attacks spawn projectiles without a "server only" check, so in multiplayer every
// client adds its own copy on top of the server's:
//  - Antishadow Onslaught: the falling weapons (these deal damage) -> 3x weapons with a host + 1 friend.
//  - Rubble Gravity Slam: the per-player teleport portals.
// Every other place Avatar spawns these two projectile types is already server-only, so a client spawning one
// while running Avatar's AI is always a duplicate.
public static class WotGDuplicateFix
{
    public const string FixName = "WotG Avatar duplicate projectile fix";

    private static readonly HashSet<int> serverOnlyTypes = [];

    public static void Apply()
    {
        ClientSpawnBlocker.RegisterPendingSpawnAction(typeof(NoxusBoss.Core.Fixes.ProjectileSpawnManagementSystem), "preSyncAction");
        ClientSpawnBlocker.AddProjectileRule(ShouldBlock);
        CalWotGMPFix.MarkFix(FixName, true);
    }

    public static void PostSetupContent()
    {
        serverOnlyTypes.Clear();
        serverOnlyTypes.Add(ModContent.ProjectileType<FallingMeleeWeapon>());
        serverOnlyTypes.Add(ModContent.ProjectileType<DarkPortal>());
    }

    private static bool ShouldBlock(IEntitySource source, int type)
    {
        if (!(MPFixConfig.Instance?.EnableWotGDuplicateFix ?? false) || !serverOnlyTypes.Contains(type))
            return false;

        return ClientSpawnBlocker.UpdatingNPC?.ModNPC is AvatarOfEmptiness;
    }
}
