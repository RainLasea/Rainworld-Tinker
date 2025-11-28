using BepInEx;
using SlugBase.DataTypes;
using SlugBase.Features;
using tinker.Mouse;
using tinker.Silk;
using Tinker.PlayerRender;
using Weaver.Silk.Bridge;
using static SlugBase.Features.FeatureTypes;

namespace tinker
{
    [BepInPlugin("abysslasea.tinker", "The Tinker", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_ID = "abysslasea.tinker";
        public const string SlugName = "tinker";
        public static Plugin Instance;
        public static BepInEx.Logging.ManualLogSource Logger;
        public static PlayerFeature<bool> MouseAiming;
        public static PlayerFeature<bool> SilkFeatureEnabled;
        public static readonly PlayerFeature<PlayerColor> AntennaBaseColor = PlayerCustomColor("AntennaBase");
        public static readonly PlayerFeature<PlayerColor> AntennaTipColor = PlayerCustomColor("AntennaTip");

        public void OnEnable()
        {
            Instance = this;
            Logger = base.Logger;

            MouseAiming = PlayerBool("tinker/mouse_aiming");
            SilkFeatureEnabled = PlayerBool("tinker/silk_enabled");

            tinkerSilkData.Initialize();
            SilkAimInput.Initialize();
            MouseAimSystem.Initialize();
            MouseRender.Initialize();
            SilkBridgeManager.Initialize();
            SilkBridgeGraphics.Initialize();
            AntennaManager.Init();

            On.Player.Update += Player_Update;
        }


        public void OnDisable()
        {
            SilkAimInput.Cleanup();
            tinkerSilkData.Cleanup();
            MouseAimSystem.Cleanup();
            MouseRender.Cleanup();
            AntennaManager.Cleanup();

            On.Player.Update -= Player_Update;
            Instance = null;
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