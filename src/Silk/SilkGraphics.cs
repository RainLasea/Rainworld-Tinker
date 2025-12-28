using RWCustom;
using System.Collections.Generic;
using UnityEngine;

namespace tinker.Silk
{
    public class SilkGraphics
    {
        public Player player;
        public SilkPhysics silk;

        private TriangleMesh lineMesh;
        private FSprite tensionIndicator;
        private FSprite pullIndicator;
        private bool spritesInitiated;
        private RoomCamera currentCamera;

        private SilkMode lastDrawnMode;
        private bool wasPulling;

        private float currentTension;
        private float displayedTension;

        private const int MAX_ROPE_RENDER_SEGMENTS = 50;
        private Vector2[] ropeRenderPoints;


        private bool fadingActive;
        private List<Vector2> fadingPath;
        private Vector2[] fadingPositions;
        private Vector2[] fadingVelocities;
        private float fadeAlpha;
        private float fadeThickness;
        private float fadeSwayPhase;
        private const float FADE_ALPHA_DECAY = 0.02f;
        private const float FADE_THICKNESS_DECAY = 0.02f;
        private const float FADE_GRAVITY = 0.15f;
        private List<Vector2> lastRenderedPathCache;
        private int shootAnimFrames;

        public SilkGraphics(Player player)
        {
            this.player = player;
            this.silk = tinkerSilkData.Get(player);
            this.lastDrawnMode = SilkMode.Retracted;
            this.wasPulling = false;
            this.ropeRenderPoints = new Vector2[MAX_ROPE_RENDER_SEGMENTS];
            this.spritesInitiated = false;
            this.currentTension = 0f;
            this.displayedTension = 0f;
            this.shootAnimFrames = 0;

            fadingActive = false;
            fadingPath = null;
            fadeAlpha = 0f;
            fadeThickness = 0f;
            fadeSwayPhase = 0f;
            lastRenderedPathCache = null;
        }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            if (spritesInitiated) return;

            currentCamera = rCam;

            TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[(MAX_ROPE_RENDER_SEGMENTS - 1) * 2];
            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS - 1; i++)
            {
                int vertIndex = i * 4;
                tris[i * 2] = new TriangleMesh.Triangle(vertIndex, vertIndex + 1, vertIndex + 2);
                tris[i * 2 + 1] = new TriangleMesh.Triangle(vertIndex + 1, vertIndex + 2, vertIndex + 3);
            }

            lineMesh = new TriangleMesh("Futile_White", tris, false, false);
            lineMesh.color = Color.white;
            lineMesh.isVisible = false;
            lineMesh.shader = rCam.game.rainWorld.Shaders["Basic"];

            tensionIndicator = CreateSprite("pixel", Color.yellow, 1f);
            tensionIndicator.scaleX = 30f;
            tensionIndicator.scaleY = 3f;
            tensionIndicator.alpha = 0f;

            pullIndicator = CreateSprite("Futile_White", new Color(0.3f, 1f, 0.3f), 1.2f);
            pullIndicator.shader = rCam.game.rainWorld.Shaders["FlatLight"];
            pullIndicator.alpha = 0f;

            FContainer midground = rCam.ReturnFContainer("Midground");
            FContainer hud = rCam.ReturnFContainer("HUD");

            AddToContainer(midground, lineMesh, pullIndicator);
            hud.AddChild(tensionIndicator);

            spritesInitiated = true;
        }

        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!spritesInitiated || player == null || player.slatedForDeletetion || silk == null)
            {
                HideAllSprites();
                return;
            }

            if (lastDrawnMode == SilkMode.Retracted && silk.mode == SilkMode.ShootingOut)
            {
                shootAnimFrames = 3;
                fadingActive = false;
            }

            if (!fadingActive && lastDrawnMode != SilkMode.Retracted && silk.mode == SilkMode.Retracted && lastRenderedPathCache != null && lastRenderedPathCache.Count >= 2)
            {
                StartFadeFromPath(lastRenderedPathCache);
            }

            Vector2 headPos = Vector2.Lerp(player.bodyChunks[0].lastPos, player.bodyChunks[0].pos, timeStacker);
            Vector2 silkTipPos = Vector2.Lerp(silk.lastPos, silk.pos, timeStacker);

            float distance = Vector2.Distance(headPos, silkTipPos);
            UpdateTension(distance);

            bool ropeShouldBeVisible = (silk.mode != SilkMode.Retracted && silk.mode != SilkMode.Retracting && distance >= 3f) || shootAnimFrames > 0;

            wasPulling = silk.pullingObject;

            if (ropeShouldBeVisible && !fadingActive)
            {
                UpdateSilkLine(headPos, silkTipPos, camPos);

                CacheCurrentRenderPath();
            }
            else if (fadingActive)
            {
                UpdateFadingSilkLine(camPos);
            }
            else
            {
                lineMesh.isVisible = false;
            }

            UpdateTensionIndicator(headPos, camPos);
            UpdatePullIndicator(silkTipPos, camPos);


            lastDrawnMode = silk.mode;
            if (shootAnimFrames > 0)
            {
                shootAnimFrames--;
            }
        }

        private void CacheCurrentRenderPath()
        {
            var path = silk.GetRopePath();
            if (path != null && path.Count >= 2)
            {
                lastRenderedPathCache = new List<Vector2>(path);
            }
        }

        private void StartFadeFromPath(List<Vector2> path)
        {
            fadingPath = new List<Vector2>(path);
            int count = Mathf.Min(MAX_ROPE_RENDER_SEGMENTS, fadingPath.Count);
            fadingPositions = new Vector2[count];
            fadingVelocities = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                fadingPositions[i] = fadingPath[i];
                float randX = (Random.value - 0.5f) * 1.2f;
                float randY = -Random.Range(0.2f, 0.6f);
                fadingVelocities[i] = new Vector2(randX, randY);
            }

            fadeAlpha = 1f;
            fadeThickness = 1f;
            fadeSwayPhase = Random.Range(0f, 6.28318f);
            fadingActive = true;
        }

        private void UpdateTension(float distance)
        {
            if (!silk.Attached || silk.pullingObject)
            {
                currentTension = 0f;
                displayedTension = Mathf.Lerp(displayedTension, 0f, 0.2f);
                return;
            }

            float overExtension = Mathf.Max(0f, distance - silk.requestedRopeLength);
            currentTension = overExtension == 0f ? 0f : Mathf.Clamp01(overExtension / 100f);
            displayedTension = Mathf.Lerp(displayedTension, currentTension, 0.6f);
        }

        private void UpdateTensionIndicator(Vector2 headPos, Vector2 camPos)
        {
            if (!silk.Attached || displayedTension < 0.05f || silk.pullingObject)
            {
                tensionIndicator.isVisible = false;
                return;
            }

            tensionIndicator.isVisible = true;
            tensionIndicator.x = headPos.x - camPos.x;
            tensionIndicator.y = headPos.y - camPos.y + 25f;
            tensionIndicator.scaleX = 30f * displayedTension;
            tensionIndicator.alpha = Mathf.Clamp01(displayedTension * 0.8f);
            tensionIndicator.color = Color.Lerp(Color.yellow, Color.red, displayedTension);
        }

        private void UpdatePullIndicator(Vector2 tipPos, Vector2 camPos)
        {
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

        private void UpdateSilkLine(Vector2 startPos, Vector2 endPos, Vector2 camPos)
        {
            lineMesh.isVisible = true;

            List<Vector2> ropePath = silk.GetRopePath();
            CalculateRenderPointsFromPath(ropePath);

            float baseWidth = 3f;
            float width = silk.pullingObject ? baseWidth * 1.2f : baseWidth * (1f + displayedTension * 0.4f);
            if (silk.mode == SilkMode.ShootingOut || shootAnimFrames > 0) width = baseWidth * 0.8f;

            int segmentCount = UpdateMeshVertices(width, camPos);
            ClearUnusedVertices(segmentCount);

            lineMesh.color = GetSilkColor();
            lineMesh.alpha = 1f;
        }

        private void UpdateFadingSilkLine(Vector2 camPos)
        {
            if (!fadingActive || fadingPositions == null)
            {
                lineMesh.isVisible = false;
                return;
            }

            lineMesh.isVisible = true;
            fadeSwayPhase += 0.05f;

            int count = fadingPositions.Length;
            for (int i = 0; i < count; i++)
            {
                Vector2 vel = fadingVelocities[i];
                vel.y -= FADE_GRAVITY * 0.3f;
                vel.x *= 0.99f;

                float sway = Mathf.Sin(fadeSwayPhase + i * 0.35f) * 0.4f * fadeAlpha;
                vel.x += sway * 0.02f;

                fadingVelocities[i] = vel;
                fadingPositions[i] += vel;
            }

            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS; i++)
                ropeRenderPoints[i] = Vector2.zero;

            if (count >= 2)
            {
                ropeRenderPoints[0] = fadingPositions[0];
                ropeRenderPoints[MAX_ROPE_RENDER_SEGMENTS - 1] = fadingPositions[count - 1];

                for (int i = 1; i < MAX_ROPE_RENDER_SEGMENTS - 1; i++)
                {
                    float t = (float)i / (MAX_ROPE_RENDER_SEGMENTS - 1);
                    float sourceIndexF = t * (count - 1);
                    int idxA = Mathf.FloorToInt(sourceIndexF);
                    int idxB = Mathf.Min(count - 1, idxA + 1);
                    float localT = sourceIndexF - idxA;
                    ropeRenderPoints[i] = Vector2.Lerp(fadingPositions[idxA], fadingPositions[idxB], localT);
                }
            }

            float baseWidth = 3f * fadeThickness;
            int segmentCount = 0;

            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS - 1; i++)
            {
                if (ropeRenderPoints[i] == Vector2.zero && ropeRenderPoints[i + 1] == Vector2.zero)
                    break;

                Vector2 segStart = ropeRenderPoints[i];
                Vector2 segEnd = ropeRenderPoints[i + 1];
                if (segStart == Vector2.zero || segEnd == Vector2.zero) break;

                Vector2 segDir = (segEnd - segStart).normalized;
                Vector2 perpendicular = Custom.PerpendicularVector(segDir);
                float segmentT = (float)i / (MAX_ROPE_RENDER_SEGMENTS - 1);

                float widthMultiplier = (1f - Mathf.Abs(segmentT * 2f - 1f) * 0.4f) * (1f - segmentT * 0.15f);
                float segmentWidth = baseWidth * Mathf.Clamp01(widthMultiplier);

                int vertIndex = i * 4;
                lineMesh.MoveVertice(vertIndex, segStart - perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 1, segStart + perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 2, segEnd - perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 3, segEnd + perpendicular * segmentWidth * 0.5f - camPos);

                segmentCount++;
            }

            ClearUnusedVertices(segmentCount);

            Color baseColor = new Color(1f, 1f - 0.4f * (1f - fadeAlpha), 1f - 0.6f * (1f - fadeAlpha), fadeAlpha);
            lineMesh.color = baseColor;
            lineMesh.alpha = fadeAlpha;

            fadeAlpha = Mathf.Max(0f, fadeAlpha - FADE_ALPHA_DECAY);
            fadeThickness = Mathf.Max(0f, fadeThickness - FADE_THICKNESS_DECAY);

            if (fadeAlpha <= 0f || fadeThickness <= 0f)
            {
                fadingActive = false;
                fadingPath = null;
                fadingPositions = null;
                fadingVelocities = null;
                lineMesh.isVisible = false;
                lastRenderedPathCache = null;
            }
        }

        private int UpdateMeshVertices(float width, Vector2 camPos)
        {
            int segmentCount = 0;
            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS - 1; i++)
            {
                if (ropeRenderPoints[i] == Vector2.zero && ropeRenderPoints[i + 1] == Vector2.zero)
                    break;

                Vector2 segStart = ropeRenderPoints[i];
                Vector2 segEnd = ropeRenderPoints[i + 1];

                if (segStart == Vector2.zero || segEnd == Vector2.zero)
                    break;

                Vector2 segDir = (segEnd - segStart).normalized;
                Vector2 perpendicular = Custom.PerpendicularVector(segDir);
                float segmentT = (float)i / (MAX_ROPE_RENDER_SEGMENTS - 1);
                float widthMultiplier = 1f - Mathf.Abs(segmentT * 2f - 1f) * 0.3f;
                float segmentWidth = width * widthMultiplier;

                int vertIndex = i * 4;
                lineMesh.MoveVertice(vertIndex, segStart - perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 1, segStart + perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 2, segEnd - perpendicular * segmentWidth * 0.5f - camPos);
                lineMesh.MoveVertice(vertIndex + 3, segEnd + perpendicular * segmentWidth * 0.5f - camPos);

                segmentCount++;
            }
            return segmentCount;
        }

        private void ClearUnusedVertices(int segmentCount)
        {
            for (int i = segmentCount; i < MAX_ROPE_RENDER_SEGMENTS - 1; i++)
            {
                int vertIndex = i * 4;
                lineMesh.MoveVertice(vertIndex, Vector2.zero);
                lineMesh.MoveVertice(vertIndex + 1, Vector2.zero);
                lineMesh.MoveVertice(vertIndex + 2, Vector2.zero);
                lineMesh.MoveVertice(vertIndex + 3, Vector2.zero);
            }
        }

        private Color GetSilkColor()
        {
            Color silkColor = Color.white;

            if (silk.pullingObject)
                silkColor = Color.Lerp(silkColor, new Color(0.3f, 1f, 0.3f), 0.4f);
            else
                silkColor = Color.Lerp(silkColor, new Color(1f, 0.3f, 0.3f), displayedTension * 0.6f);

            silkColor.a = 1f;
            return silkColor;
        }

        private void CalculateRenderPointsFromPath(List<Vector2> ropePath)
        {
            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS; i++)
                ropeRenderPoints[i] = Vector2.zero;

            if (ropePath.Count < 2) return;

            float totalLength = 0f;
            for (int i = 0; i < ropePath.Count - 1; i++)
                totalLength += Vector2.Distance(ropePath[i], ropePath[i + 1]);

            if (totalLength < 0.1f) return;

            DistributeRenderPointsAlongPath(ropePath, totalLength);
        }

        private void DistributeRenderPointsAlongPath(List<Vector2> ropePath, float totalLength)
        {
            int renderPointIndex = 0;
            float currentDistance = 0f;
            float segmentLength = totalLength / (MAX_ROPE_RENDER_SEGMENTS - 1);

            ropeRenderPoints[renderPointIndex++] = ropePath[0];

            for (int pathIndex = 0; pathIndex < ropePath.Count - 1 && renderPointIndex < MAX_ROPE_RENDER_SEGMENTS; pathIndex++)
            {
                Vector2 segStart = ropePath[pathIndex];
                Vector2 segEnd = ropePath[pathIndex + 1];
                float segDist = Vector2.Distance(segStart, segEnd);

                while (currentDistance + segDist >= segmentLength * renderPointIndex && renderPointIndex < MAX_ROPE_RENDER_SEGMENTS)
                {
                    float targetDist = segmentLength * renderPointIndex;
                    float remainingDist = targetDist - currentDistance;
                    float t = remainingDist / segDist;

                    Vector2 point = Vector2.Lerp(segStart, segEnd, t);

                    if (silk.Attached && !silk.pullingObject)
                    {
                        float pathT = (float)renderPointIndex / (MAX_ROPE_RENDER_SEGMENTS - 1);
                        float sag = Mathf.Sin(pathT * Mathf.PI) * (totalLength * 0.03f);
                        point.y -= sag;
                    }

                    ropeRenderPoints[renderPointIndex++] = point;
                    if (renderPointIndex >= MAX_ROPE_RENDER_SEGMENTS) break;
                }

                currentDistance += segDist;
            }

            if (renderPointIndex < MAX_ROPE_RENDER_SEGMENTS)
                ropeRenderPoints[MAX_ROPE_RENDER_SEGMENTS - 1] = ropePath[ropePath.Count - 1];
        }

        private FSprite CreateSprite(string element, Color color, float scale)
        {
            var sprite = new FSprite(element, true);
            sprite.color = color;
            sprite.scale = scale;
            sprite.isVisible = false;
            return sprite;
        }

        public void AddToContainer(FContainer container, params FNode[] nodes)
        {
            foreach (var node in nodes)
                container.AddChild(node);
        }

        public void RemoveSprites()
        {
            if (!spritesInitiated) return;

            FNode[] sprites = { lineMesh, tensionIndicator, pullIndicator };
            foreach (var sprite in sprites)
            {
                if (sprite != null)
                    sprite.RemoveFromContainer();
            }
            spritesInitiated = false;
        }

        private void HideAllSprites()
        {
            FNode[] sprites = { lineMesh, tensionIndicator, pullIndicator };
            foreach (var sprite in sprites)
                if (sprite != null)
                    sprite.isVisible = false;
        }
    }
}