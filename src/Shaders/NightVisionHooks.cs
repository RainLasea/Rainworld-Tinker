using UnityEngine;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace tinker.shaders
{
    public static class NightVisionHooks
    {
        private sealed class CameraState
        {
            public float intensity;
            public FSprite overlay;
        }

        private static ConditionalWeakTable<RoomCamera, CameraState> cameraStates = new();
        private static readonly List<WeakReference<CameraState>> trackedStates = new();
        private static MaterialPropertyBlock _propBlock;
        private static FieldInfo _renderLayerField;
        private static FieldInfo _meshRendererField;

        public static void Init()
        {
            _propBlock = new MaterialPropertyBlock();
            _renderLayerField = typeof(FFacetNode).GetField("_renderLayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            On.RoomCamera.Update += RoomCamera_Update;
            On.RainWorldGame.ShutDownProcess += Game_ShutDownProcess;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
        }

        public static void Cleanup()
        {
            On.RoomCamera.Update -= RoomCamera_Update;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.RainWorldGame.ShutDownProcess -= Game_ShutDownProcess;
            ClearOverlays();
        }

        private static void ClearOverlays()
        {
            foreach (var reference in trackedStates)
                if (reference.TryGetTarget(out var state)) state.overlay?.RemoveFromContainer();
            trackedStates.Clear();
            cameraStates = new();
        }

        private static void Game_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
        {
            ClearOverlays();
            orig(self);
        }

        private static void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
        {
            orig(self);
            var state = cameraStates.GetValue(self, camera =>
            {
                var value = new CameraState();
                trackedStates.RemoveAll(reference => !reference.TryGetTarget(out _));
                trackedStates.Add(new WeakReference<CameraState>(value));
                return value;
            });
            var player = self.followAbstractCreature?.realizedCreature as Player;
            bool enabled = player != null && player.room == self.room &&
                player.slugcatStats.name == Plugin.SlugName && !player.isSlugpup &&
                self.game.IsStorySession && Options_Hook.NightVisionEnabled &&
                self.room?.world?.region?.name == "SH";
            state.intensity = Mathf.MoveTowards(state.intensity, enabled ? 1f : 0f, 0.015f);
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            if (!cameraStates.TryGetValue(self, out var state)) return;
            if (self.room == null || state.intensity <= 0.01f ||
                !self.game.rainWorld.Shaders.TryGetValue("TinkerNightVision", out FShader nvShader))
            {
                if (state.overlay != null) state.overlay.isVisible = false;
                return;
            }
            if (state.overlay == null)
                state.overlay = new FSprite("Futile_White");
            if (state.overlay.container == null)
                self.ReturnFContainer("Foreground").AddChild(state.overlay);

            state.overlay.isVisible = true;
            state.overlay.shader = nvShader;
            state.overlay.SetPosition(self.sSize.x / 2f, self.sSize.y / 2f);
            state.overlay.scaleX = self.sSize.x / 16f;
            state.overlay.scaleY = self.sSize.y / 16f;
            UpdateShaderParams(state.overlay, state.intensity);
        }

        private static void UpdateShaderParams(FSprite sprite, float intensity)
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
                        _propBlock.SetFloat("_Intensity", intensity);
                        _propBlock.SetFloat("_Gain", 2.0f);
                        renderer.SetPropertyBlock(_propBlock);
                    }
                }
            }
            catch { }
        }
    }
}
