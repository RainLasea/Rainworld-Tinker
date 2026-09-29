using System.Collections.Generic;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Mouse
{
    public static class MouseRender
    {
        private static readonly Dictionary<HUD.HUD, CursorRenderer> renderers = new();
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized) return;
            On.HUD.HUD.InitSinglePlayerHud += HUD_InitSinglePlayerHud;
            On.HUD.HUD.Update += HUD_Update;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
            On.RainWorldGame.ShutDownProcess += Game_ShutDownProcess;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.HUD.HUD.InitSinglePlayerHud -= HUD_InitSinglePlayerHud;
            On.HUD.HUD.Update -= HUD_Update;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.RainWorldGame.ShutDownProcess -= Game_ShutDownProcess;
            foreach (var renderer in renderers.Values) renderer.Remove();
            renderers.Clear();
            initialized = false;
        }

        private static void HUD_InitSinglePlayerHud(On.HUD.HUD.orig_InitSinglePlayerHud orig, HUD.HUD self, RoomCamera cam)
        {
            orig(self, cam);
            var stale = new List<HUD.HUD>();
            foreach (var pair in renderers)
                if (pair.Key == self || pair.Value.Camera.game != cam.game || pair.Value.Camera.hud != pair.Key)
                {
                    pair.Value.Remove();
                    stale.Add(pair.Key);
                }
            foreach (var hud in stale) renderers.Remove(hud);
            renderers[self] = new CursorRenderer(cam, self.fContainers[1]);
        }

        private static void HUD_Update(On.HUD.HUD.orig_Update orig, HUD.HUD self)
        {
            orig(self);
            if (renderers.TryGetValue(self, out var renderer)) renderer.UpdatePreview(self.owner as Player);
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            // Draw after the world so WorldToHud uses this frame's camera origin.
            // Sample the mouse every render frame without adding smoothing lag.
            if (self.hud != null && renderers.TryGetValue(self.hud, out var renderer))
                renderer.Draw(self.hud.owner as Player);
        }

        private static void Game_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame game)
        {
            var stale = new List<HUD.HUD>();
            foreach (var pair in renderers)
                if (pair.Value.Camera.game == game)
                {
                    pair.Value.Remove();
                    stale.Add(pair.Key);
                }
            foreach (var hud in stale) renderers.Remove(hud);
            orig(game);
        }

        private sealed class CursorRenderer
        {
            internal readonly RoomCamera Camera;
            private readonly FContainer container;
            private readonly FSprite cursor, anchor, preview;
            private bool hasPreview;
            private Vector2 previewHit;
            private Room previewRoom;
            private BridgeModeState previewBridge;

            internal CursorRenderer(RoomCamera camera, FContainer container)
            {
                Camera = camera;
                this.container = container;
                cursor = new FSprite("Mouse");
                anchor = new FSprite("Circle20");
                preview = new FSprite("Futile_White") { rotation = 45f, color = new Color(0.2f, 1f, 0.3f) };
                container.AddChild(cursor);
                container.AddChild(anchor);
                container.AddChild(preview);
                Hide();
            }

            internal void Remove()
            {
                cursor.RemoveFromContainer();
                anchor.RemoveFromContainer();
                preview.RemoveFromContainer();
            }

            private void Hide()
            {
                cursor.isVisible = anchor.isVisible = preview.isVisible = false;
            }

            private bool CanDraw(Player player)
            {
                if (!MouseAimSystem.IsLocalPlayer(player) || player.room != Camera.room ||
                    Camera.game.paused || player.dead) return false;
                return GamepadBridgeState.GetOrCreate(player).aiming ||
                    (!MouseAimSystem.IsGamepadActive(player) && MouseAimSystem.IsMouseAimEnabled(player));
            }

            internal void UpdatePreview(Player player)
            {
                hasPreview = false;
                previewRoom = null;
                previewBridge = null;
                if (!CanDraw(player))
                {
                    Hide();
                    return;
                }

                var bridge = SilkBridgeManager.GetBridgeModeState(player);
                if (bridge?.active != true) return;
                var gamepad = GamepadBridgeState.GetOrCreate(player);
                Vector2 target;
                if (gamepad.aiming) target = gamepad.aimWorldPos;
                else if (!MouseAimSystem.TryGetMouseWorldPosition(player, out target)) return;

                // Collision prediction can trace up to 30 simulation steps. Cache
                // the result at the logic rate instead of repeating it per draw.
                hasPreview = bridge.TryGetVirtualSilkPreviewHit(player, target, out previewHit);
                previewRoom = player.room;
                previewBridge = bridge;
            }

            internal void Draw(Player player)
            {
                Hide();
                if (!CanDraw(player)) return;

                var gamepad = GamepadBridgeState.GetOrCreate(player);
                bool aiming = gamepad.aiming;
                var bridge = SilkBridgeManager.GetBridgeModeState(player);
                bool building = bridge?.active == true;
                Vector2 screen = aiming ? MouseAimSystem.WorldToHud(Camera, container, gamepad.aimWorldPos)
                    : container.ScreenToLocal(Futile.mousePosition);
                cursor.SetPosition(screen);
                cursor.color = aiming ? new Color(0.3f, 0.8f, 1f) : Color.white;
                cursor.scale = aiming ? 0.85f : building ? 0.8f : 1f;
                cursor.alpha = aiming ? 0.9f : building ? 0.5f : 1f;
                cursor.isVisible = true;

                if (building)
                {
                    anchor.SetPosition(MouseAimSystem.WorldToHud(Camera, container, bridge.point2));
                    anchor.scale = 0.5f + Mathf.Sin(Time.time * 6f) * 0.08f;
                    anchor.alpha = 0.85f + Mathf.Sin(Time.time * 6f) * 0.15f;
                    anchor.color = new Color(1f, 0.2f, 0.2f);
                    anchor.isVisible = true;
                    if (hasPreview && previewRoom == player.room && previewBridge == bridge)
                    {
                        preview.SetPosition(MouseAimSystem.WorldToHud(Camera, container, previewHit));
                        preview.scale = 0.6f + Mathf.Sin(Time.time * 8f) * 0.15f;
                        preview.alpha = 0.7f + Mathf.Sin(Time.time * 8f) * 0.2f;
                        preview.isVisible = true;
                    }
                }
                else if (aiming && gamepad.rtHeld)
                {
                    anchor.SetPosition(screen);
                    anchor.scale = (0.6f + Mathf.Sin(Time.time * 8f) * 0.1f) * 0.6f;
                    anchor.alpha = 0.7f + Mathf.Sin(Time.time * 8f) * 0.2f;
                    anchor.color = new Color(0.3f, 1f, 0.5f);
                    anchor.isVisible = true;
                }
            }
        }
    }
}
