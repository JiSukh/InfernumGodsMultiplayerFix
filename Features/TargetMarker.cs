using System.Collections.Generic;
using CalWotGMPFix.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace CalWotGMPFix.Features;

// Draws the head icon of every boss currently targeting a player above that player's head, so everyone can see
// who the boss is going after. Uses npc.target, which the server syncs to all clients.
public class TargetMarkerSystem : ModSystem
{
    private static readonly Dictionary<int, List<int>> headsByPlayer = [];

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Entity Health Bars");
        layers.Insert(index < 0 ? 0 : index + 1, new LegacyGameInterfaceLayer("CalWotGMPFix: Boss Target Marker", Draw, InterfaceScaleType.Game));
    }

    private static bool Draw()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || !(MPFixClientConfig.Instance?.ShowTargetMarker ?? false))
            return true;

        headsByPlayer.Clear();
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (!npc.boss || !npc.HasPlayerTarget)
                continue;

            int head = npc.GetBossHeadTextureIndex();
            if (!headsByPlayer.TryGetValue(npc.target, out List<int> heads))
                headsByPlayer[npc.target] = heads = [];
            if (!heads.Contains(head))
                heads.Add(head);
        }

        float bob = System.MathF.Sin(Main.GlobalTimeWrappedHourly * 5f) * 3f;
        foreach ((int playerIndex, List<int> heads) in headsByPlayer)
        {
            Player player = Main.player[playerIndex];
            if (!player.active || player.dead || player.ghost)
                continue;

            const float spacing = 34f;
            Vector2 start = player.Top - Main.screenPosition + new Vector2(-(heads.Count - 1) * spacing * 0.5f, -46f + bob + player.gfxOffY);
            for (int i = 0; i < heads.Count; i++)
                DrawMarker(start + Vector2.UnitX * spacing * i, heads[i], playerIndex == Main.myPlayer);
        }
        return true;
    }

    private static void DrawMarker(Vector2 center, int headIndex, bool isLocalPlayer)
    {
        Color glow = isLocalPlayer ? new Color(255, 60, 60) : new Color(255, 170, 60);
        if (headIndex >= 0 && headIndex < TextureAssets.NpcHeadBoss.Length && TextureAssets.NpcHeadBoss[headIndex]?.IsLoaded == true)
        {
            Texture2D texture = TextureAssets.NpcHeadBoss[headIndex].Value;
            Vector2 origin = texture.Size() * 0.5f;
            for (int i = 0; i < 4; i++)
                Main.spriteBatch.Draw(texture, center + (MathHelper.PiOver2 * i).ToRotationVector2() * 2f, null, glow * 0.8f, 0f, origin, 1f, SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(texture, center, null, Color.White, 0f, origin, 1f, SpriteEffects.None, 0f);
        }
        else
        {
            Vector2 size = FontAssets.MouseText.Value.MeasureString("!") * 1.4f;
            Utils.DrawBorderString(Main.spriteBatch, "!", center - size * 0.5f, glow, 1.4f);
        }
    }
}
