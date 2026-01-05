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

        public static bool MouseAimEnabled => mouseAimConfig?.Value ?? true;
        public static bool LanguageHintEnabled => languageHintConfig?.Value ?? true;
        public static KeyCode SilkShootKey => silkShootKeyConfig?.Value ?? KeyCode.Mouse1;
        public static bool NightVisionEnabled => nightVisionConfig?.Value ?? true;
        public static bool AntennaRenderEnabled => antennaRenderConfig?.Value ?? true;

        public Options_Hook()
        {
            mouseAimConfig = config.Bind("Tinker_MouseAim_Toggle", true, new ConfigurableInfo("Allows aiming with the mouse when throwing items.", null, "", "Mouse Aim"));
            languageHintConfig = config.Bind("Tinker_LanguageHint_Toggle", true, new ConfigurableInfo("Show tutorial hints for the demo version.", null, "", "Demo Hint"));
            silkShootKeyConfig = config.Bind("Tinker_SilkShoot_Key", KeyCode.Mouse1, new ConfigurableInfo("The key used to fire silk.", null, "", "Silk Key"));
            nightVisionConfig = config.Bind("Tinker_NightVision_Toggle", true, new ConfigurableInfo("Toggle the innate night vision ability.", null, "", "Night Vision"));
            antennaRenderConfig = config.Bind("Tinker_Antenna_Render", true, new ConfigurableInfo("Toggle the visual rendering of antennas.", null, "", "Render Antennas"));
        }

        public override void Initialize()
        {
            base.Initialize();

            OpTab abilityTab = new OpTab(this, "Abilities");
            OpTab appearanceTab = new OpTab(this, "Appearance");
            OpTab miscTab = new OpTab(this, "Misc");

            miscTab.colorButton = Color.red;

            this.Tabs = new OpTab[] { abilityTab, appearanceTab, miscTab };

            abilityTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), "ABILITIES", FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), "Customize and toggle the innate powers of the Tinker.", FLabelAlignment.Center, false),

                new OpCheckBox(mouseAimConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, "Enable Mouse Aim Throw"),

                new OpCheckBox(nightVisionConfig, new Vector2(50f, 440f)),
                new OpLabel(90f, 440f, "Enable Night Vision"),

                new OpKeyBinder(silkShootKeyConfig, new Vector2(50f, 390f), new Vector2(100f, 30f)),
                new OpLabel(160f, 390f, "Silk Shoot Key")
            );

            appearanceTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), "APPEARANCE", FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), "Visual and cosmetic settings for the Tinker.", FLabelAlignment.Center, false),

                new OpCheckBox(antennaRenderConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, "Enable Antenna Rendering")
            );

            miscTab.AddItems(
                new OpLabel(new Vector2(0f, 570f), new Vector2(600f, 30f), "MISC", FLabelAlignment.Center, true),
                new OpLabel(new Vector2(0f, 540f), new Vector2(600f, 20f), "Extra settings", FLabelAlignment.Center, false),

                new OpCheckBox(languageHintConfig, new Vector2(50f, 490f)),
                new OpLabel(90f, 490f, "Enable Demo Version Hint")
            );
        }
    }
}