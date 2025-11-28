using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace tinker.Silk
{
    public static class tinkerSilkData
    {
        private static readonly ConditionalWeakTable<Player, SilkPhysics> physicsTable = new();
        private static readonly ConditionalWeakTable<Player, SilkGraphics> graphicsTable = new();
        private static readonly Dictionary<Player, RoomCamera.SpriteLeaser> spriteLeasers = new();

        public static SilkPhysics Get(Player player) => physicsTable.GetValue(player, p => new SilkPhysics(p));
        public static SilkGraphics GetGraphics(Player player) => graphicsTable.GetValue(player, p => new SilkGraphics(p));

        public static void Initialize()
        {
            On.Player.ctor += PlayerCtor;
            On.Player.Update += PlayerUpdate;
            On.Player.Destroy += PlayerDestroy;
            On.PlayerGraphics.InitiateSprites += PlayerGraphicsInitiateSprites;
            On.PlayerGraphics.DrawSprites += PlayerGraphicsDrawSprites;
            On.PlayerGraphics.AddToContainer += PlayerGraphicsAddToContainer;
        }

        public static void Cleanup()
        {
            On.Player.ctor -= PlayerCtor;
            On.Player.Update -= PlayerUpdate;
            On.Player.Destroy -= PlayerDestroy;
            On.PlayerGraphics.InitiateSprites -= PlayerGraphicsInitiateSprites;
            On.PlayerGraphics.DrawSprites -= PlayerGraphicsDrawSprites;
            On.PlayerGraphics.AddToContainer -= PlayerGraphicsAddToContainer;

            spriteLeasers.Clear();
        }

        private static void PlayerCtor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);

            Get(self);
            GetGraphics(self);
        }

        private static void PlayerUpdate(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (physicsTable.TryGetValue(self, out SilkPhysics silk))
                silk.Update();
        }

        private static void PlayerDestroy(On.Player.orig_Destroy orig, Player self)
        {
            CleanupPlayerData(self);
            orig(self);
        }

        private static void PlayerGraphicsInitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            orig(self, sLeaser, rCam);

            Player player = self.owner as Player;
            if (!IsPlayerValid(player)) return;

            if (graphicsTable.TryGetValue(player, out SilkGraphics silkGraphics))
            {
                spriteLeasers[player] = sLeaser;
                silkGraphics.InitiateSprites(sLeaser, rCam);
            }
        }

        private static void PlayerGraphicsDrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            Player player = self.owner as Player;
            if (!IsPlayerValid(player)) return;

            if (graphicsTable.TryGetValue(player, out SilkGraphics silkGraphics))
                silkGraphics.DrawSprites(sLeaser, rCam, timeStacker, camPos);
        }

        private static void PlayerGraphicsAddToContainer(On.PlayerGraphics.orig_AddToContainer orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
        {
            orig(self, sLeaser, rCam, newContatiner);

            Player player = self.owner as Player;
            if (!IsPlayerValid(player)) return;

            if (graphicsTable.TryGetValue(player, out SilkGraphics silkGraphics))
                silkGraphics.AddToContainer(newContatiner);
        }

        private static bool IsPlayerValid(Player player)
        {
            if (player == null) return false;
            if (Plugin.SilkFeatureEnabled != null &&
                Plugin.SilkFeatureEnabled.TryGet(player, out bool enabled) && !enabled)
                return false;
            return true;
        }

        private static void CleanupPlayerData(Player player)
        {

            if (graphicsTable.TryGetValue(player, out SilkGraphics graphics))
            {
                graphics.RemoveSprites();
                graphicsTable.Remove(player);
            }


            if (physicsTable.TryGetValue(player, out SilkPhysics physics))
                physicsTable.Remove(player);


            spriteLeasers.Remove(player);
        }
    }
}