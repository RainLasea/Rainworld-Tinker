using HUD;
using UnityEngine;

namespace Weaver
{
    public static class MouseRender
    {
        private static FSprite cursorSprite;
        private static bool initialized = false;

        public static void Initialize()
        {
            if (initialized) return;
            On.HUD.HUD.ctor += HUD_ctor;
            On.HUD.HUD.Update += HUD_Update;
            On.HUD.HUD.ClearAllSprites += HUD_ClearAllSprites;
            On.RainWorld.Update += RainWorld_Update;
            initialized = true;
        }

        private static void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self)
        {
            orig(self);
            Cursor.visible = !MouseAimSystem.IsMouseAimEnabled();
        }

        private static void HUD_ctor(On.HUD.HUD.orig_ctor orig, global::HUD.HUD self, global::FContainer[] fContainers, global::RainWorld rainWorld, IOwnAHUD owner)
        {
            orig(self, fContainers, rainWorld, owner);
            if (owner is Player && self.fContainers.Length > 1)
            {
                CreateCursorSprite(self.fContainers[1]);
            }
        }

        private static void HUD_ClearAllSprites(On.HUD.HUD.orig_ClearAllSprites orig, global::HUD.HUD self)
        {
            if (cursorSprite != null)
            {
                cursorSprite.RemoveFromContainer();
                cursorSprite = null;
            }
            orig(self);
        }

        private static void CreateCursorSprite(FContainer container)
        {
            if (cursorSprite != null) return;

            cursorSprite = new FSprite("Circle20");
            cursorSprite.color = new Color(1f, 1f, 1f, 0.9f);
            cursorSprite.scale = 0.4f;
            cursorSprite.anchorX = 0.5f;
            cursorSprite.anchorY = 0.5f;
            cursorSprite.alpha = 1f;
            cursorSprite.isVisible = false;

            container.AddChild(cursorSprite);
        }

        private static void HUD_Update(On.HUD.HUD.orig_Update orig, global::HUD.HUD self)
        {
            orig(self);

            if (cursorSprite != null && self.owner is Player)
            {
                if (MouseAimSystem.IsMouseAimEnabled())
                {
                    UpdateCursorPosition();
                }
                else
                {
                    cursorSprite.isVisible = false;
                }
            }
        }

        private static void UpdateCursorPosition()
        {
            if (cursorSprite == null) return;
            Vector2 mousePos = Futile.mousePosition;
            cursorSprite.x = mousePos.x;
            cursorSprite.y = mousePos.y;
            cursorSprite.isVisible = true;
        }

        public static void Cleanup()
        {
            if (cursorSprite != null)
            {
                cursorSprite.RemoveFromContainer();
                cursorSprite = null;
            }

            On.HUD.HUD.ctor -= HUD_ctor;
            On.HUD.HUD.Update -= HUD_Update;
            On.HUD.HUD.ClearAllSprites -= HUD_ClearAllSprites;
            On.RainWorld.Update -= RainWorld_Update;

            Cursor.visible = true;
            initialized = false;
        }
    }
}