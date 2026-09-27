using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace tinker.Silk
{
    public static class tinkerSilkData
    {
        private static readonly ConditionalWeakTable<Player, SilkPhysics> physicsTable = new();
        private static ConditionalWeakTable<RoomCamera.SpriteLeaser, SilkGraphics> graphicsTable = new();
        private static readonly List<WeakReference<SilkGraphics>> trackedGraphics = new();
        private static readonly ConditionalWeakTable<Player, StrongBox<float>> energyTable = new();
        private static readonly ConditionalWeakTable<Player, StrongBox<bool>> exhaustedTable = new();

        /// <summary>
        /// Returns true if the Player is a tinker.
        /// </summary>
        public static bool IsTinkerPlayer(Player player)
        {
            return player != null &&
                   player.slugcatStats?.name.ToString() == Plugin.SlugName.ToString() &&
                   !player.isSlugpup;
        }

        public static float GetEnergy(Player player) => energyTable.GetValue(player, p => new StrongBox<float>(100f)).Value;

        public static bool GetExhausted(Player player) => exhaustedTable.GetValue(player, p => new StrongBox<bool>(false)).Value;

        public static void SetExhausted(Player player, bool value)
        {
            exhaustedTable.GetValue(player, p => new StrongBox<bool>(false)).Value = value;
        }

        public static void SetEnergy(Player player, float value, bool isEating = false)
        {
            var box = energyTable.GetValue(player, p => new StrongBox<float>(100f));
            bool exhausted = GetExhausted(player);
            float limit = 100f;

            if (!exhausted && (box.Value > 100f || (isEating && value > 100f)))
            {
                limit = 140f;
            }

            box.Value = Mathf.Clamp(value, 0f, limit);

            if (exhausted && box.Value >= 100f)
            {
                SetExhausted(player, false);
            }
        }

        public static void AddEnergy(Player player, float amount, bool isEating = false) => SetEnergy(player, GetEnergy(player) + amount, isEating);

        public static SilkPhysics Get(Player player)
        {
            return physicsTable.GetValue(player, p => new SilkPhysics(p));
        }

        private static SilkGraphics GetGraphics(Player player, RoomCamera.SpriteLeaser leaser) =>
            graphicsTable.GetValue(leaser, key =>
            {
                var graphics = new SilkGraphics(player);
                trackedGraphics.RemoveAll(reference => !reference.TryGetTarget(out _));
                trackedGraphics.Add(new WeakReference<SilkGraphics>(graphics));
                return graphics;
            });

        public static void Initialize()
        {
            On.Player.ctor += PlayerCtor;
            On.Player.Update += PlayerUpdate;
            On.Player.Destroy += PlayerDestroy;
            On.Player.AddFood += Player_AddFood;
            On.PlayerGraphics.InitiateSprites += PlayerGraphicsInitiateSprites;
            On.PlayerGraphics.DrawSprites += PlayerGraphicsDrawSprites;
            On.PlayerGraphics.AddToContainer += PlayerGraphicsAddToContainer;
        }

        public static void Cleanup()
        {
            foreach (var reference in trackedGraphics)
                if (reference.TryGetTarget(out var graphics)) graphics.RemoveSprites();
            trackedGraphics.Clear();
            graphicsTable = new();
            On.Player.ctor -= PlayerCtor;
            On.Player.Update -= PlayerUpdate;
            On.Player.Destroy -= PlayerDestroy;
            On.Player.AddFood -= Player_AddFood;
            On.PlayerGraphics.InitiateSprites -= PlayerGraphicsInitiateSprites;
            On.PlayerGraphics.DrawSprites -= PlayerGraphicsDrawSprites;
            On.PlayerGraphics.AddToContainer -= PlayerGraphicsAddToContainer;
        }

        private static void PlayerCtor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);

            // Always create energy tracking for food-to-energy conversion (needed for all players)
            energyTable.Add(self, new StrongBox<float>(100f));
            exhaustedTable.Add(self, new StrongBox<bool>(false));

            // Silk physics + graphics are NOT created here for remote players (Rain Meadow).
            // Remote Tinker players may not have their slugcat type set yet at ctor time.
            // Silk will be lazily created on first Player.Update when slugcat type is correct.
            // Local silk physics is created immediately; sprites are created per camera later.
            if (IsTinkerPlayer(self))
            {
                Get(self);

                // CRITICAL: Attach EntityData for Rain Meadow sync immediately at ctor time.
                // On the host, the OnlinePhysicalObject map entry is set up by RM before Player.ctor runs.
                // If we delay AttachSilkData to PlayerUpdate, the first RM EntityState snapshot
                // might be sent without our EntityData, and the remote client never receives it.
                if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                {
                    RainMeadow.RainMeadowBridge.AttachSilkData(self);
                }
            }
        }

        private static void PlayerUpdate(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (!IsTinkerPlayer(self)) return;

            // Ensure silk physics exists for the Tinker player.
            SilkPhysics silk = Get(self);

            // Dynamically update remote state every frame
            bool isRemote = CheckIsRemotePlayer(self);
            silk.isRemote = isRemote;

            // Run physics (skips internally if isRemote is true)
            silk.Update();

            // Sync silk state with Rain Meadow every frame
            if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
            {
                if (isRemote)
                {
                    RainMeadow.RainMeadowBridge.PullSilkState(self, silk);
                }
                else
                {
                    // Safety: ensure EntityData is attached (may have been missed in PlayerCtor)
                    if (!RainMeadow.RainMeadowBridge.HasSilkData(self))
                    {
                        RainMeadow.RainMeadowBridge.AttachSilkData(self);
                    }
                    RainMeadow.RainMeadowBridge.PushSilkState(self, silk);
                }
            }
        }

        /// <summary>
        /// Check if this player is a remote multiplayer player (Rain Meadow's OnlineController).
        /// Safe when Rain Meadow is not installed (returns false).
        /// </summary>
        private static bool CheckIsRemotePlayer(Player self)
        {
            if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
            {
                return RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self);
            }
            // Vanilla cutscenes and other mods also provide PlayerController instances.
            // A custom controller alone is not evidence of remote network ownership.
            return false;
        }

        private static void Player_AddFood(On.Player.orig_AddFood orig, Player self, int add)
        {
            // Save food level BEFORE orig() so we can calculate energy correctly
            int foodBefore = self.playerState.foodInStomach;
            int maxFood = self.MaxFoodInStomach;

            orig(self, add);

            // Only track food energy for Tinker players
            if (!IsTinkerPlayer(self)) return;

            float energyToAdd = 0f;
            for (int i = 0; i < add; i++)
            {
                int virtualStomach = foodBefore + i;
                if (virtualStomach < maxFood)
                {
                    energyToAdd += 20f;
                }
                else
                {
                    energyToAdd += 10f;
                }
            }
            AddEnergy(self, energyToAdd, true);
        }

        public static bool RequestEnergy(Player player, float demand)
        {
            if (player == null || float.IsNaN(demand) || float.IsInfinity(demand) || demand < 0f)
                return false;
            float currentEnergy = GetEnergy(player);

            if (currentEnergy >= demand)
            {
                AddEnergy(player, -demand);
                return true;
            }

            if (demand <= 100f && player.playerState.foodInStomach > 0)
            {
                if (player.playerState.foodInStomach >= 1)
                {
                    player.SubtractFood(1);
                    SetEnergy(player, 100f);
                    AddEnergy(player, -demand);
                    SetExhausted(player, true);
                    return true;
                }
            }

            return false;
        }

        private static void PlayerDestroy(On.Player.orig_Destroy orig, Player self)
        {
            CleanupPlayerData(self);
            orig(self);
        }

        private static void PlayerGraphicsInitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            orig(self, sLeaser, rCam);
            if (self.owner is Player player && IsTinkerPlayer(player))
                GetGraphics(player, sLeaser).InitiateSprites(sLeaser, rCam);
        }

        private static void PlayerGraphicsDrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);
            if (sLeaser.deleteMeNextFrame || self.owner is not Player player ||
                player.slatedForDeletetion || player.room != rCam.room || !IsTinkerPlayer(player)) return;

            // Remote players may acquire their slugcat type after sprite initialization.
            var graphics = GetGraphics(player, sLeaser);
            if (!graphics.IsSpritesInitiated) graphics.InitiateSprites(sLeaser, rCam);
            graphics.DrawSprites(sLeaser, rCam, timeStacker, camPos);
        }

        private static void PlayerGraphicsAddToContainer(On.PlayerGraphics.orig_AddToContainer orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
        {
            orig(self, sLeaser, rCam, newContatiner);
            if (graphicsTable.TryGetValue(sLeaser, out var graphics))
                graphics.AddToContainer(sLeaser, newContatiner ?? rCam.ReturnFContainer("Midground"));
        }

        private static void CleanupPlayerData(Player player)
        {
            foreach (var reference in trackedGraphics)
                if (reference.TryGetTarget(out var graphics) && graphics.player == player)
                    graphics.RemoveSprites();
            if (physicsTable.TryGetValue(player, out SilkPhysics physics))
                physicsTable.Remove(player);

            energyTable.Remove(player);
            exhaustedTable.Remove(player);
        }
    }
}
