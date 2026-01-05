using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SlugBase.DataTypes;
using SlugBase.Features;
using tinker.Mouse;
using tinker.shaders;
using tinker.Silk;
using Tinker;
using Tinker.PlayerGraphics_Hooks;
using Tinker.Silk.Bridge;
using UnityEngine;
using static SlugBase.Features.FeatureTypes;

namespace tinker
{
    [BepInPlugin("abysslasea.tinker", "The Tinker", "0.5.0")]
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
        private Harmony _harmony;
        private bool _isInit = false;

        public void OnEnable()
        {
            Instance = this;
            Logger = base.Logger;
            _harmony = new Harmony("abysslasea.tinker");
            _harmony.PatchAll();
            On.RainWorld.OnModsInit += RainWorld_OnModsInit_LoadResources;
            NightVisionHooks.Init();
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
            TinkerLanguageHint.Init();
            On.Player.Update += Player_Update;
        }

        public void OnDisable()
        {
            _harmony?.UnpatchAll("abysslasea.tinker");
            On.RainWorld.OnModsInit -= RainWorld_OnModsInit_LoadResources;
            tinkerSilkData.Cleanup();
            MouseAimSystem.Cleanup();
            MouseRender.Cleanup();
            BrokenSilkManager.Cleanup();
            PlayerGraphicsHooks.Cleanup();
            Instance = null;
        }

        private void RainWorld_OnModsInit_LoadResources(On.RainWorld.orig_OnModsInit orig, RainWorld self)
        {
            orig(self);
            if (_isInit) return;
            _isInit = true;
            try
            {
                string path = AssetManager.ResolveFilePath("shaders/nvshader/nvshader");
                AssetBundle ab = AssetBundle.LoadFromFile(path);
                if (ab != null)
                {
                    Shader shaderSource = ab.LoadAsset<Shader>("Assets/Shaders/NightVision.shader");
                    if (!self.Shaders.ContainsKey("TinkerNightVision"))
                    {
                        self.Shaders.Add("TinkerNightVision", FShader.CreateShader("TinkerNightVision", shaderSource));
                    }
                }
            }
            catch (System.Exception) { }
            Futile.atlasManager.LoadAtlas("atlases/tinker_face");
            MachineConnector.SetRegisteredOI(MOD_ID, new Options_Hook());
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