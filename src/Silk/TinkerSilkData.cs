using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace tinker.Silk
{
    public static class tinkerSilkData
    {
        private static readonly ConditionalWeakTable<Player, SilkPhysics> physicsTable = new();
        private static readonly ConditionalWeakTable<Player, SilkGraphics> graphicsTable = new();
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

        public static SilkGraphics GetGraphics(Player player) => graphicsTable.GetValue(player, p => new SilkGraphics(p));

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
            // For local Tinker players, silk is created immediately via the Get/GetGraphics calls.
            if (IsTinkerPlayer(self))
            {
                Get(self);
                GetGraphics(self);

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

            // Ensure silk and graphics exist for the Tinker player
            SilkPhysics silk = Get(self);
            GetGraphics(self);

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
            if (self.controller == null) return false;
            string controllerType = self.controller.GetType().Name;
            // Standard vanilla controllers: KeyboardController, JoystickController
            // Rain Meadow remote controller: OnlineController
            return controllerType != "KeyboardController" && controllerType != "JoystickController";
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
            float currentEnergy = GetEnergy(player);

            if (currentEnergy >= demand)
            {
                AddEnergy(player, -demand);
                return true;
            }

            if (player.playerState.foodInStomach > 0)
            {
                if (player.playerState.foodInStomach >= 1)
                {
                    player.playerState.foodInStomach -= 1;
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
            Player player = self.owner as Player;
            if (!IsPlayerValid(player))
            {
                return;
            }

            bool deferred = false;
            if (!graphicsTable.TryGetValue(player, out SilkGraphics silkGraphics))
            {
                bool isTinker = IsTinkerPlayer(player);
                bool isRemote = CheckIsRemotePlayer(player);

                if (isTinker || isRemote)
                {
                    deferred = true;
                    silkGraphics = GetGraphics(player);
                    Get(player); // ensure SilkPhysics exists

                    var silk = Get(player);
                    if (isRemote)
                    {
                        silk.isRemote = true;
                        bool pulled = false;
                        if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                            pulled = RainMeadow.RainMeadowBridge.PullSilkState(player, silk);
                    }
                    else
                    {
                        if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                            RainMeadow.RainMeadowBridge.AttachSilkData(player);
                    }
                }
            }

            if (silkGraphics != null)
            {
                silkGraphics.InitiateSprites(sLeaser, rCam);
                if (deferred)
                {
                    var container = rCam.ReturnFContainer("Midground");
                    silkGraphics.AddToContainer(container);
                }
            }
        }

        private static void PlayerGraphicsDrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);
            Player player = self.owner as Player;
            if (!IsPlayerValid(player)) return;

            bool hasGraphics = graphicsTable.TryGetValue(player, out SilkGraphics silkGraphics);

            // Safety fallback: if silk doesn't exist but player IS Tinker, create NOW.
            if (!hasGraphics)
            {
                bool isTinker = IsTinkerPlayer(player);
                bool isRemote = CheckIsRemotePlayer(player);

                if (isTinker || isRemote)
                {
                    silkGraphics = GetGraphics(player);
                    Get(player);

                    if (isRemote)
                    {
                        Get(player).isRemote = true;
                        if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                            RainMeadow.RainMeadowBridge.PullSilkState(player, Get(player));
                    }
                    else
                    {
                        if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                            RainMeadow.RainMeadowBridge.AttachSilkData(player);
                    }

                    silkGraphics.InitiateSprites(sLeaser, rCam);
                    silkGraphics.AddToContainer(rCam.ReturnFContainer("Midground"));
                    hasGraphics = true;
                }
            }

            // Render silk for any player that has silk graphics data
            if (silkGraphics != null)
            {
                silkGraphics.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            }
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
            return player != null;
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

            energyTable.Remove(player);
            exhaustedTable.Remove(player);
        }
    }
}