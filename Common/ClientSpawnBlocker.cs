using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalWotGMPFix.Common;

// In multiplayer the server owns every boss and every boss projectile. Boss AI still runs on each client
// (for smooth movement), so any spawn call that isn't wrapped in a "server only" check runs on clients too:
//  - a projectile spawned on a client is owned by that client and synced to everyone, so the attack fires again
//    once per player on top of the server's copy;
//  - an NPC spawned on a client exists only on that client (a "ghost" enemy nobody else can see or properly kill).
// This class tracks which boss/projectile the game is currently updating on this machine, and lets each fix
// register rules for spawns that a client should never make while running that code.
public class ClientSpawnBlocker : ModSystem
{
    public delegate bool ProjectileRule(IEntitySource source, int type);
    public delegate bool NPCRule(IEntitySource source, int type);

    private static readonly List<ProjectileRule> projectileRules = [];
    private static readonly List<NPCRule> npcRules = [];

    // Spawn-setup callbacks that Infernum and WotG insert into NewProjectile. If we skip a spawn they never run,
    // so they must be cleared or they'd be applied to the next, unrelated projectile.
    private static readonly List<FieldInfo> pendingSpawnActionFields = [];

    public static NPC UpdatingNPC { get; private set; }
    public static Projectile UpdatingProjectile { get; private set; }

    public static int BlockedProjectiles;
    public static int BlockedNPCs;

    public static void AddProjectileRule(ProjectileRule rule) => projectileRules.Add(rule);
    public static void AddNPCRule(NPCRule rule) => npcRules.Add(rule);

    public static void RegisterPendingSpawnAction(Type system, string fieldName)
    {
        FieldInfo field = system?.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (field is not null)
            pendingSpawnActionFields.Add(field);
        else
            CalWotGMPFix.Instance?.Logger.Warn($"Could not find {system?.FullName}.{fieldName}; a blocked spawn may leak its setup onto the next projectile.");
    }

    public override void Load()
    {
        On_NPC.UpdateNPC += TrackNPCUpdate;
        On_Projectile.Update += TrackProjectileUpdate;
        On_Projectile.Kill += TrackProjectileKill;
        On_Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float += BlockProjectile;
        On_NPC.NewNPC += BlockNPC;
    }

    public override void Unload()
    {
        projectileRules.Clear();
        npcRules.Clear();
        pendingSpawnActionFields.Clear();
        UpdatingNPC = null;
        UpdatingProjectile = null;
    }

    private static void TrackNPCUpdate(On_NPC.orig_UpdateNPC orig, NPC self, int i)
    {
        NPC previous = UpdatingNPC;
        UpdatingNPC = self;
        try { orig(self, i); }
        finally { UpdatingNPC = previous; }
    }

    private static void TrackProjectileUpdate(On_Projectile.orig_Update orig, Projectile self, int i)
    {
        Projectile previous = UpdatingProjectile;
        UpdatingProjectile = self;
        try { orig(self, i); }
        finally { UpdatingProjectile = previous; }
    }

    private static void TrackProjectileKill(On_Projectile.orig_Kill orig, Projectile self)
    {
        Projectile previous = UpdatingProjectile;
        UpdatingProjectile = self;
        try { orig(self); }
        finally { UpdatingProjectile = previous; }
    }

    private static int BlockProjectile(On_Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float orig,
        IEntitySource source, float x, float y, float speedX, float speedY, int type, int damage, float knockback, int owner, float ai0, float ai1, float ai2)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient && (UpdatingNPC is not null || UpdatingProjectile is not null))
        {
            foreach (ProjectileRule rule in projectileRules)
            {
                if (!rule(source, type))
                    continue;

                foreach (FieldInfo field in pendingSpawnActionFields)
                    field.SetValue(null, null);
                BlockedProjectiles++;
                return Main.maxProjectiles; // Vanilla's "no projectile was made" result.
            }
        }

        return orig(source, x, y, speedX, speedY, type, damage, knockback, owner, ai0, ai1, ai2);
    }

    private static int BlockNPC(On_NPC.orig_NewNPC orig, IEntitySource source, int x, int y, int type, int start, float ai0, float ai1, float ai2, float ai3, int target)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient && (UpdatingNPC is not null || UpdatingProjectile is not null))
        {
            foreach (NPCRule rule in npcRules)
            {
                if (!rule(source, type))
                    continue;

                BlockedNPCs++;
                return Main.maxNPCs; // Vanilla's "no NPC was made" result.
            }
        }

        return orig(source, x, y, type, start, ai0, ai1, ai2, ai3, target);
    }
}
