using RWCustom;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using tinker;
using UnityEngine;

namespace Tinker.PlayerGraphics_Hooks
{
    public static class PlayerGraphicsHooks
    {
        internal static ConditionalWeakTable<PlayerGraphics, AntennaSystem> activeSystems = new();
        private static readonly List<WeakReference<PlayerGraphics>> trackedGraphics = new();
        internal static ConditionalWeakTable<PlayerGraphics, TailModule> tailData = new();
        internal static ConditionalWeakTable<PlayerGraphics, SpiderArmsModule> armData = new();

        private static bool hooksRegistered;

        public static void Init()
        {
            if (hooksRegistered) return;

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
                activeSystems.TryGetValue(self, out var system))
            {
                system.Reset();
            }
        }

        private static void PlayerGraphics_Update(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
        {
            orig(self);

            if (self.owner is Player player)
            {
                if (activeSystems.TryGetValue(self, out var system))
                    system.Update();

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
                trackedGraphics.RemoveAll(reference => !reference.TryGetTarget(out _));
                trackedGraphics.Add(new WeakReference<PlayerGraphics>(self));
            }
            tail.InitiateSprites(sLeaser, rCam);

            if (!armData.TryGetValue(self, out var arms))
            {
                arms = new SpiderArmsModule(self, player);
                armData.Add(self, arms);
            }
            arms.InitiateSprites(sLeaser, rCam);

            if (ShouldHaveAntenna(player))
            {
                var system = activeSystems.GetValue(self, graphics => new AntennaSystem(graphics, player));
                system.InitiateSprites(sLeaser, rCam);
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

            if (self.owner is not Player player || sLeaser.deleteMeNextFrame ||
                player.slatedForDeletetion || player.room != rCam.room) return;

            if (tailData.TryGetValue(self, out var tail))
                tail.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            if (armData.TryGetValue(self, out var arms))
                arms.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            if (activeSystems.TryGetValue(self, out var system))
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

            if (armData.TryGetValue(self, out var arms))
                arms.AddToContainer(sLeaser, rCam, container);

            if (self.owner is Player player &&
                activeSystems.TryGetValue(self, out var system))
            {
                system.AddToContainer(sLeaser, rCam, container);
            }
        }

        public static bool ShouldHaveAntenna(Player player)
        {
            return player != null &&
                   player.slugcatStats?.name.ToString() == Plugin.SlugName.ToString() &&
                   player.room != null &&
                   player.graphicsModule != null;
        }

        public static void Cleanup()
        {
            foreach (var reference in trackedGraphics)
            {
                if (!reference.TryGetTarget(out var graphics)) continue;
                if (activeSystems.TryGetValue(graphics, out var antenna)) antenna.RemoveSprites();
                if (tailData.TryGetValue(graphics, out var tail)) tail.Cleanup();
                if (armData.TryGetValue(graphics, out var arms)) arms.Cleanup();
            }
            trackedGraphics.Clear();
            activeSystems = new();
            tailData = new();
            armData = new();

            On.PlayerGraphics.Update -= PlayerGraphics_Update;
            On.PlayerGraphics.Reset -= PlayerGraphics_Reset;
            On.PlayerGraphics.InitiateSprites -= PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.DrawSprites -= PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer -= PlayerGraphics_AddToContainer;

            hooksRegistered = false;
        }
    }
}
