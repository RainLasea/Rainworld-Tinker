using RWCustom;
using UnityEngine;
using Weaver.Silk.Bridge;

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
            cursorSprite = new FSprite("Futile_White")
            {
                color = Color.white,
                scale = 0.4f,
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
                if (MouseAimSystem.IsMouseAimEnabled())
                {
                    UpdateCursorPosition(player);
                }
                else
                {
                    cursorSprite.isVisible = false;
                    bridgeAnchorSprite.isVisible = false;
                    targetPreviewSprite.isVisible = false;
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
                cursorSprite.color = new Color(1f, 1f, 1f, 0.5f);
                cursorSprite.scale = 0.35f;
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
                cursorSprite.scale = 0.4f;
                cursorSprite.isVisible = true;
                bridgeAnchorSprite.isVisible = false;
                targetPreviewSprite.isVisible = false;
            }
        }

        private static void UpdateTargetPreview(Player player, RoomCamera cam, BridgeModeState bridgeState)
        {
            if (targetPreviewSprite == null || player.room == null) return;

            Vector2 D2 = bridgeState.point2;
            Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);

            Vector2 rayDir = mouseWorldPos - D2;
            float rayLen = Mathf.Min(rayDir.magnitude, 800f);
            if (rayLen < 0.001f)
            {
                targetPreviewSprite.isVisible = false;
                return;
            }
            rayDir /= rayDir.magnitude;

            IntVector2? firstSolid = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(player.room, D2, mouseWorldPos);
            float terrainDist = float.MaxValue;
            Vector2? terrainPoint = null;
            if (firstSolid.HasValue)
            {
                terrainPoint = GetPreciseTerrainCollisionForPreview(player.room, D2, mouseWorldPos);
                if (terrainPoint.HasValue)
                    terrainDist = Vector2.Distance(D2, terrainPoint.Value);
            }

            var bridges = SilkBridgeManager.GetBridgesInRoom(player.room);
            var candidates = new System.Collections.Generic.List<(Vector2 point, float dist)>();

            if (bridges != null)
            {
                foreach (var b in bridges)
                {
                    if (b == null || b.room != player.room) continue;
                    var path = b.GetRenderPath();
                    if (path == null || path.Count < 2) continue;

                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        Vector2 a = path[i], bpt = path[i + 1];
                        Vector2 seg = bpt - a, r = D2 - a;
                        float denom = rayDir.x * seg.y - rayDir.y * seg.x;
                        if (Mathf.Abs(denom) < 1e-6f) continue;

                        float t = (r.x * seg.y - r.y * seg.x) / denom;
                        float u = (r.x * rayDir.y - r.y * rayDir.x) / denom;
                        if (t >= 0f && t <= rayLen && u >= 0f && u <= 1f && t <= terrainDist + 0.001f)
                        {
                            Vector2 intersect = D2 + rayDir * t;
                            candidates.Add((intersect, Vector2.Distance(intersect, mouseWorldPos)));
                        }
                    }
                }
            }

            if (terrainPoint.HasValue && terrainDist <= rayLen)
                candidates.Add((terrainPoint.Value, Vector2.Distance(terrainPoint.Value, mouseWorldPos)));

            if (candidates.Count > 0)
            {
                float bestDist = float.MaxValue;
                Vector2 bestPoint = Vector2.zero;
                foreach (var c in candidates)
                    if (c.dist < bestDist) { bestDist = c.dist; bestPoint = c.point; }

                Vector2 screenPos = bestPoint - cam.pos;
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

        private static Vector2? GetPreciseTerrainCollisionForPreview(Room room, Vector2 from, Vector2 to)
        {
            IntVector2? firstSolid = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, from, to);
            if (!firstSolid.HasValue) return null;

            Vector2 start = from;
            Vector2 end = to;

            for (int i = 0; i < 10; i++)
            {
                Vector2 mid = (start + end) * 0.5f;
                IntVector2? hit = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, from, mid);
                if (hit.HasValue)
                    end = mid;
                else
                    start = mid;
            }

            return end;
        }
    }
}