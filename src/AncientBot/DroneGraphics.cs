using UnityEngine;
using RWCustom;

namespace Tinker.AncientBot
{
    public class DroneGraphics : GraphicsModule, IDrawable
    {
        public DroneGraphics(PhysicalObject ow) : base(ow, false)
        {
        }

        public override void Update()
        {
            base.Update();
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            sLeaser.sprites = new FSprite[3];
            sLeaser.sprites[0] = new FSprite("Circle20", true);
            sLeaser.sprites[1] = new FSprite("pixel", true);
            sLeaser.sprites[2] = new FSprite("Futile_White", true);
            sLeaser.sprites[2].shader = rCam.room.game.rainWorld.Shaders["LightSource"];
            this.AddToContainer(sLeaser, rCam, null);
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            Vector2 pos = Vector2.Lerp(owner.firstChunk.lastPos, owner.firstChunk.pos, timeStacker);
            for (int i = 0; i < sLeaser.sprites.Length; i++)
            {
                sLeaser.sprites[i].x = pos.x - camPos.x;
                sLeaser.sprites[i].y = pos.y - camPos.y;
            }

            bool active = false;
            if (owner is UVsKeyDrone_Item item) active = item.isPowered;
            else if (owner is UVsKeyDrone_Creature crit) active = crit.isPowered;

            int blink = 0;
            if (owner is PlayerCarryableItem pci) blink = pci.blink;

            if (blink > 0 && UnityEngine.Random.value < 0.5f)
            {
                sLeaser.sprites[0].color = Color.white;
            }
            else if (!active)
            {
                sLeaser.sprites[0].color = new Color(0.2f, 0.2f, 0.2f);
                sLeaser.sprites[1].color = new Color(0.1f, 0.1f, 0.1f);
                sLeaser.sprites[2].isVisible = false;
            }
            else
            {
                Color cyan = new Color(0.4f, 0.9f, 1f);
                sLeaser.sprites[0].color = cyan;
                sLeaser.sprites[1].color = Color.white;
                sLeaser.sprites[2].isVisible = true;
                sLeaser.sprites[2].scale = 8f;
                sLeaser.sprites[2].alpha = 0.6f;
            }
        }

        public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette) { }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            newContainer ??= rCam.ReturnFContainer("Midground");
            foreach (var s in sLeaser.sprites) newContainer.AddChild(s);
        }
    }
}