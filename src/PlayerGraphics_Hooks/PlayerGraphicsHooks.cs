using RWCustom;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using tinker;
using UnityEngine;

namespace Tinker.PlayerGraphics_Hooks
{
    public static class PlayerGraphicsHooks
    {
        internal static ConcurrentDictionary<Player, AntennaSystem> activeSystems = new();
        internal static readonly ConditionalWeakTable<PlayerGraphics, TailModule> tailData = new();

        private static bool hooksRegistered;

        public static void Init()
        {
            if (hooksRegistered) return;

            On.Player.Update += Player_Update;
            On.PlayerGraphics.Update += PlayerGraphics_Update;
            On.PlayerGraphics.Reset += PlayerGraphics_Reset;
            On.PlayerGraphics.InitiateSprites += PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer += PlayerGraphics_AddToContainer;

            hooksRegistered = true;
        }

        private static void PlayerGraphics_Reset(On.PlayerGraphics.orig_Reset orig, PlayerGraphics self)
        {
            orig(self);

            if (self.owner is Player player &&
                activeSystems.TryGetValue(player, out var system))
            {
                system.Reset();
            }
        }

        private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);

            if (self.owner is Player player)
            {
                if (activeSystems.TryGetValue(player, out var system))
                    system.Update();

                if (tailData.TryGetValue(self, out var tail))
                    tail.Update();
            }
        }

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            bool shouldHave = ShouldHaveAntenna(self);
            bool has = activeSystems.ContainsKey(self);

            if (!shouldHave && has)
            {
                if (activeSystems.TryRemove(self, out var system))
                    system.RemoveSprites();
            }
        }

        private static void PlayerGraphics_InitiateSprites(
            On.PlayerGraphics.orig_InitiateSprites orig,
            PlayerGraphics self,
            RoomCamera.SpriteLeaser sLeaser,
            RoomCamera rCam)
        {
            orig(self, sLeaser, rCam);

            if (self.owner is not Player player) return;
            if (player.slugcatStats?.name.ToString() != Plugin.SlugName.ToString()) return;

            if (!tailData.TryGetValue(self, out var tail))
            {
                tail = new TailModule(self);
                tailData.Add(self, tail);
            }
            tail.InitiateSprites(sLeaser, rCam);

            if (ShouldHaveAntenna(player))
            {
                if (!activeSystems.ContainsKey(player))
                    activeSystems[player] = new AntennaSystem(self, player);

                activeSystems[player].InitiateSprites(sLeaser, rCam);
            }
        }

        private static void PlayerGraphics_DrawSprites(
            On.PlayerGraphics.orig_DrawSprites orig,
            PlayerGraphics self,
            RoomCamera.SpriteLeaser sLeaser,
            RoomCamera rCam,
            float timeStacker,
            Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);

            if (self.owner is not Player player) return;

            if (tailData.TryGetValue(self, out var tail))
                tail.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            if (activeSystems.TryGetValue(player, out var system))
                system.DrawSprites(sLeaser, rCam, timeStacker, camPos);
        }

        private static void PlayerGraphics_AddToContainer(
            On.PlayerGraphics.orig_AddToContainer orig,
            PlayerGraphics self,
            RoomCamera.SpriteLeaser sLeaser,
            RoomCamera rCam,
            FContainer container)
        {
            orig(self, sLeaser, rCam, container);

            if (tailData.TryGetValue(self, out var tail))
                tail.AddToContainer(sLeaser, rCam, container);

            if (self.owner is Player player &&
                activeSystems.TryGetValue(player, out var system))
            {
                system.AddToContainer(sLeaser, rCam, container);
            }
        }

        public static bool ShouldHaveAntenna(Player player)
        {
            return player != null &&
                   player.slugcatStats?.name.ToString() == Plugin.SlugName.ToString() &&
                   player.room != null &&
                   player.graphicsModule != null &&
                   player.bodyMode != Player.BodyModeIndex.ClimbIntoShortCut;
        }

        public static void Cleanup()
        {
            foreach (var kv in activeSystems)
                kv.Value.RemoveSprites();

            activeSystems.Clear();

            On.Player.Update -= Player_Update;
            On.PlayerGraphics.Update -= PlayerGraphics_Update;
            On.PlayerGraphics.Reset -= PlayerGraphics_Reset;
            On.PlayerGraphics.InitiateSprites -= PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.DrawSprites -= PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer -= PlayerGraphics_AddToContainer;

            hooksRegistered = false;
        }
    }
}
