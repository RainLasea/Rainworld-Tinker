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
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker
{
    [BepInPlugin("abysslasea.tinker", "The Tinker", "0.5.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_ID = "abysslasea.tinker";
        public static SlugcatStats.Name SlugName = new SlugcatStats.Name("tinker", false);
        public static Plugin Instance;
        public static new ManualLogSource Logger;
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
            On.HUD.HUD.InitSinglePlayerHud += HUD_InitSinglePlayerHud;
            On.Menu.FastTravelScreen.SpawnSlugcatButtons += FastTravelScreen_SpawnSlugcatButtons;
            NightVisionHooks.Init();
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
            On.Menu.FastTravelScreen.SpawnSlugcatButtons += FastTravelScreen_SpawnSlugcatButtons;
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

            try
            {
                string hudPath = AssetManager.ResolveFilePath("shaders/hudshader");
                AssetBundle hudAb = AssetBundle.LoadFromFile(hudPath);
                if (hudAb != null)
                {
                    Shader silkShaderSource = hudAb.LoadAsset<Shader>("Assets/Shaders/SilkJarWave.shader");
                    if (!self.Shaders.ContainsKey("SilkJarWave"))
                    {
                        self.Shaders.Add("SilkJarWave", FShader.CreateShader("SilkJarWave", silkShaderSource));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError("Failed to load SilkJarWave Shader: " + ex.Message);
            }

            Futile.atlasManager.LoadAtlas("atlases/tinker_face");
            Futile.atlasManager.LoadAtlas("atlases/silkhud");
            Futile.atlasManager.LoadAtlas("atlases/Mouse");
            Futile.atlasManager.LoadAtlas("atlases/Small_Tinker");
            MachineConnector.SetRegisteredOI(MOD_ID, new Options_Hook());
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            if (self.graphicsModule == null)
                self.InitiateGraphicsModule();
            bool isTinker = self.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !self.isSlugpup;

            if (isTinker)
            {
                bool shouldEnableMouseAim = Options_Hook.MouseAimEnabled;
                MouseAimSystem.SetMouseAimEnabled(shouldEnableMouseAim, self);
            }
        }
        private void HUD_InitSinglePlayerHud(On.HUD.HUD.orig_InitSinglePlayerHud orig, HUD.HUD self, RoomCamera cam)
        {
            orig(self, cam);
            if (self.owner is Player player && player.SlugCatClass == Plugin.SlugName)
            {
                self.AddPart(new tinker.HUD_Hook.SilkFuelMeter(self, self.fContainers[1], player));
            }
        }

        private void FastTravelScreen_SpawnSlugcatButtons(On.Menu.FastTravelScreen.orig_SpawnSlugcatButtons orig, Menu.FastTravelScreen self)
        {
            orig(self);
            for (int i = 0; i < self.slugcatButtons.Count; i++)
            {
                if (self.slugcatButtons[i].signalText == "SLUG" + Plugin.SlugName.value)
                {
                    FSprite icon = self.slugcatLabels[i];

                    icon.element = Futile.atlasManager.GetElementWithName("Small_Tinker");
                    icon.color = Color.white;
                }
            }
        }
    }
}