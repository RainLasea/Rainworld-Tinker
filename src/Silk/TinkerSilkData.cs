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
        private static readonly Dictionary<Player, RoomCamera.SpriteLeaser> spriteLeasers = new();

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

            spriteLeasers.Clear();
        }

        private static void PlayerCtor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);
            energyTable.Add(self, new StrongBox<float>(100f));
            exhaustedTable.Add(self, new StrongBox<bool>(false));
            Get(self);
            GetGraphics(self);
        }

        private static void PlayerUpdate(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (physicsTable.TryGetValue(self, out SilkPhysics silk))
                silk.Update();
        }

        private static void Player_AddFood(On.Player.orig_AddFood orig, Player self, int add)
        {
            int foodBefore = self.playerState.foodInStomach;
            int maxFood = self.MaxFoodInStomach;
            bool isExhausted = GetExhausted(self);

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
            orig(self, add);
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
                float missingEnergy = demand - currentEnergy;
                float foodNeeded = demand / 10f;

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
            spriteLeasers.Remove(player);
        }
    }
}