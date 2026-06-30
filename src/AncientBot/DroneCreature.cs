using UnityEngine;

namespace Tinker.AncientBot
{
    public class UVsKeyDrone_Item : PlayerCarryableItem, IDrawable
    {
        public bool isPowered = false;

        public UVsKeyDrone_Item(AbstractPhysicalObject abstractPhysicalObject) : base(abstractPhysicalObject)
        {
            this.bodyChunks = new BodyChunk[1];
            this.bodyChunks[0] = new BodyChunk(this, 0, new Vector2(0f, 0f), 8f, 0.07f);
            this.bodyChunkConnections = new PhysicalObject.BodyChunkConnection[0];
            this.airFriction = 0.99f;
            this.gravity = 0.9f;
            this.bounce = 0.4f;
            this.surfaceFriction = 0.4f;
            this.collisionLayer = 1;
        }

        public override void Update(bool eu)
        {
            base.Update(eu);
            if (this.blink > 0) this.blink--;

            if (isPowered && this.grabbedBy.Count == 0 && this.room != null)
            {
                ActivateAsCreature();
            }
        }

        private void ActivateAsCreature()
        {
            Room currentRoom = this.room;
            WorldCoordinate coord = this.abstractPhysicalObject.pos;
            this.AllGraspsLetGoOfThisObject(true);

            AbstractCreature abstractDrone = new AbstractCreature(this.abstractPhysicalObject.world, StaticWorld.GetCreatureTemplate(CreatureTemplate.Type.Fly), null, coord, this.abstractPhysicalObject.ID);
            currentRoom.abstractRoom.AddEntity(abstractDrone);
            abstractDrone.RealizeInRoom();

            abstractDrone.realizedCreature.mainBodyChunk.pos = this.bodyChunks[0].pos;
            abstractDrone.realizedCreature.mainBodyChunk.vel = this.bodyChunks[0].vel;

            this.abstractPhysicalObject.Destroy();
            this.Destroy();
        }
        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam) => new DroneGraphics(this).InitiateSprites(sLeaser, rCam);
        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos) => (graphicsModule as DroneGraphics ?? new DroneGraphics(this)).DrawSprites(sLeaser, rCam, timeStacker, camPos);
        public void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette) { }
        public void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer) => new DroneGraphics(this).AddToContainer(sLeaser, rCam, newContainer);
    }

    public class UVsKeyDrone_Creature : Creature
    {
        public bool isPowered = true;

        public UVsKeyDrone_Creature(AbstractCreature abstractCreature, World world) : base(abstractCreature, world)
        {
            this.bodyChunks = new BodyChunk[1];
            this.bodyChunks[0] = new BodyChunk(this, 0, new Vector2(0f, 0f), 8f, 0.07f);
            this.bodyChunkConnections = new PhysicalObject.BodyChunkConnection[0];
            this.airFriction = 0.95f;
            this.gravity = 0f;
            this.collisionLayer = 1;
        }

        public override void InitiateGraphicsModule()
        {
            if (this.graphicsModule == null)
            {
                this.graphicsModule = new DroneGraphics(this);
            }
        }

        public override void Update(bool eu)
        {
            base.Update(eu);

            // 简单的漂浮动画
            if (isPowered)
            {
                this.mainBodyChunk.vel.y += Mathf.Sin((float)room.world.game.clock / 10f) * 0.2f;
            }
        }
    }
}