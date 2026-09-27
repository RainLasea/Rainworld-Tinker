using System;
using UnityEngine;

namespace Tinker.PlayerGraphics_Hooks
{
    public class SpiderArmsModule : IDisposable
    {
        private readonly LeaserSprites sprites = new();

        public SpiderArmsModule(PlayerGraphics self, Player player) { }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            sprites.Add(sLeaser, new FSprite("PlayerArm0"), new FSprite("PlayerArm0"));
            AddToContainer(sLeaser, rCam, null);
        }

        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!sprites.TryGet(sLeaser, out var arms)) return;
            for (int j = 0; j < arms.Length; j++)
            {
                FSprite original = sLeaser.sprites[5 + j];
                FSprite copy = arms[j];
                copy.element = original.element;
                copy.x = original.x;
                copy.y = original.y - 6f;
                copy.rotation = original.rotation;
                copy.scaleX = original.scaleX;
                copy.scaleY = original.scaleY;
                copy.anchorX = original.anchorX;
                copy.anchorY = original.anchorY;
                copy.color = original.color;
                copy.alpha = original.alpha;
                copy.shader = original.shader;
                copy.isVisible = original.isVisible;
            }
        }

        public void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container)
        {
            if (!sprites.TryGet(sLeaser, out var arms)) return;
            for (int j = 0; j < arms.Length; j++)
            {
                FSprite original = sLeaser.sprites[5 + j];
                FContainer target = container ?? original.container ?? rCam.ReturnFContainer("Midground");
                arms[j].RemoveFromContainer();
                target.AddChild(arms[j]);
                if (original.container == target) arms[j].MoveBehindOtherNode(original);
            }
        }

        public void Cleanup() => sprites.Cleanup();
        public void Dispose() => Cleanup();
    }
}
