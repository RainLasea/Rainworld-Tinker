using RWCustom;
using UnityEngine;
using Tinker.Silk.Bridge;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Mouse
{
    public static class MouseRender
    {
        private static FSprite cursorSprite;
        private static FSprite bridgeAnchorSprite;
        private static FSprite targetPreviewSprite;
        private static bool initialized = false;

        public static void Initialize()
        {
            if (initialized) return;
            On.HUD.HUD.InitSinglePlayerHud += HUD_InitSinglePlayerHud;
            On.HUD.HUD.Update += HUD_Update;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.HUD.HUD.InitSinglePlayerHud -= HUD_InitSinglePlayerHud;
            On.HUD.HUD.Update -= HUD_Update;

            cursorSprite?.RemoveFromContainer();
            bridgeAnchorSprite?.RemoveFromContainer();
            targetPreviewSprite?.RemoveFromContainer();

            cursorSprite = null;
            bridgeAnchorSprite = null;
            targetPreviewSprite = null;
            initialized = false;
        }

        private static void HUD_InitSinglePlayerHud(On.HUD.HUD.orig_InitSinglePlayerHud orig, HUD.HUD self, RoomCamera cam)
        {
            orig(self, cam);
            CreateCursorSprites(self.fContainers[1]);
        }

        private static void CreateCursorSprites(FContainer container)
        {
            cursorSprite = new FSprite("Mouse")
            {
                color = Color.white,
                scale = 1f,
                anchorX = 0.5f,
                anchorY = 0.5f,
                alpha = 1f,
                isVisible = false
            };

            bridgeAnchorSprite = new FSprite("Circle20")
            {
                color = new Color(1f, 0.3f, 0.3f, 1f),
                scale = 0.5f,
                anchorX = 0.5f,
                anchorY = 0.5f,
                alpha = 1f,
                isVisible = false
            };

            targetPreviewSprite = new FSprite("Futile_White")
            {
                color = new Color(0.2f, 1f, 0.3f, 0.8f),
                scale = 0.6f,
                anchorX = 0.5f,
                anchorY = 0.5f,
                alpha = 0f,
                isVisible = false
            };

            container.AddChild(cursorSprite);
            container.AddChild(bridgeAnchorSprite);
            container.AddChild(targetPreviewSprite);
        }

        private static void HUD_Update(On.HUD.HUD.orig_Update orig, HUD.HUD self)
        {
            orig(self);
            if (cursorSprite != null && self.owner is Player player)
            {
                bool isTinker = player.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !player.isSlugpup;
                if (!isTinker)
                {
                    HideAllSprites();
                    return;
                }

                var gpState = Tinker.Silk.Bridge.GamepadBridgeState.GetOrCreate(player);
                if (gpState.aiming)
                {
                    UpdateGamepadCursor(player, gpState);
                }
                else if (player.input[0].gamePad || gpState.gamepadConnected)
                {
                    HideAllSprites();
                }
                else if (MouseAimSystem.IsMouseAimEnabled())
                {
                    // Mouse mode: show mouse cursor
                    UpdateCursorPosition(player);
                }
                else
                {
                    HideAllSprites();
                }
            }
        }

        private static void UpdateCursorPosition(Player player)
        {
            if (cursorSprite == null) return;

            Vector2 mousePos = Futile.mousePosition;
            var bridgeState = SilkBridgeManager.GetBridgeModeState(player);
            bool inBridgeMode = bridgeState != null && bridgeState.active;

            if (inBridgeMode)
            {
                cursorSprite.x = mousePos.x;
                cursorSprite.y = mousePos.y;
                cursorSprite.alpha = 0.5f;
                cursorSprite.color = Color.white;
                cursorSprite.scale = 0.8f;
                cursorSprite.isVisible = true;

                var cam = MouseAimSystem.GetCurrentCamera();
                if (cam != null)
                {
                    Vector2 anchorScreenPos = bridgeState.point2 - cam.pos;
                    bridgeAnchorSprite.x = anchorScreenPos.x;
                    bridgeAnchorSprite.y = anchorScreenPos.y;
                    bridgeAnchorSprite.isVisible = true;

                    float pulse = 0.5f + Mathf.Sin(Time.time * 6f) * 0.08f;
                    bridgeAnchorSprite.scale = pulse;
                    bridgeAnchorSprite.alpha = 0.85f + Mathf.Sin(Time.time * 6f) * 0.15f;
                    bridgeAnchorSprite.color = new Color(1f, 0.2f, 0.2f, 1f);

                    UpdateTargetPreview(player, cam, bridgeState);
                }
            }
            else
            {
                cursorSprite.x = mousePos.x;
                cursorSprite.y = mousePos.y;
                cursorSprite.alpha = 1f;
                cursorSprite.color = Color.white;
                cursorSprite.scale = 1f;
                cursorSprite.isVisible = true;
                bridgeAnchorSprite.isVisible = false;
                targetPreviewSprite.isVisible = false;
            }
        }

        private static void UpdateTargetPreview(Player player, RoomCamera cam, BridgeModeState bridgeState)
        {
            Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
            UpdateTargetPreview(player, cam, bridgeState, mouseWorldPos);
        }

        private static void UpdateTargetPreview(Player player, RoomCamera cam, BridgeModeState bridgeState, Vector2 targetWorldPos)
        {
            if (targetPreviewSprite == null || player.room == null) return;

            if (bridgeState.TryGetVirtualSilkPreviewHit(player, targetWorldPos, out Vector2 hitPoint))
            {
                Vector2 screenPos = hitPoint - cam.pos;
                targetPreviewSprite.x = screenPos.x;
                targetPreviewSprite.y = screenPos.y;
                targetPreviewSprite.isVisible = true;

                float breathe = 0.6f + Mathf.Sin(Time.time * 8f) * 0.15f;
                targetPreviewSprite.scale = breathe;
                targetPreviewSprite.alpha = 0.7f + Mathf.Sin(Time.time * 8f) * 0.2f;
                targetPreviewSprite.rotation = 45f;

                targetPreviewSprite.SetElementByName("Futile_White");
            }
            else
            {
                targetPreviewSprite.isVisible = false;
            }
        }

        private static void DrawDiamondShape(FSprite sprite)
        {
            if (sprite.element.name != "DiamondPreview")
            {
                FAtlasElement diamondElement = Futile.atlasManager.GetElementWithName("pixel");
                if (diamondElement != null)
                {
                    sprite.SetElementByName("pixel");
                    sprite.scaleX = 0.8f;
                    sprite.scaleY = 0.8f;
                }
            }
        }

        private static void HideAllSprites()
        {
            if (cursorSprite != null) cursorSprite.isVisible = false;
            if (bridgeAnchorSprite != null) bridgeAnchorSprite.isVisible = false;
            if (targetPreviewSprite != null) targetPreviewSprite.isVisible = false;
        }

        private static void UpdateGamepadCursor(Player player, GamepadBridgeState gpState)
        {
            if (cursorSprite == null) return;

            var cam = MouseAimSystem.GetCurrentCamera();
            if (cam == null)
            {
                HideAllSprites();
                return;
            }

            Vector2 screenPos = gpState.aimWorldPos - cam.pos;

            // Gamepad cursor: blue-tinted, slightly smaller
            cursorSprite.x = screenPos.x;
            cursorSprite.y = screenPos.y;
            cursorSprite.alpha = 0.9f;
            cursorSprite.color = new Color(0.3f, 0.8f, 1f);
            cursorSprite.scale = 0.85f;
            cursorSprite.isVisible = true;

            // Bridge anchor preview at the silk's attached position
            var bridgeState = SilkBridgeManager.GetBridgeModeState(player);
            bool inBridgeMode = bridgeState != null && bridgeState.active;

            if (inBridgeMode)
            {
                Vector2 anchorScreenPos = bridgeState.point2 - cam.pos;
                bridgeAnchorSprite.x = anchorScreenPos.x;
                bridgeAnchorSprite.y = anchorScreenPos.y;
                bridgeAnchorSprite.isVisible = true;

                float pulse = 0.5f + Mathf.Sin(Time.time * 6f) * 0.08f;
                bridgeAnchorSprite.scale = pulse;
                bridgeAnchorSprite.alpha = 0.85f + Mathf.Sin(Time.time * 6f) * 0.15f;
                bridgeAnchorSprite.color = new Color(1f, 0.2f, 0.2f, 1f);
            }
            else if (gpState.rtHeld)
            {
                // RT held → show target preview at cursor
                bridgeAnchorSprite.x = screenPos.x;
                bridgeAnchorSprite.y = screenPos.y;
                bridgeAnchorSprite.isVisible = true;

                float pulse = 0.6f + Mathf.Sin(Time.time * 8f) * 0.1f;
                bridgeAnchorSprite.scale = pulse * 0.6f;
                bridgeAnchorSprite.alpha = 0.7f + Mathf.Sin(Time.time * 8f) * 0.2f;
                bridgeAnchorSprite.color = new Color(0.3f, 1f, 0.5f);
            }
            else
            {
                bridgeAnchorSprite.isVisible = false;
            }

            if (inBridgeMode)
                UpdateTargetPreview(player, cam, bridgeState, gpState.aimWorldPos);
            else
                targetPreviewSprite.isVisible = false;
        }
    }
}