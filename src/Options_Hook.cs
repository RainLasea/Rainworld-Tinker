using Menu.Remix.MixedUI;
using UnityEngine;

namespace tinker
{
    public class Options_Hook : OptionInterface
    {
        public static Configurable<bool> mouseAimConfig;
        public static Configurable<bool> languageHintConfig;
        public static Configurable<KeyCode> silkShootKeyConfig;
        public static Configurable<bool> nightVisionConfig;
        public static Configurable<bool> antennaRenderConfig;
        public static Configurable<bool> dualSenseLightConfig;
        public static Configurable<string> dualSenseLightColorSourceConfig;
        public static Configurable<string> gamepadBackendConfig;
        public static Configurable<string> gamepadProfileConfig;
        public static Configurable<int> gamepadDeadzonePercentConfig;
        public static Configurable<int> gamepadCursorSpeedConfig;
        public static Configurable<int> gamepadBridgeHoldMsConfig;

        public static bool MouseAimEnabled => mouseAimConfig?.Value ?? true;
        public static bool LanguageHintEnabled => languageHintConfig?.Value ?? true;
        public static KeyCode SilkShootKey => silkShootKeyConfig?.Value ?? KeyCode.Mouse1;
        public static bool NightVisionEnabled => nightVisionConfig?.Value ?? true;
        public static bool AntennaRenderEnabled => antennaRenderConfig?.Value ?? true;
        public static bool DualSenseLightEnabled => dualSenseLightConfig?.Value ?? true;
        public static string DualSenseLightColorSource => dualSenseLightColorSourceConfig?.Value ?? "AntennaBase";
        public static string GamepadBackend => gamepadBackendConfig?.Value ?? "Auto";
        public static string GamepadProfile => gamepadProfileConfig?.Value ?? "Auto";
        public static float GamepadDeadzone => (gamepadDeadzonePercentConfig?.Value ?? 20) / 100f;
        public static float GamepadCursorSpeed => gamepadCursorSpeedConfig?.Value ?? 560f;
        public static float GamepadBridgeHoldSeconds => (gamepadBridgeHoldMsConfig?.Value ?? 135) / 1000f;

        public Options_Hook()
        {
            mouseAimConfig = config.Bind("Tinker_MouseAim_Toggle", true, new ConfigurableInfo("Allows aiming with the mouse when throwing items.", null, "", "Mouse Aim"));
            languageHintConfig = config.Bind("Tinker_LanguageHint_Toggle", true, new ConfigurableInfo("Show tutorial hints for the demo version.", null, "", "Demo Hint"));
            silkShootKeyConfig = config.Bind("Tinker_SilkShoot_Key", KeyCode.Mouse1, new ConfigurableInfo("The key used to fire silk.", null, "", "Silk Key"));
            nightVisionConfig = config.Bind("Tinker_NightVision_Toggle", true, new ConfigurableInfo("Toggle the innate night vision ability.", null, "", "Night Vision"));
            antennaRenderConfig = config.Bind("Tinker_Antenna_Render", true, new ConfigurableInfo("Toggle the visual rendering of antennas.", null, "", "Render Antennas"));
            dualSenseLightConfig = config.Bind("Tinker_DualSense_Light", true, new ConfigurableInfo("Match the assigned DualSense controller light to Antenna Base color.", null, "", "DualSense Light Color"));
            dualSenseLightColorSourceConfig = config.Bind("Tinker_DualSense_Light_ColorSource", "AntennaBase", new ConfigurableInfo("Choose which Tinker color controls the assigned DualSense controller light.", null, "", "DualSense Light Color Source"));
            gamepadBackendConfig = config.Bind("Tinker_Gamepad_Backend", "Auto", new ConfigurableInfo("Choose automatic detection, Rewired raw input, or XInput.", null, "", "Input Backend"));
            gamepadProfileConfig = config.Bind("Tinker_Gamepad_Profile", "Auto", new ConfigurableInfo("Choose automatic, Xbox, or DualSense controller detection.", null, "", "Controller Profile"));
            gamepadDeadzonePercentConfig = config.Bind("Tinker_Gamepad_DeadzonePercent", 20, new ConfigurableInfo("Right stick radial deadzone percentage.", new ConfigAcceptableRange<int>(5, 45), "", "Right Stick Deadzone"));
            gamepadCursorSpeedConfig = config.Bind("Tinker_Gamepad_CursorSpeed", 560, new ConfigurableInfo("Gamepad cursor speed in world units per second.", new ConfigAcceptableRange<int>(100, 1200), "", "Cursor Speed"));
            gamepadBridgeHoldMsConfig = config.Bind("Tinker_Gamepad_BridgeHoldMs", 135, new ConfigurableInfo("How long RT must be held before bridge selection begins.", new ConfigAcceptableRange<int>(50, 600), "", "Bridge Hold Time"));
        }

        public override void Initialize()
        {
            base.Initialize();

            OpTab abilityTab = new OpTab(this, Translate("Abilities"));
            OpTab appearanceTab = new OpTab(this, Translate("Appearance"));
            OpTab miscTab = new OpTab(this, Translate("Misc"));
            OpTab gamepadTab = new OpTab(this, Translate("Gamepad"));

            miscTab.colorButton = Color.red;

            this.Tabs = new OpTab[] { abilityTab, appearanceTab, miscTab, gamepadTab };

            abilityTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), Translate("ABILITIES"), FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), Translate("Customize and toggle the innate powers of the Tinker."), FLabelAlignment.Center, false),

                new OpCheckBox(mouseAimConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, Translate("Enable Mouse Aim Throw")),

                new OpCheckBox(nightVisionConfig, new Vector2(50f, 440f)),
                new OpLabel(90f, 440f, Translate("Enable Night Vision")),

                new OpKeyBinder(silkShootKeyConfig, new Vector2(50f, 390f), new Vector2(100f, 30f)),
                new OpLabel(160f, 390f, Translate("Silk Shoot Key"))
            );

            appearanceTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), Translate("APPEARANCE"), FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), Translate("Visual and cosmetic settings for the Tinker."), FLabelAlignment.Center, false),

                new OpCheckBox(antennaRenderConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, Translate("Enable Antenna Rendering"))
            );

            miscTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), Translate("MISC"), FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), Translate("Extra settings"), FLabelAlignment.Center, false),

                new OpCheckBox(languageHintConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, Translate("Enable Demo Version Hint")),

                new OpCheckBox(dualSenseLightConfig, new Vector2(50f, 440f)),
                new OpLabel(90f, 440f, Translate("Match DualSense Light to Antenna Base")),

                new OpComboBox(dualSenseLightColorSourceConfig, new Vector2(50f, 390f), 180f, new[] { "Body", "Eyes", "AntennaBase", "AntennaTip" }),
                new OpLabel(250f, 390f, Translate("DualSense Light Color Source"))
            );

            gamepadTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), Translate("GAMEPAD"), FLabelAlignment.Center, true),
                new OpComboBox(gamepadBackendConfig, new Vector2(50f, 510f), 180f, new[] { "Auto", "RewiredRaw", "XInput" }),
                new OpLabel(250f, 510f, Translate("Input Backend")),

                new OpComboBox(gamepadProfileConfig, new Vector2(50f, 460f), 180f, new[] { "Auto", "Xbox", "DualSense" }),
                new OpLabel(250f, 460f, Translate("Controller Profile")),

                new OpSlider(gamepadDeadzonePercentConfig, new Vector2(50f, 400f), 180),
                new OpLabel(250f, 400f, Translate("Right Stick Deadzone")),

                new OpSlider(gamepadCursorSpeedConfig, new Vector2(50f, 340f), 180),
                new OpLabel(250f, 340f, Translate("Cursor Speed")),

                new OpSlider(gamepadBridgeHoldMsConfig, new Vector2(50f, 280f), 180),
                new OpLabel(250f, 280f, Translate("Bridge Hold Time"))
            );
        }
    }
}