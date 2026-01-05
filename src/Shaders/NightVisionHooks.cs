using UnityEngine;
using System.Reflection;

namespace tinker.shaders
{
    public static class NightVisionHooks
    {
        public static float nvIntensity = 0f;
        private static FSprite nvOverlay;
        private static MaterialPropertyBlock _propBlock;
        private static FieldInfo _renderLayerField;
        private static FieldInfo _meshRendererField;

        public static void Init()
        {
            _propBlock = new MaterialPropertyBlock();
            _renderLayerField = typeof(FFacetNode).GetField("_renderLayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            On.Player.Update += Player_Update;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
        }

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (self.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !self.isSlugpup)
            {
                if (Options_Hook.NightVisionEnabled && self.room != null && self.room.world.region.name == "SH")
                    nvIntensity = Mathf.Min(1f, nvIntensity + 0.015f);
                else
                    nvIntensity = Mathf.Max(0f, nvIntensity - 0.015f);
            }
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);

            if (nvIntensity <= 0.01f)
            {
                if (nvOverlay != null) nvOverlay.isVisible = false;
                return;
            }

            if (nvOverlay == null || nvOverlay.container == null)
            {
                nvOverlay = new FSprite("Futile_White");
                self.ReturnFContainer("Foreground").AddChild(nvOverlay);
            }

            if (self.game.rainWorld.Shaders.TryGetValue("TinkerNightVision", out FShader nvShader))
            {
                nvOverlay.isVisible = true;
                nvOverlay.shader = nvShader;

                nvOverlay.SetPosition(self.sSize.x / 2f, self.sSize.y / 2f);
                nvOverlay.scaleX = self.sSize.x / 16f;
                nvOverlay.scaleY = self.sSize.y / 16f;

                UpdateShaderParams(nvOverlay);
            }
        }

        private static void UpdateShaderParams(FSprite sprite)
        {
            try
            {
                object renderLayer = _renderLayerField?.GetValue(sprite);
                if (renderLayer != null)
                {
                    if (_meshRendererField == null)
                        _meshRendererField = renderLayer.GetType().GetField("_meshRenderer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    MeshRenderer renderer = _meshRendererField?.GetValue(renderLayer) as MeshRenderer;
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_propBlock);
                        _propBlock.SetFloat("_Intensity", nvIntensity);
                        _propBlock.SetFloat("_Gain", 2.0f);
                        renderer.SetPropertyBlock(_propBlock);
                    }
                }
            }
            catch { }
        }
    }
}