using BepInEx;
using SlugBase.Features;
using static SlugBase.Features.FeatureTypes;

namespace Weaver
{
    [BepInPlugin("abysslasea.weaver", "Weaver", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string MOD_ID = "abysslasea.weaver";
        public static readonly PlayerFeature<bool> MouseAiming = PlayerBool("weaver/mouse_aiming");

        public void OnEnable()
        {
            MouseAimSystem.Initialize();
            MouseRender.Initialize();
            On.Player.Update += Player_Update;
        }

        public void OnDisable()
        {
            MouseAimSystem.Cleanup();
            MouseRender.Cleanup();
            On.Player.Update -= Player_Update;
        }

        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (MouseAiming.TryGet(self, out bool enabled))
            {
                MouseAimSystem.SetMouseAimEnabled(enabled, self);
            }
        }
    }
}