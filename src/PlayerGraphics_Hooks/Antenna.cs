using RWCustom;
using System;
using tinker;
using UnityEngine;
using SlugBase.DataTypes;

namespace Tinker.PlayerGraphics_Hooks
{
    public abstract class Antenna
    {
        protected Vector2[] lastPoints;
        public abstract void Update();
        public abstract void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam);
        public abstract void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos);
        public abstract void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer container);
        public abstract void RemoveSprites();
        public abstract void Reset();

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
            enabled = player.slugcatStats.name.ToString() == Plugin.SlugName.ToString();
            if (!enabled) return;
            leftAntenna = new MainAntenna(graphics, player, true, 0f);
            rightAntenna = new MainAntenna(graphics, player, false, 0f);
            leftMiniAntenna = new MiniAntenna(graphics, player, true, 0f);
            rightMiniAntenna = new MiniAntenna(graphics, player, false, 0f);
            Reset(); // Call Reset on creation to initialize positions
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

        public void Reset()
        {
            if (!enabled) return;
            leftAntenna?.Reset();
            rightAntenna?.Reset();
            leftMiniAntenna?.Reset();
            rightMiniAntenna?.Reset();
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
            lastPoints = new Vector2[segments + 1];
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
        protected float baseYOffset = 7.5f;
        protected float lateralOffset = 2f;

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

        private float zOffset = 0f;
        private Vector2 tipVel;

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
            if (player == null || player.room == null || graphics == null) return;

            for (int i = 0; i <= segments; i++)
            {
                lastPoints[i] = currentPoints[i];
            }

            CalculateIdealPoints();
            UpdatePhysics();
        }

        public override void Reset()
        {
            if (player == null || player.room == null || graphics == null) return;
            for (int i = 0; i <= segments; i++)
            {
                currentPoints[i] = player.mainBodyChunk.pos;
                lastPoints[i] = player.mainBodyChunk.pos;
                velocities[i] = Vector2.zero;
            }
            tipVel = Vector2.zero;

            CalculateIdealPoints();

            for (int i = 0; i <= segments; i++)
            {
                currentPoints[i] = idealPoints[i];
                lastPoints[i] = idealPoints[i];
            }
        }

        private static Vector2 Rotate(Vector2 v, float angle)
        {
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        private void CalculateIdealPoints()
        {
            if (graphics?.head == null || player?.mainBodyChunk == null) return;
            Vector2 headPos = graphics.head.pos;
            Vector2 bodyDir = (player.mainBodyChunk.pos - player.bodyChunks[1].pos).normalized;
            Vector2 bodyPerp = Custom.PerpendicularVector(bodyDir);

            bool isCrawling = player.bodyMode == Player.BodyModeIndex.Crawl || player.bodyMode == Player.BodyModeIndex.CorridorClimb;
            bool facingLeft = graphics.lookDirection.x < 0;

            float side = isLeft ? -1f : 1f;

            if (isCrawling)
            {
                float bodyAngle = Custom.VecToDeg(bodyDir);
                float flip = (bodyAngle > 0f && bodyAngle < 180f) ? 1f : -1f;
                if (isLeft) flip *= -1f;

                Vector2 targetDir = (bodyDir + bodyPerp * side * 0.4f * flip).normalized;
                Vector2 tipTarget = headPos + targetDir * (segments * segmentLength);

                tipVel *= 0.8f;
                tipVel += (tipTarget - currentPoints[segments]) * 0.1f;
                tipVel += Custom.RNV() * 0.2f;

                Vector2 bezPt2 = headPos + bodyDir * 20f;
                Vector2 tipPos = currentPoints[segments] + tipVel;

                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    idealPoints[i] = Custom.Bezier(headPos, bezPt2, tipPos, tipPos, t);
                }
            }
            else
            {
                Vector2 neckPos = graphics.drawPositions[1, 0];
                float bodyRotation = Custom.VecToDeg(bodyDir);
                float currentBaseYOffset = this.baseYOffset;
                float currentLateralOffset = this.lateralOffset;

                float baseXOffset = side * -1f * (facingLeft ? 3f : 1.5f);
                baseXOffset += side * -1f * currentLateralOffset;

                Vector2 headDir = (graphics.head.pos - graphics.drawPositions[0, 0]).normalized;
                if (headDir.sqrMagnitude < 0.1f) headDir = (graphics.head.pos - neckPos).normalized;
                if (headDir.sqrMagnitude < 0.1f) headDir = Vector2.up;


                Vector2 headOffset = new Vector2(-3f * headDir.x, -2.5f * headDir.y);
                Vector2 bodyOffset = -3f * bodyDir;

                Vector2 rotatedBase = Rotate(new Vector2(baseXOffset, currentBaseYOffset), Mathf.Deg2Rad * -bodyRotation * (0.4f * (1f - Mathf.Abs(Mathf.Cos(Mathf.Deg2Rad * bodyRotation))) + 1f));
                idealPoints[0] = headPos + headOffset + bodyOffset + rotatedBase;

                Vector2 antennaUpDirection = (bodyDir + Vector2.up * 0.5f).normalized;

                Vector2 lookDir = graphics.lookDirection;
                float horizontalLook = (facingLeft ? -1 : 1) * 0.3f;
                float verticalLook = lookDir.y * 0.5f;

                Vector2 finalDir = (antennaUpDirection + new Vector2(horizontalLook, verticalLook)).normalized;
                float baseAngle = Custom.VecToDeg(finalDir);

                baseAngle += angleOffset;
                baseAngle += (bodyRotation * 0.12f) * side * -1f;

                for (int i = 1; i <= segments; i++)
                {
                    float angle = baseAngle;
                    float outwardAngle = side * -1f * 40f;
                    float inwardAngle = side * 50f;

                    if (i == 1) angle += outwardAngle;
                    else if (i == 2) angle += side * -1f * 5.7f;
                    else if (i == 3) angle += inwardAngle;
                    Vector2 dir = Custom.DegToVec(angle);
                    idealPoints[i] = idealPoints[i - 1] + dir * segmentLength;
                }
            }

            if (isCrawling)
            {
                bool shouldBeBehind = (facingLeft && !isLeft) || (!facingLeft && isLeft);
                zOffset = shouldBeBehind ? -1f : 1f;
            }
            else
            {
                zOffset = 1f;
            }
        }

        private void UpdatePhysics()
        {
            for (int i = 0; i <= segments; i++)
            {
                currentPoints[i] = idealPoints[i];
            }

            for (int i = 1; i <= segments; i++)
            {
                Vector2 toNext = currentPoints[i] - currentPoints[i - 1];
                float dist = toNext.magnitude;
                float stretch = dist - segmentLength;
                if (dist > 0.01f)
                {
                    Vector2 dir = toNext / dist;
                    currentPoints[i] -= dir * stretch * 0.5f;
                    currentPoints[i - 1] += dir * stretch * 0.5f;
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
                    Array.Resize(ref sLeaser.sprites, sLeaser.sprites.Length + 1);
                    sLeaser.sprites[sLeaser.sprites.Length - 1] = antennaMesh;
                }
            }

            Reset();

            spritesInitiated = true;
            addedToContainer = false;
            currentContainer = null;
            TryAddToContainer(rCam);
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!spritesInitiated || antennaMesh == null || player.room == null) return;

            if (!addedToContainer && rCam != null)
            {
                TryAddToContainer(rCam);
                if (!addedToContainer) return;
            }

            FSprite headSprite = sLeaser.sprites[3];
            if (zOffset < 0)
            {
                antennaMesh.MoveInFrontOfOtherNode(headSprite);
                FContainer container = headSprite.container;
                if (container != null && antennaMesh != null)
                {
                    container.RemoveChild(antennaMesh);
                    int headIndex = container.GetChildIndex(headSprite);
                    container.AddChildAtIndex(antennaMesh, Math.Max(0, headIndex));
                }
            }
            else
            {
                antennaMesh.MoveInFrontOfOtherNode(headSprite);
            }

            UpdateAntennaMesh(sLeaser, camPos, timeStacker);
        }

        private void UpdateAntennaMesh(RoomCamera.SpriteLeaser sLeaser, Vector2 camPos, float timeStacker)
        {
            if (antennaMesh == null || antennaMesh.verticeColors == null) return;

            Color color1;
            Color color2;

            try
            {
                color1 = PlayerColor.GetCustomColor(graphics, "AntennaBase");
                color2 = PlayerColor.GetCustomColor(graphics, "AntennaTip");
            }
            catch (Exception e)
            {
                Debug.LogError("TinkerMod: Failed to get custom colors for antenna! " + e.Message);
                color1 = Color.magenta;
                color2 = Color.yellow;
            }

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