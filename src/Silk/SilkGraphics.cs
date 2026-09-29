using System.Collections.Generic;
using Tinker.PlayerGraphics_Hooks;
using UnityEngine;

namespace tinker.Silk
{
    public class SilkGraphics
    {
        public Player player;
        public SilkPhysics silk;

        private TriangleMesh lineMesh;
        private FSprite pullIndicator;
        public bool IsSpritesInitiated => spritesInitiated;
        private bool spritesInitiated;
        private readonly LeaserSprites leasedSprites = new();

        private const int MAX_ROPE_RENDER_SEGMENTS = 120;

        public SilkGraphics(Player player)
        {
            this.player = player;
            this.silk = tinkerSilkData.Get(player);
            this.spritesInitiated = false;
        }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            RemoveSprites();

            lineMesh = SilkFadeMesh.Create(MAX_ROPE_RENDER_SEGMENTS);
            lineMesh.shader = rCam.game.rainWorld.Shaders["Basic"];

            pullIndicator = CreateSprite("Futile_White", new Color(0.3f, 1f, 0.3f), 1.2f);
            pullIndicator.shader = rCam.game.rainWorld.Shaders["FlatLight"];
            pullIndicator.alpha = 0f;

            leasedSprites.Add(sLeaser, lineMesh, pullIndicator);
            AddToContainer(sLeaser, rCam.ReturnFContainer("Midground"));
            spritesInitiated = true;
        }

        public void AddToContainer(RoomCamera.SpriteLeaser leaser, FContainer newContainer)
        {
            if (newContainer == null || !leasedSprites.TryGet(leaser, out _)) return;

            if (lineMesh != null)
            {
                lineMesh.RemoveFromContainer();
                newContainer.AddChild(lineMesh);
            }

            if (pullIndicator != null)
            {
                pullIndicator.RemoveFromContainer();
                newContainer.AddChild(pullIndicator);
            }

        }

        /// <returns>true if sprites were actually drawn, false if hidden</returns>
        public bool DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!spritesInitiated || player == null || player.slatedForDeletetion || silk == null)
            {
                HideAllSprites();
                return false;
            }

            Vector2 headPos = Vector2.Lerp(player.bodyChunks[0].lastPos, player.bodyChunks[0].pos, timeStacker);
            Vector2 silkTipPos = Vector2.Lerp(silk.lastPos, silk.pos, timeStacker);
            float distance = Vector2.Distance(headPos, silkTipPos);

            bool ropeShouldBeVisible = (silk.mode != SilkMode.Retracted && silk.mode != SilkMode.Retracting && distance >= 3f);

            if (ropeShouldBeVisible)
            {
                UpdateUmbilicalStyleMesh(silk.GetRopePath(timeStacker), camPos);
            }
            else if (lineMesh != null)
            {
                lineMesh.isVisible = false;
            }

            UpdatePullIndicator(silkTipPos, camPos);
            return true;
        }

        private void UpdateUmbilicalStyleMesh(List<Vector2> path, Vector2 camPos)
        {
            if (lineMesh == null) return;
            var points = SilkFade.Resample(path, MAX_ROPE_RENDER_SEGMENTS);
            if (points.Length < 2)
            {
                lineMesh.isVisible = false;
                return;
            }
            float stretchFactor = Mathf.Clamp01(Vector2.Distance(points[0], points[points.Length - 1]) / 500f);
            SilkFadeMesh.Draw(lineMesh, points, null, 1f, camPos, Mathf.Lerp(1.2f, 0.6f, stretchFactor));
            lineMesh.color = GetSilkColor();
        }

        private void UpdatePullIndicator(Vector2 tipPos, Vector2 camPos)
        {
            if (pullIndicator == null) return;
            if (!silk.pullingObject || !silk.AttachedToItem)
            {
                pullIndicator.isVisible = false;
                return;
            }
            pullIndicator.isVisible = true;
            pullIndicator.x = tipPos.x - camPos.x;
            pullIndicator.y = tipPos.y - camPos.y;
            float pulse = 0.6f + Mathf.Sin(Time.time * 8f) * 0.4f;
            pullIndicator.scale = pulse * 1.5f;
            pullIndicator.alpha = pulse * 0.7f;
        }

        private Color GetSilkColor()
        {
            Color silkColor = new Color(0.9f, 0.9f, 0.9f);
            if (silk.superJumpTimer > 0)
                silkColor = Color.Lerp(silkColor, new Color(0.7f, 1f, 1f), 0.5f);
            else if (silk.pullingObject)
                silkColor = Color.Lerp(silkColor, new Color(0.4f, 1f, 0.4f), 0.3f);

            return silkColor;
        }

        private FSprite CreateSprite(string element, Color color, float scale)
        {
            var sprite = new FSprite(element, true);
            sprite.color = color;
            sprite.scale = scale;
            sprite.isVisible = false;
            return sprite;
        }

        public void RemoveSprites()
        {
            leasedSprites.Cleanup();
            spritesInitiated = false;
        }

        private void HideAllSprites()
        {
            FNode[] sprites = { lineMesh, pullIndicator };
            foreach (var sprite in sprites) if (sprite != null) sprite.isVisible = false;
        }
    }
}
