using RWCustom;
using System;
using System.Collections.Generic;
using tinker;
using UnityEngine;

namespace Tinker.PlayerRender
{
    public abstract class Antenna
    {
        protected Vector2[] lastPoints;
        public abstract void Update();
        public abstract void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam);
        public abstract void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos);
        public abstract void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container);
        public abstract void RemoveSprites();

        protected Vector2 Interpolate(Vector2 last, Vector2 current, float timeStacker)
        {
            return Vector2.Lerp(last, current, timeStacker);
        }
    }

    public class AntennaSystem
    {
        internal MainAntenna leftAntenna;
        internal MainAntenna rightAntenna;
        internal MiniAntenna leftMiniAntenna;
        internal MiniAntenna rightMiniAntenna;
        private Player player;
        private PlayerGraphics graphics;
        private bool enabled;

        public AntennaSystem(PlayerGraphics graphics, Player player)
        {
            this.player = player;
            this.graphics = graphics;
            enabled = player.slugcatStats.name.ToString() == Plugin.SlugName;
            if (!enabled) return;
            leftAntenna = new MainAntenna(graphics, player, true, 0f);
            rightAntenna = new MainAntenna(graphics, player, false, 0f);
            leftMiniAntenna = new MiniAntenna(graphics, player, true, 0f);
            rightMiniAntenna = new MiniAntenna(graphics, player, false, 0f);
            leftAntenna.Update();
            rightAntenna.Update();
            leftMiniAntenna.Update();
            rightMiniAntenna.Update();
        }

        public void Update()
        {
            if (!enabled) return;
            leftAntenna?.Update();
            rightAntenna?.Update();
            leftMiniAntenna?.Update();
            rightMiniAntenna?.Update();
        }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            if (!enabled) return;
            leftAntenna?.InitiateSprites(sLeaser, rCam);
            rightAntenna?.InitiateSprites(sLeaser, rCam);
            leftMiniAntenna?.InitiateSprites(sLeaser, rCam);
            rightMiniAntenna?.InitiateSprites(sLeaser, rCam);
        }

        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!enabled) return;
            leftAntenna?.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            rightAntenna?.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            leftMiniAntenna?.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            rightMiniAntenna?.DrawSprites(sLeaser, rCam, timeStacker, camPos);
        }

        public void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container)
        {
            if (!enabled) return;
            leftAntenna?.AddToContainer(sLeaser, rCam, container);
            rightAntenna?.AddToContainer(sLeaser, rCam, container);
            leftMiniAntenna?.AddToContainer(sLeaser, rCam, container);
            rightMiniAntenna?.AddToContainer(sLeaser, rCam, container);
        }

        public void RemoveSprites()
        {
            if (!enabled) return;
            leftAntenna?.RemoveSprites();
            rightAntenna?.RemoveSprites();
            leftMiniAntenna?.RemoveSprites();
            rightMiniAntenna?.RemoveSprites();
        }
    }

    public static class AntennaManager
    {
        internal static Dictionary<Player, AntennaSystem> activeSystems = new();
        internal static Dictionary<Player, AntennaSystem> ActiveSystems => activeSystems;

        public static void Init()
        {
            On.Player.Update += Player_Update;
            On.PlayerGraphics.InitiateSprites += PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer += PlayerGraphics_AddToContainer;
            On.Player.NewRoom += Player_NewRoom;
        }

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            if (ShouldHaveAntenna(self) && !activeSystems.ContainsKey(self))
            {
                activeSystems[self] = new AntennaSystem(self.graphicsModule as PlayerGraphics, self);
            }
            else if (!ShouldHaveAntenna(self) && activeSystems.ContainsKey(self))
            {
                activeSystems[self].RemoveSprites();
                activeSystems.Remove(self);
                return;
            }

            if (activeSystems.ContainsKey(self))
            {
                activeSystems[self]?.Update();
            }
        }

        private static void PlayerGraphics_InitiateSprites(On.PlayerGraphics.orig_InitiateSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            orig(self, sLeaser, rCam);
            if (self.owner is Player player && ShouldHaveAntenna(player))
            {
                InitSystemForPlayer(player);
                activeSystems[player]?.InitiateSprites(sLeaser, rCam);
            }
        }

        private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            orig(self, sLeaser, rCam, timeStacker, camPos);
            if (self.owner is Player player && IsActive(player))
            {
                activeSystems[player]?.DrawSprites(sLeaser, rCam, timeStacker, camPos);
            }
        }

        private static void PlayerGraphics_AddToContainer(On.PlayerGraphics.orig_AddToContainer orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container)
        {
            orig(self, sLeaser, rCam, container);
            if (self.owner is Player player && IsActive(player))
            {
                activeSystems[player]?.AddToContainer(sLeaser, rCam, container);
            }
        }

        private static void Player_NewRoom(On.Player.orig_NewRoom orig, Player self, Room newRoom)
        {
            if (activeSystems.ContainsKey(self) && (newRoom == null || !ShouldHaveAntenna(self)))
            {
                activeSystems[self].RemoveSprites();
                activeSystems.Remove(self);
            }

            orig(self, newRoom);
        }

        public static bool IsActive(Player player)
        {
            return activeSystems.ContainsKey(player) && ShouldHaveAntenna(player);
        }

        public static bool ShouldHaveAntenna(Player player)
        {
            return player != null &&
                   player.slugcatStats.name.ToString() == Plugin.SlugName &&
                   player.room != null &&
                   player.graphicsModule != null;
        }

        public static void InitSystemForPlayer(Player player)
        {
            if (!activeSystems.ContainsKey(player) && ShouldHaveAntenna(player))
            {
                activeSystems[player] = new AntennaSystem(player.graphicsModule as PlayerGraphics, player);
            }
        }

        public static void Cleanup()
        {
            foreach (var system in activeSystems.Values)
                system.RemoveSprites();
            activeSystems.Clear();

            On.Player.Update -= Player_Update;
            On.PlayerGraphics.InitiateSprites -= PlayerGraphics_InitiateSprites;
            On.PlayerGraphics.DrawSprites -= PlayerGraphics_DrawSprites;
            On.PlayerGraphics.AddToContainer -= PlayerGraphics_AddToContainer;
            On.Player.NewRoom -= Player_NewRoom;
        }
    }

    public class MiniAntenna : MainAntenna
    {
        public MiniAntenna(PlayerGraphics graphics, Player player, bool isLeft, float angleOffset)
            : base(graphics, player, isLeft, angleOffset)
        {
            segments = 1;
            segmentLength = 7f;
            baseWidth = 2.0f;
            tipWidth = 0.5f;
            baseYOffset = 2f;
            lateralOffset = 2f;
            currentPoints = new Vector2[segments + 1];
            idealPoints = new Vector2[segments + 1];
            velocities = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                velocities[i] = Vector2.zero;
            }
        }
    }

    public class MainAntenna : Antenna
    {
        protected int segments = 3;
        protected float segmentLength = 9f;
        protected float baseWidth = 2.5f;
        protected float tipWidth = 0.5f;
        protected float baseYOffset = 7f;
        protected float lateralOffset = 0f;

        private PlayerGraphics graphics;
        private Player player;
        private bool isLeft;
        private float angleOffset;

        private TriangleMesh antennaMesh;
        private bool spritesInitiated;
        private bool addedToContainer;
        private FContainer currentContainer;

        protected Vector2[] currentPoints;
        protected Vector2[] idealPoints;
        protected Vector2[] velocities;
        private RoomCamera currentCamera;

        public MainAntenna(PlayerGraphics graphics, Player player, bool isLeft, float angleOffset)
        {
            this.graphics = graphics;
            this.player = player;
            this.isLeft = isLeft;
            this.angleOffset = angleOffset;
            spritesInitiated = false;
            addedToContainer = false;
            currentContainer = null;
            currentPoints = new Vector2[segments + 1];
            idealPoints = new Vector2[segments + 1];
            velocities = new Vector2[segments + 1];
            lastPoints = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                velocities[i] = Vector2.zero;
                lastPoints[i] = Vector2.zero;
            }
        }

        public override void Update()
        {
            if (player == null || player.room == null) return;
            for (int i = 0; i <= segments; i++)
            {
                lastPoints[i] = currentPoints[i];
            }
            CalculateIdealPoints();
            if (!spritesInitiated)
            {
                for (int i = 0; i <= segments; i++)
                {
                    currentPoints[i] = idealPoints[i];
                    velocities[i] = Vector2.zero;
                    lastPoints[i] = idealPoints[i];
                }
            }
            UpdatePhysics();
        }

        private static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        private void CalculateIdealPoints()
        {
            Vector2 headPos = graphics.drawPositions[0, 0];
            Vector2 neckPos = graphics.drawPositions[1, 0];
            Vector2 bodyPos = player.mainBodyChunk != null ? player.mainBodyChunk.pos : neckPos;
            Vector2 hipsPos = (player.bodyChunks != null && player.bodyChunks.Length > 1) ? player.bodyChunks[1].pos : neckPos;
            Vector2 dif = (bodyPos - hipsPos);
            Vector2 difNorm = dif.sqrMagnitude > 1e-6f ? dif.normalized : Vector2.up;
            float bodyRotation = 0f;
            if (difNorm != Vector2.zero)
            {
                bodyRotation = Mathf.Atan2(difNorm.x, difNorm.y);
            }

            bool facingLeft = headPos.x < neckPos.x;
            float baseXOffset = (isLeft ? 1 : -1) * (facingLeft ? 3f : 1.5f);
            baseXOffset += isLeft ? lateralOffset : -lateralOffset;

            Vector2 headDir = (headPos - neckPos);
            if (headDir.sqrMagnitude > 1e-6f) headDir.Normalize();
            else headDir = Vector2.up;

            Vector2 headOffset = new Vector2(-3f * headDir.x, -2.5f * headDir.y);
            Vector2 bodyOffset = -3f * difNorm;
            Vector2 rotatedBase = Rotate(new Vector2(baseXOffset, baseYOffset), -bodyRotation * (0.4f * (1f - Mathf.Abs(Mathf.Cos(bodyRotation))) + 1f));
            Vector2 basePos = headPos + headOffset + bodyOffset + rotatedBase;
            idealPoints[0] = basePos;

            Vector2 headDirNorm = (headPos - neckPos);
            if (headDirNorm.sqrMagnitude > 1e-6f) headDirNorm.Normalize();
            else headDirNorm = Vector2.right;
            float baseAngle = Mathf.Atan2(headDirNorm.y, headDirNorm.x) + angleOffset;
            if (facingLeft)
                baseAngle = Mathf.PI - baseAngle;
            baseAngle += bodyRotation * 0.12f * (isLeft ? -1f : 1f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = baseAngle;
                float outwardAngle = (isLeft ? -1 : 1) * Mathf.Deg2Rad * 40f;
                float inwardAngle = (isLeft ? 1 : -1) * Mathf.Deg2Rad * 50f;
                if (i == 1) angle += outwardAngle;
                else if (i == 2) angle += isLeft ? 0.1f : -0.1f;
                else if (i == 3) angle += inwardAngle;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                idealPoints[i] = idealPoints[i - 1] + dir * segmentLength;
            }
        }

        private void UpdatePhysics()
        {
            currentPoints[0] = idealPoints[0];
            Vector2 bodyVel = player?.mainBodyChunk?.vel ?? Vector2.zero;
            for (int i = 1; i <= segments; i++)
            {
                currentPoints[i] += velocities[i];
                velocities[i] *= 0.98f;
                velocities[i] += Vector2.down * 0.2f;
                float speedInfluence = 0.02f * (1f - (float)i / Mathf.Max(1f, segments));
                velocities[i] += bodyVel * speedInfluence;
                Vector2 dir = (idealPoints[i] - currentPoints[i]);
                if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
                float dist = Vector2.Distance(currentPoints[i], idealPoints[i]);
                if (dist > 2f)
                {
                    currentPoints[i] += 0.4f * dir * (dist - 2f);
                    velocities[i] += 0.4f * dir * (dist - 2f);
                }
                if (velocities[i].sqrMagnitude < 0.0001f && (currentPoints[i] - idealPoints[i]).sqrMagnitude < 0.0001f)
                {
                    velocities[i] = Vector2.zero;
                    currentPoints[i] = idealPoints[i];
                }
                Vector2 toNext = currentPoints[i] - currentPoints[i - 1];
                float currentDist = toNext.magnitude;
                if (currentDist > 0.01f)
                {
                    float targetDist = segmentLength;
                    Vector2 correctedPos = currentPoints[i - 1] + toNext.normalized * targetDist;
                    currentPoints[i] = Vector2.Lerp(currentPoints[i], correctedPos, 0.5f);
                }
            }
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            if (graphics == null || player == null || player.room == null)
            {
                RemoveSprites();
                return;
            }

            if (spritesInitiated && antennaMesh != null && sLeaser != null)
            {
                for (int i = 0; i < sLeaser.sprites.Length; i++)
                {
                    if (ReferenceEquals(sLeaser.sprites[i], antennaMesh))
                    {
                        var newSprites = new FSprite[sLeaser.sprites.Length - 1];
                        Array.Copy(sLeaser.sprites, 0, newSprites, 0, i);
                        Array.Copy(sLeaser.sprites, i + 1, newSprites, i, sLeaser.sprites.Length - i - 1);
                        sLeaser.sprites = newSprites;
                        break;
                    }
                }
                RemoveSprites();
            }

            currentCamera = rCam;
            TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[segments * 2];
            for (int i = 0; i < segments; i++)
            {
                int vertIndex = i * 4;
                tris[i * 2] = new TriangleMesh.Triangle(vertIndex, vertIndex + 1, vertIndex + 2);
                tris[i * 2 + 1] = new TriangleMesh.Triangle(vertIndex + 1, vertIndex + 2, vertIndex + 3);
            }
            antennaMesh = new TriangleMesh("Futile_White", tris, true);
            if (antennaMesh.verticeColors == null || antennaMesh.verticeColors.Length != antennaMesh.vertices.Length)
                antennaMesh.verticeColors = new Color[antennaMesh.vertices.Length];
            antennaMesh.color = Color.white;
            antennaMesh.isVisible = true;
            if (rCam != null && rCam.game?.rainWorld?.Shaders != null && rCam.game.rainWorld.Shaders.ContainsKey("Basic"))
            {
                antennaMesh.shader = rCam.game.rainWorld.Shaders["Basic"];
            }
            if (sLeaser != null)
            {
                bool exists = false;
                for (int i = 0; i < sLeaser.sprites.Length; i++)
                {
                    if (ReferenceEquals(sLeaser.sprites[i], antennaMesh)) { exists = true; break; }
                }
                if (!exists)
                {
                    var newSprites = new FSprite[sLeaser.sprites.Length + 1];
                    Array.Copy(sLeaser.sprites, newSprites, sLeaser.sprites.Length);
                    newSprites[newSprites.Length - 1] = antennaMesh;
                    sLeaser.sprites = newSprites;
                }
            }
            CalculateIdealPoints();
            for (int i = 0; i <= segments; i++)
            {
                currentPoints[i] = idealPoints[i];
                lastPoints[i] = idealPoints[i];
                velocities[i] = Vector2.zero;
            }

            spritesInitiated = true;
            addedToContainer = false;
            currentContainer = null;
            TryAddToContainer(rCam);
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!spritesInitiated) return;
            if (!addedToContainer && rCam != null)
                TryAddToContainer(rCam);
            UpdateAntennaMesh(camPos, timeStacker);
        }

        private void UpdateAntennaMesh(Vector2 camPos, float timeStacker)
        {
            if (antennaMesh == null || antennaMesh.verticeColors == null) return;
            Color color1 = new Color(0x48 / 255f, 0xbe / 255f, 0x87 / 255f, 0.95f);
            Color color2 = new Color(0xaa / 255f, 0xe2 / 255f, 0x61 / 255f, 0.95f);
            float bodySpeed = player?.mainBodyChunk?.vel.magnitude ?? 0f;
            float widthWobble = Mathf.Clamp(bodySpeed * 0.2f, 0f, 1f);
            for (int i = 0; i < segments; i++)
            {
                Vector2 segStart = Interpolate(lastPoints[i], currentPoints[i], timeStacker);
                Vector2 segEnd = Interpolate(lastPoints[i + 1], currentPoints[i + 1], timeStacker);
                Vector2 segDir = segEnd - segStart;
                if (segDir.sqrMagnitude < 1e-6f)
                    segDir = new Vector2(isLeft ? -1f : 1f, 0f);
                segDir.Normalize();
                Vector2 perpendicular = Custom.PerpendicularVector(segDir);
                float t = (float)i / segments;
                float t2 = (float)(i + 1) / segments;
                float startWidth = Mathf.Lerp(baseWidth, tipWidth, t) * (1f + widthWobble * 0.25f);
                float endWidth = Mathf.Lerp(baseWidth, tipWidth, t2) * (1f + widthWobble * 0.25f);
                int vertIndex = i * 4;
                antennaMesh.MoveVertice(vertIndex, segStart - perpendicular * startWidth * 0.5f - camPos);
                antennaMesh.MoveVertice(vertIndex + 1, segStart + perpendicular * startWidth * 0.5f - camPos);
                antennaMesh.MoveVertice(vertIndex + 2, segEnd - perpendicular * endWidth * 0.5f - camPos);
                antennaMesh.MoveVertice(vertIndex + 3, segEnd + perpendicular * endWidth * 0.5f - camPos);
                Color segColorStart = Color.Lerp(color1, color2, t);
                Color segColorEnd = Color.Lerp(color1, color2, t2);
                if (vertIndex + 3 < antennaMesh.verticeColors.Length)
                {
                    antennaMesh.verticeColors[vertIndex] = segColorStart;
                    antennaMesh.verticeColors[vertIndex + 1] = segColorStart;
                    antennaMesh.verticeColors[vertIndex + 2] = segColorEnd;
                    antennaMesh.verticeColors[vertIndex + 3] = segColorEnd;
                }
            }
            antennaMesh.Refresh();
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container)
        {
            if (!spritesInitiated) return;
            TryAddToContainer(rCam);
        }

        private void TryAddToContainer(RoomCamera rCam)
        {

            if (rCam == null || antennaMesh == null) return;

            if (antennaMesh.shader == null && rCam.game?.rainWorld?.Shaders != null && rCam.game.rainWorld.Shaders.ContainsKey("Basic"))
            {
                antennaMesh.shader = rCam.game.rainWorld.Shaders["Basic"];
            }

            FContainer targetContainer = rCam.ReturnFContainer("Midground");
            if (targetContainer == null) return;


            if (currentContainer == targetContainer)
            {
                addedToContainer = true;
                return;
            }


            if (currentContainer != null)
            {
                try
                {
                    antennaMesh.RemoveFromContainer();
                }
                catch { }
            }


            try
            {
                targetContainer.AddChild(antennaMesh);
                currentContainer = targetContainer;
                addedToContainer = true;
            }
            catch
            {

                addedToContainer = false;
            }
        }

        public override void RemoveSprites()
        {
            if (!spritesInitiated) return;
            try
            {
                antennaMesh?.RemoveFromContainer();
            }
            catch { }
            antennaMesh = null;
            spritesInitiated = false;
            addedToContainer = false;
            currentContainer = null;
        }
    }
}