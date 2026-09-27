using BepInEx;
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
    [BepInPlugin("abysslasea.tinker", "The Tinker", "0.5.5")]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_ID = "abysslasea.tinker";
        public static SlugcatStats.Name SlugName = new SlugcatStats.Name("tinker", false);
        public static Plugin Instance;
        public static readonly PlayerFeature<PlayerColor> AntennaBaseColor = PlayerCustomColor("AntennaBase");
        public static readonly PlayerFeature<PlayerColor> AntennaTipColor = PlayerCustomColor("AntennaTip");

        private Harmony _harmony;
        private bool _isInit = false;
        private bool _hooksRegistered;

        public void OnEnable()
        {
            if (_hooksRegistered) return;
            _hooksRegistered = true;
            Instance = this;
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
            if (!_hooksRegistered) return;
            _hooksRegistered = false;
            _harmony?.UnpatchAll("abysslasea.tinker");
            On.RainWorld.OnModsInit -= RainWorld_OnModsInit_LoadResources;
            On.HUD.HUD.InitSinglePlayerHud -= HUD_InitSinglePlayerHud;
            On.Menu.FastTravelScreen.SpawnSlugcatButtons -= FastTravelScreen_SpawnSlugcatButtons;
            On.Player.Update -= Player_Update;
            NightVisionHooks.Cleanup();
            SilkAimInput.Cleanup();
            SilkClimb.Cleanup();
            SilkBridgeGraphics.Cleanup();
            SilkBridgeManager.Cleanup();
            TinkerLanguageHint.Cleanup();
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

#if RAINMEADOW
            if (tinker.Silk.RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
            {
                try
                {
                    RainMeadow.OnlineState.RegisterState(typeof(tinker.Silk.RainMeadow.TinkerSilkEntityData.TinkerSilkEntityDataState));
                }
                catch (System.Exception)
                {
                }
            }
#endif

            OptionalImprovedInput.Initialize();

            //Tinker.AncientBot.GenerateKeyDrone.RegisterValues();
            //Tinker.AncientBot.GenerateKeyDrone.ApplyHooks();

            LoadShader(self, "shaders/nvshader/nvshader", "Assets/Shaders/NightVision.shader", "TinkerNightVision");
            LoadShader(self, "shaders/hudshader", "Assets/Shaders/SilkJarWave.shader", "SilkJarWave");

            Futile.atlasManager.LoadAtlas("atlases/tinker_face");
            Futile.atlasManager.LoadAtlas("atlases/silkhud");
            Futile.atlasManager.LoadAtlas("atlases/Mouse");
            Futile.atlasManager.LoadAtlas("atlases/Small_Tinker");
            MachineConnector.SetRegisteredOI(MOD_ID, new Options_Hook());
            _isInit = true;
        }

        private void LoadShader(RainWorld rainWorld, string path, string assetName, string shaderName)
        {
            if (rainWorld.Shaders.ContainsKey(shaderName)) return;
            AssetBundle bundle = null;
            try
            {
                bundle = AssetBundle.LoadFromFile(AssetManager.ResolveFilePath(path));
                Shader shader = bundle?.LoadAsset<Shader>(assetName);
                if (shader == null)
                {
                    Logger.LogWarning($"Unable to load {shaderName} from {path}");
                    return;
                }
                rainWorld.Shaders.Add(shaderName, FShader.CreateShader(shaderName, shader));
            }
            catch (System.Exception error)
            {
                Logger.LogError($"Unable to load {shaderName}: {error}");
            }
            finally
            {
                // Keep the shader alive while releasing the bundle file and metadata.
                bundle?.Unload(false);
            }
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);

            bool isTinker = self.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !self.isSlugpup;

            if (isTinker)
            {
                bool shouldEnableMouseAim = Options_Hook.MouseAimEnabled;
                MouseAimSystem.SetMouseAimEnabled(shouldEnableMouseAim, self);
                TheTinker.UpdateDualSenseLight(self);
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
