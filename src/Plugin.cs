using BepInEx;
using BepInEx.Logging;
using SlugBase.DataTypes;
using SlugBase.Features;
using tinker.Mouse;
using tinker.Silk;
using Tinker;
using Tinker.PlayerGraphics_Hooks;
using Tinker.Silk.Bridge;
using static SlugBase.Features.FeatureTypes;

namespace tinker
{
    [BepInPlugin("abysslasea.tinker", "The Tinker", "0.4.6")]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_ID = "abysslasea.tinker";
        public static SlugcatStats.Name SlugName = new SlugcatStats.Name("tinker", false);
        public static Plugin Instance;
        public static new ManualLogSource Logger;
        public static PlayerFeature<bool> MouseAiming;

        public static PlayerFeature<bool> SilkFeatureEnabled;
        public static readonly PlayerFeature<PlayerColor> AntennaBaseColor = PlayerCustomColor("AntennaBase");
        public static readonly PlayerFeature<PlayerColor> AntennaTipColor = PlayerCustomColor("AntennaTip");

        public void OnEnable()
        {
            Instance = this;
            Logger = base.Logger;

            On.RainWorld.OnModsInit += RainWorld_OnModsInit_LoadResources;

            MouseAiming = PlayerBool("tinker/mouse_aiming");
            SilkFeatureEnabled = PlayerBool("tinker/silk_enabled");

            tinkerSilkData.Initialize();
            SilkAimInput.Initialize();
            MouseAimSystem.Initialize();
            MouseRender.Initialize();
            SilkBridgeManager.Initialize();
            SilkBridgeGraphics.Initialize();
            BrokenSilkManager.Initialize();
            SilkClimb.Init();
            PlayerGraphicsHooks.Init();

            //Demo
            TinkerLanguageHint.Init();

            On.Player.Update += Player_Update;
            // Creature_Update 已移到 BrokenSilkManager
        }

        public void OnDisable()
        {
            On.RainWorld.OnModsInit -= RainWorld_OnModsInit_LoadResources;

            SilkAimInput.Cleanup();
            tinkerSilkData.Cleanup();
            MouseAimSystem.Cleanup();
            MouseRender.Cleanup();
            BrokenSilkManager.Cleanup();

            PlayerGraphicsHooks.Cleanup();

            On.Player.Update -= Player_Update;
            Instance = null;
        }

        private void RainWorld_OnModsInit_LoadResources(On.RainWorld.orig_OnModsInit orig, RainWorld self)
        {
            orig(self);
            Futile.atlasManager.LoadAtlas("atlases/tinker_face");
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (self.graphicsModule == null)
                self.InitiateGraphicsModule();

            if (MouseAiming.TryGet(self, out bool mouseEnabled) && mouseEnabled)
                MouseAimSystem.SetMouseAimEnabled(true, self);
        }
    }
}