using System;
using CalWotGMPFix.Common;
using Luminance.Core.Graphics;
using Microsoft.Xna.Framework;
using NoxusBoss.Content.NPCs.Bosses.Draedon.Projectiles;
using NoxusBoss.Content.NPCs.Friendly;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Fixes;

// Mars - Electric Shockwave
// WotG only pushes players and Solyn back when Main.netMode == SinglePlayer, so in multiplayer the
// shockwave does nothing. Players own their own movement in Terraria, so each client pushes its own
// player; the server pushes Solyn (an NPC) and syncs her.
public class MarsShockwaveFix : GlobalProjectile
{
    public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) =>
        entity.type == ModContent.ProjectileType<ElectricShockwave>();

    public override void PostAI(Projectile projectile)
    {
        if (Main.netMode == NetmodeID.SinglePlayer || !(MPFixConfig.Instance?.EnableShockwavePush ?? false))
            return;
        if (projectile.Opacity < 0.3f)
            return;

        // Same numbers as ElectricShockwave.AI.
        float pushBackSpeed = MathHelper.Lerp(11.5f, 30f, MathF.Sqrt(projectile.Opacity));
        float softDistanceLimit = projectile.scale * projectile.width * 0.41f;
        float hardDistanceLimit = projectile.scale * projectile.width * 0.38f;

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Player player = Main.LocalPlayer;
            if (player.active && !player.dead && Push(player, projectile, pushBackSpeed, softDistanceLimit, hardDistanceLimit))
            {
                ScreenShakeSystem.StartShake(3.7f);
                player.mount?.Dismount(player);
            }
            return;
        }

        int solynID = ModContent.NPCType<BattleSolyn>();
        foreach (NPC solyn in Main.ActiveNPCs)
        {
            if (solyn.type == solynID && Push(solyn, projectile, pushBackSpeed, softDistanceLimit, hardDistanceLimit))
                solyn.netUpdate = true;
        }
    }

    private static bool Push(Entity entity, Projectile projectile, float pushBackSpeed, float softDistanceLimit, float hardDistanceLimit)
    {
        if (!entity.WithinRange(projectile.Center, softDistanceLimit))
            return false;

        Vector2 awayFromCenter = (entity.Center - projectile.Center).SafeNormalize(Vector2.Zero);
        entity.velocity = Vector2.Lerp(entity.velocity, awayFromCenter * pushBackSpeed, 0.36f);

        if (entity.WithinRange(projectile.Center, hardDistanceLimit))
        {
            Vector2 shoveDestination = projectile.Center + awayFromCenter * hardDistanceLimit;
            bool wouldShoveIntoTiles = Collision.SolidTiles(shoveDestination - entity.Size * 0.5f, entity.width, entity.height);
            bool wouldEffectivelyTeleport = !shoveDestination.WithinRange(entity.Center, 150f);
            if (!wouldShoveIntoTiles && !wouldEffectivelyTeleport)
                entity.Center = shoveDestination;
        }
        return true;
    }
}
