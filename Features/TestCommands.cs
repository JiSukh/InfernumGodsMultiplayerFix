using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace CalWotGMPFix.Features;

// Test helpers so a boss gauntlet can be run back to back. Type these in chat.
//   /mptest list            - numbered boss list with where to fight each one
//   /mptest boss <n|name>   - summon that boss on you
//   /mptest next            - summon the next boss after the last one summoned
//   /mptest kill            - remove all bosses, enemies and enemy projectiles (no loot)
//   /mptest gather          - teleport every player to you
//   /mpgod                  - (your own game) toggle taking no damage
public class MPTestCommand : ModCommand
{
    private record BossEntry(string Name, string Where, Func<int[]> Types, bool UseProperSummon = false);

    private static readonly List<BossEntry> Bosses =
    [
        new("King Slime", "anywhere", () => [NPCID.KingSlime]),
        new("Desert Scourge", "Desert", () => NPCOf("CalamityMod", "DesertScourgeHead")),
        new("Eye of Cthulhu", "night", () => [NPCID.EyeofCthulhu]),
        new("Crabulon", "Glowing Mushroom biome", () => NPCOf("CalamityMod", "Crabulon")),
        new("Eater of Worlds", "Corruption", () => [NPCID.EaterofWorldsHead]),
        new("Brain of Cthulhu", "Crimson", () => [NPCID.BrainofCthulhu]),
        new("Hive Mind", "Corruption", () => NPCOf("CalamityMod", "HiveMind")),
        new("Perforators", "Crimson", () => NPCOf("CalamityMod", "PerforatorHive")),
        new("Queen Bee", "Jungle", () => [NPCID.QueenBee]),
        new("Deerclops", "Snow", () => [NPCID.Deerclops]),
        new("Skeletron", "Dungeon entrance, night", () => [NPCID.SkeletronHead]),
        new("Slime God", "anywhere", () => NPCOf("CalamityMod", "SlimeGodCore")),
        new("Wall of Flesh", "Underworld (spawns at you)", () => [NPCID.WallofFlesh]),
        new("Queen Slime", "Hallow", () => [NPCID.QueenSlimeBoss]),
        new("Cryogen", "Snow", () => NPCOf("CalamityMod", "Cryogen")),
        new("The Twins", "night", () => [NPCID.Retinazer, NPCID.Spazmatism]),
        new("Aquatic Scourge", "Sulphurous Sea", () => NPCOf("CalamityMod", "AquaticScourgeHead")),
        new("The Destroyer", "night", () => [NPCID.TheDestroyer]),
        new("Brimstone Elemental", "Brimstone Crag", () => NPCOf("CalamityMod", "BrimstoneElemental")),
        new("Skeletron Prime", "night", () => [NPCID.SkeletronPrime]),
        new("Calamitas Clone", "night", () => NPCOf("CalamityMod", "CalamitasClone")),
        new("Plantera", "Underground Jungle", () => [NPCID.Plantera]),
        new("Leviathan & Anahita", "Ocean", () => NPCOf("CalamityMod", "Anahita")),
        new("Astrum Aureus", "Astral Infection, night", () => NPCOf("CalamityMod", "AstrumAureus")),
        new("Golem", "Jungle Temple", () => [NPCID.Golem]),
        new("Plaguebringer Goliath", "Jungle", () => NPCOf("CalamityMod", "PlaguebringerGoliath")),
        new("Empress of Light", "Hallow, night", () => [NPCID.HallowBoss]),
        new("Duke Fishron", "Ocean", () => [NPCID.DukeFishron]),
        new("Ravager", "anywhere", () => NPCOf("CalamityMod", "RavagerBody")),
        new("Lunatic Cultist", "Dungeon entrance", () => [NPCID.CultistBoss]),
        new("Astrum Deus", "surface, night", () => NPCOf("CalamityMod", "AstrumDeusHead")),
        new("Moon Lord", "anywhere", () => [NPCID.MoonLordCore]),
        new("Profaned Guardians", "Profaned Garden / Hallow or Underworld, day", () => NPCOf("CalamityMod", "ProfanedGuardianCommander")),
        new("Dragonfolly", "Jungle", () => NPCOf("CalamityMod", "Dragonfolly")),
        new("Providence", "Profaned Garden", () => NPCOf("CalamityMod", "Providence")),
        new("Ceaseless Void", "Dungeon", () => NPCOf("CalamityMod", "CeaselessVoid")),
        new("Storm Weaver", "Space", () => NPCOf("CalamityMod", "StormWeaverHead")),
        new("Signus", "anywhere", () => NPCOf("CalamityMod", "Signus")),
        new("Polterghast", "Dungeon", () => NPCOf("CalamityMod", "Polterghast")),
        new("Old Duke", "Sulphurous Sea", () => NPCOf("CalamityMod", "OldDuke")),
        new("Devourer of Gods", "anywhere", () => NPCOf("CalamityMod", "DevourerofGodsHead")),
        new("Yharon", "Jungle", () => NPCOf("CalamityMod", "Yharon")),
        new("Exo Mechs (Draedon)", "anywhere - pick mechs in Draedon's menu", () => NPCOf("CalamityMod", "Draedon")),
        new("Supreme Calamitas", "anywhere", () => NPCOf("CalamityMod", "SupremeCalamitas")),
        new("Mars (WotG)", "anywhere", () => NPCOf("NoxusBoss", "MarsBody"), UseProperSummon: true),
        new("Avatar of Emptiness (WotG)", "anywhere", () => NPCOf("NoxusBoss", "AvatarRift"), UseProperSummon: true),
        new("Nameless Deity (WotG)", "Eternal Garden - use the Seed of Will there", () => [], UseProperSummon: true),
    ];

    private static int lastSummoned = -1;

    public override CommandType Type => CommandType.World; // runs on the server in multiplayer
    public override string Command => "mptest";
    public override string Usage => "/mptest list | boss <number or name> | next | kill | gather";
    public override string Description => "Multiplayer boss test helpers (CalWotGMPFix)";

    private static int[] NPCOf(string mod, string name) =>
        ModContent.TryFind(mod, name, out ModNPC npc) ? [npc.Type] : [];

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        switch (sub)
        {
            case "list":
                for (int i = 0; i < Bosses.Count; i++)
                    caller.Reply($"{i + 1}. {Bosses[i].Name} - {Bosses[i].Where}", Bosses[i].UseProperSummon ? Color.Orange : Color.LightGray);
                break;

            case "boss":
                if (args.Length < 2)
                    throw new UsageException("Usage: /mptest boss <number or name>");
                Summon(caller, FindBoss(string.Join(' ', args.Skip(1))));
                break;

            case "next":
                Summon(caller, (lastSummoned + 1) % Bosses.Count);
                break;

            case "kill":
                ClearFight(caller);
                break;

            case "gather":
                Gather(caller);
                break;

            default:
                throw new UsageException(Usage);
        }
    }

    private static int FindBoss(string query)
    {
        if (int.TryParse(query, out int number) && number >= 1 && number <= Bosses.Count)
            return number - 1;

        int index = Bosses.FindIndex(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new UsageException($"No boss matching \"{query}\". Use /mptest list.");
        return index;
    }

    private static void Summon(CommandCaller caller, int index)
    {
        BossEntry entry = Bosses[index];
        int[] types = entry.Types();
        Player player = caller.Player;
        if (player is null)
            throw new UsageException("Run this in-game, not from the server console.");
        if (types.Length == 0)
        {
            caller.Reply($"{entry.Name}: can't summon directly ({entry.Where}).", Color.Orange);
            lastSummoned = index;
            return;
        }

        if (NPC.AnyNPCs(types[0]))
            throw new UsageException($"{entry.Name} is already alive. Use /mptest kill first.");

        foreach (int type in types)
        {
            if (type == NPCID.WallofFlesh)
                NPC.SpawnWOF(player.Center);
            else
                NPC.SpawnOnPlayer(player.whoAmI, type);
        }

        lastSummoned = index;
        caller.Reply($"[{index + 1}/{Bosses.Count}] {entry.Name} summoned. Fight it in: {entry.Where}" +
            (entry.UseProperSummon ? " (direct spawn may skip its setup - use the real summon item if it acts oddly)" : ""), Color.LightGreen);
    }

    private static void ClearFight(CommandCaller caller)
    {
        int npcs = 0, projectiles = 0;
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.townNPC || npc.friendly || npc.CountsAsACritter)
                continue;
            npc.active = false;
            npc.netUpdate = true;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, npc.whoAmI);
            npcs++;
        }
        foreach (Projectile projectile in Main.ActiveProjectiles)
        {
            if (!projectile.hostile)
                continue;
            projectile.Kill();
            projectiles++;
        }
        caller.Reply($"Cleared {npcs} enemies and {projectiles} enemy projectiles.", Color.LightGreen);
    }

    private static void Gather(CommandCaller caller)
    {
        Player host = caller.Player ?? throw new UsageException("Run this in-game.");
        int moved = 0;
        foreach (Player p in Main.ActivePlayers)
        {
            if (p.whoAmI == host.whoAmI)
                continue;
            Vector2 destination = host.position + new Vector2((moved % 2 == 0 ? 1 : -1) * 48f * (moved / 2 + 1), 0f);
            p.Teleport(destination, TeleportationStyleID.RodOfDiscord);
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, p.whoAmI, destination.X, destination.Y, TeleportationStyleID.RodOfDiscord);
            moved++;
        }
        caller.Reply($"Teleported {moved} player(s) to {host.name}.", Color.LightGreen);
    }
}

// /mpgod - runs on your own game. You take no damage while it's on (for watching attacks and desyncs).
public class MPGodCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "mpgod";
    public override string Description => "Toggle taking no damage (testing only)";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        MPGodPlayer.Enabled = !MPGodPlayer.Enabled;
        Main.NewText($"[MP Fix] God mode {(MPGodPlayer.Enabled ? "ON" : "OFF")}", MPGodPlayer.Enabled ? Color.Gold : Color.Gray);
    }
}

public class MPGodPlayer : ModPlayer
{
    public static bool Enabled;

    public override bool FreeDodge(Player.HurtInfo info) => Enabled && Player.whoAmI == Main.myPlayer;

    public override bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound, ref bool genDust, ref Terraria.DataStructures.PlayerDeathReason damageSource)
    {
        if (Enabled && Player.whoAmI == Main.myPlayer)
        {
            Player.statLife = Player.statLifeMax2;
            return false;
        }
        return true;
    }
}
