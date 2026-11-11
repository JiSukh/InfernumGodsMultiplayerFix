using System;
using System.Collections.Generic;
using CalWotGMPFix.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace CalWotGMPFix.Features;

// Boss-fight lives + spectating (multiplayer only).
//
// Lives: the host sets "Boss fight lives" (0 = unlimited). Each death during a boss fight uses one life.
// When you're out, you stay dead until the fight ends (no boss alive for a few seconds), then respawn
// normally. If everyone runs out, the boss despawns as usual (no living targets) and everyone respawns.
//
// Spectating: while you're dead, your camera follows a living teammate. "[" / "]" switch between them.
// Your (dead, invisible) character is moved with the camera so the server streams you that area of the map.
public class BossLivesSystem : ModSystem
{
    public static ModKeybind SpectatePrevious;
    public static ModKeybind SpectateNext;

    // Fight state, tracked on each client.
    public static bool FightActive { get; private set; }
    private static int framesWithoutBoss;
    private const int FightEndGraceFrames = 180; // bosses briefly disappear between phases in some fights

    public static int SpectateIndex = -1;

    public override void Load()
    {
        SpectatePrevious = KeybindLoader.RegisterKeybind(Mod, "SpectatePrevious", "OemOpenBrackets");
        SpectateNext = KeybindLoader.RegisterKeybind(Mod, "SpectateNext", "OemCloseBrackets");
    }

    public override void Unload()
    {
        SpectatePrevious = null;
        SpectateNext = null;
    }

    public static bool AnyBossAlive()
    {
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.boss)
                return true;
        }
        return false;
    }

    public override void PostUpdateNPCs()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;

        if (AnyBossAlive())
        {
            framesWithoutBoss = 0;
            FightActive = true;
        }
        else if (FightActive && ++framesWithoutBoss >= FightEndGraceFrames)
        {
            FightActive = false;
            Main.LocalPlayer.GetModPlayer<BossLivesPlayer>().OnFightEnded();
        }
    }

    public override void PostUpdateInput()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !Main.LocalPlayer.dead || Main.drawingPlayerChat)
            return;

        if (SpectateNext?.JustPressed ?? false)
            SpectateIndex = FindLivingPlayer(SpectateIndex, 1);
        if (SpectatePrevious?.JustPressed ?? false)
            SpectateIndex = FindLivingPlayer(SpectateIndex, -1);
    }

    // Next living player after `from` in the given direction, or -1 if nobody else is alive.
    public static int FindLivingPlayer(int from, int direction)
    {
        for (int step = 1; step <= Main.maxPlayers; step++)
        {
            int i = ((from < 0 ? 0 : from) + step * direction + Main.maxPlayers * 2) % Main.maxPlayers;
            Player p = Main.player[i];
            if (i != Main.myPlayer && p.active && !p.dead && !p.ghost)
                return i;
        }
        return -1;
    }

    public static Player SpectateTarget
    {
        get
        {
            if (SpectateIndex >= 0 && SpectateIndex < Main.maxPlayers)
            {
                Player p = Main.player[SpectateIndex];
                if (p.active && !p.dead && !p.ghost && SpectateIndex != Main.myPlayer)
                    return p;
            }
            SpectateIndex = FindLivingPlayer(SpectateIndex, 1);
            return SpectateIndex >= 0 ? Main.player[SpectateIndex] : null;
        }
    }

    private static Vector2? smoothedCameraCenter;

    public override void ModifyScreenPosition()
    {
        if (!BossLivesPlayer.Spectating)
        {
            smoothedCameraCenter = null;
            return;
        }

        Player target = SpectateTarget;
        if (target is null)
            return;

        // Ease toward the spectated player instead of snapping every frame; jump straight there on big teleports
        // (Avatar/Nameless move everyone around) so the camera doesn't fly across the world.
        Vector2 goal = target.Center;
        if (smoothedCameraCenter is not Vector2 current || Vector2.Distance(current, goal) > 2400f)
            smoothedCameraCenter = goal;
        else
            smoothedCameraCenter = Vector2.Lerp(current, goal, 0.2f);

        Main.screenPosition = smoothedCameraCenter.Value - new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
    }

    // Wrath of the Gods bosses teleport every player (dead ones included) between their arenas/dimensions
    // themselves, and fight inside subworlds. Moving the dead player ourselves fights with that, so during those
    // fights spectating only moves the camera.
    public static bool WotGBossAlive()
    {
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.boss && npc.ModNPC?.Mod.Name == "NoxusBoss")
                return true;
        }
        return false;
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;

        int index = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
        layers.Insert(index < 0 ? layers.Count : index + 1, new LegacyGameInterfaceLayer("CalWotGMPFix: Lives and Spectate", DrawOverlay, InterfaceScaleType.UI));
    }

    private static bool DrawOverlay()
    {
        BossLivesPlayer lives = Main.LocalPlayer.GetModPlayer<BossLivesPlayer>();
        int maxLives = MPFixConfig.Instance?.BossFightLives ?? 0;
        List<string> lines = [];

        if (BossLivesPlayer.Spectating)
        {
            Player target = SpectateTarget;
            lines.Add(target is null ? "No living teammates to spectate" : $"Spectating {target.name}   ( [ / ] to switch )");
        }
        if (FightActive && maxLives > 0)
            lines.Add(lives.OutOfLives ? "Out of lives - you'll respawn when the fight ends" : $"Lives left this fight: {maxLives - lives.DeathsThisFight}/{maxLives}");

        Vector2 position = new(Main.screenWidth * 0.5f, 110f);
        foreach (string line in lines)
        {
            Vector2 size = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(line);
            Utils.DrawBorderString(Main.spriteBatch, line, position - new Vector2(size.X * 0.5f, 0f), lives.OutOfLives ? Color.OrangeRed : Color.White);
            position.Y += size.Y + 4f;
        }
        return true;
    }
}

public class BossLivesPlayer : ModPlayer
{
    public int DeathsThisFight;

    public bool OutOfLives
    {
        get
        {
            int maxLives = MPFixConfig.Instance?.BossFightLives ?? 0;
            return maxLives > 0 && DeathsThisFight >= maxLives;
        }
    }

    // True when the local player is dead in multiplayer and wants to spectate.
    public static bool Spectating =>
        Main.netMode == NetmodeID.MultiplayerClient && Main.LocalPlayer.dead && (MPFixClientConfig.Instance?.SpectateWhenDead ?? false);

    private int syncCounter;

    public void OnFightEnded()
    {
        if (DeathsThisFight > 0 && OutOfLives)
            Main.NewText("[MP Fix] Fight over - respawning.", Color.LightGreen);
        DeathsThisFight = 0;
    }

    public override void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || Player.whoAmI != Main.myPlayer || !BossLivesSystem.AnyBossAlive())
            return;

        DeathsThisFight++;
        int maxLives = MPFixConfig.Instance?.BossFightLives ?? 0;
        if (maxLives <= 0)
            return;

        if (OutOfLives)
            Main.NewText("[MP Fix] Out of lives - spectating until the fight ends.", Color.OrangeRed);
        else
            Main.NewText($"[MP Fix] {maxLives - DeathsThisFight} {(maxLives - DeathsThisFight == 1 ? "life" : "lives")} left this fight.", Color.Orange);
    }

    public override void UpdateDead()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || Player.whoAmI != Main.myPlayer)
            return;

        // Hold the respawn while out of lives and the fight is still going.
        if (OutOfLives && BossLivesSystem.FightActive)
            Player.respawnTimer = Math.Max(Player.respawnTimer, 60);

        if (!Spectating)
            return;

        // Move the dead (invisible, intangible) character with the camera, so the server sends the map and
        // enemies around the spectated player. Spawning puts you back at your spawn point as normal.
        Player target = BossLivesSystem.SpectateTarget;
        if (target is null || BossLivesSystem.WotGBossAlive())
            return;

        Player.position = target.position;
        Player.velocity = Vector2.Zero;
        if (++syncCounter % 20 == 0)
            NetMessage.SendData(MessageID.PlayerControls, -1, -1, null, Player.whoAmI);
    }

    public override void OnRespawn()
    {
        BossLivesSystem.SpectateIndex = -1;
    }
}
