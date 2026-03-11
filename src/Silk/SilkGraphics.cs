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
        private Vector2[] fadingPositions;
        private Vector2[] fadingLastPositions;
        private float segmentLength;
        private const int PHYSICS_ITERATIONS = 4;

        private bool fadingActive;
        private float fadeAlpha;
        private const float FADE_ALPHA_DECAY = 0.015f;
        private const float FADE_GRAVITY = 0.8f;
        private const float FADE_FRICTION = 0.92f;
        private List<Vector2> lastRenderedPathCache;
        private int shootAnimFrames;
        private bool isSuperJumpFade;
        private bool wasSuperJumping;

        public SilkGraphics(Player player)
        {
            this.player = player;
            this.silk = tinkerSilkData.Get(player);
            this.lastDrawnMode = SilkMode.Retracted;
            this.wasPulling = false;
            this.spritesInitiated = false;
            this.currentTension = 0f;
            this.displayedTension = 0f;
            this.shootAnimFrames = 0;
            fadingActive = false;
            fadeAlpha = 0f;
            lastRenderedPathCache = null;
        }

        public void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            if (spritesInitiated) return;
            currentCamera = rCam;

            lineMesh = TriangleMesh.MakeLongMesh(MAX_ROPE_RENDER_SEGMENTS, false, true);
            lineMesh.shader = rCam.game.rainWorld.Shaders["Basic"];

            tensionIndicator = CreateSprite("pixel", Color.yellow, 1f);
            tensionIndicator.scaleX = 30f;
            tensionIndicator.scaleY = 3f;
            tensionIndicator.alpha = 0f;

            pullIndicator = CreateSprite("Futile_White", new Color(0.3f, 1f, 0.3f), 1.2f);
            pullIndicator.shader = rCam.game.rainWorld.Shaders["FlatLight"];
            pullIndicator.alpha = 0f;

            AddToContainer(rCam.ReturnFContainer("Midground"));

            spritesInitiated = true;
        }

        public void AddToContainer(FContainer newContainer)
        {
            if (newContainer == null) return;

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

            if (tensionIndicator != null)
            {
                tensionIndicator.RemoveFromContainer();
                newContainer.AddChild(tensionIndicator);
            }
        }

        public void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!spritesInitiated || player == null || player.slatedForDeletetion || silk == null)
            {
                HideAllSprites();
                return;
            }

            if (silk.superJumpTimer > 0)
            {
                wasSuperJumping = true;
            }

            if (lastDrawnMode == SilkMode.Retracted && silk.mode == SilkMode.ShootingOut)
            {
                shootAnimFrames = 3;
                fadingActive = false;
                wasSuperJumping = false;
            }

            if (!fadingActive && lastDrawnMode != SilkMode.Retracted && silk.mode == SilkMode.Retracted)
            {
                bool isBridging = silk.Attached && silk.attachedBridge != null;
                if (!isBridging && lastRenderedPathCache != null && lastRenderedPathCache.Count >= 2)
                {
                    StartFadeFromPath(lastRenderedPathCache);
                }
                wasSuperJumping = false;
            }

            Vector2 headPos = Vector2.Lerp(player.bodyChunks[0].lastPos, player.bodyChunks[0].pos, timeStacker);
            Vector2 silkTipPos = Vector2.Lerp(silk.lastPos, silk.pos, timeStacker);
            float distance = Vector2.Distance(headPos, silkTipPos);
            UpdateTension(distance);

            bool ropeShouldBeVisible = (silk.mode != SilkMode.Retracted && silk.mode != SilkMode.Retracting && distance >= 3f) || shootAnimFrames > 0;
            wasPulling = silk.pullingObject;

            if (ropeShouldBeVisible && !fadingActive)
            {
                var path = silk.GetRopePath();
                UpdateUmbilicalStyleMesh(path, timeStacker, camPos);
                CacheCurrentRenderPath();
            }
            else if (fadingActive)
            {
                UpdateFadingSilkWithPhysics(camPos);
            }
            else if (lineMesh != null)
            {
                lineMesh.isVisible = false;
            }

            UpdatePullIndicator(silkTipPos, camPos);
            lastDrawnMode = silk.mode;
            if (shootAnimFrames > 0) shootAnimFrames--;
        }

        private void UpdateUmbilicalStyleMesh(List<Vector2> path, float timeStacker, Vector2 camPos)
        {
            if (path == null || path.Count < 2 || lineMesh == null) return;

            lineMesh.isVisible = true;
            Vector2 startPos = path[0];
            Vector2 endPos = path[path.Count - 1];
            float currentDist = Vector2.Distance(startPos, endPos);
            float stretchFactor = Mathf.Clamp01(currentDist / 500f);
            float baseWidth = Mathf.Lerp(1.2f, 0.6f, stretchFactor);
            Vector2 lastP = GetPathPoint(path, 0f, timeStacker);
            float lastWidth = 0f;

            for (int i = 0; i < MAX_ROPE_RENDER_SEGMENTS; i++)
            {
                float t = (float)i / (MAX_ROPE_RENDER_SEGMENTS - 1);
                Vector2 currentP = GetPathPoint(path, t, timeStacker);
                float lifeEffect = fadingActive ? fadeAlpha : 1f;
                float widthMultiplier = 1f - Mathf.Abs(t * 2f - 1f) * 0.2f;
                float segmentWidth = baseWidth * widthMultiplier * lifeEffect;
                if (fadingActive) segmentWidth *= Mathf.InverseLerp(0f, 0.3f, lifeEffect);

                Vector2 dir = (currentP - lastP).normalized;
                if (dir.magnitude < 0.001f) dir = Vector2.up;
                Vector2 perp = Custom.PerpendicularVector(dir);

                int v = i * 4;
                Vector2 halfOffsetA = perp * ((segmentWidth + lastWidth) * 0.5f);
                Vector2 halfOffsetB = perp * segmentWidth;

                lineMesh.MoveVertice(v, (lastP + currentP) / 2f - halfOffsetA - camPos);
                lineMesh.MoveVertice(v + 1, (lastP + currentP) / 2f + halfOffsetA - camPos);
                lineMesh.MoveVertice(v + 2, currentP - halfOffsetB - camPos);
                lineMesh.MoveVertice(v + 3, currentP + halfOffsetB - camPos);

                Color col = GetSilkColor();
                for (int j = 0; j < 4; j++) lineMesh.verticeColors[v + j] = col;

                lastP = currentP;
                lastWidth = segmentWidth;
            }
        }

        private Vector2 GetPathPoint(List<Vector2> path, float t, float timeStacker)
        {
            float sourceIndexF = t * (path.Count - 1);
            int idxA = Mathf.FloorToInt(sourceIndexF);
            int idxB = Mathf.Min(path.Count - 1, idxA + 1);
            float localT = sourceIndexF - idxA;
            return Vector2.Lerp(path[idxA], path[idxB], localT);
        }

        private void StartFadeFromPath(List<Vector2> path)
        {
            int count = MAX_ROPE_RENDER_SEGMENTS;
            fadingPositions = new Vector2[count];
            fadingLastPositions = new Vector2[count];
            isSuperJumpFade = wasSuperJumping;

            Vector2 anchorPos = path[path.Count - 1];
            Vector2 playerPos = path[0];
            Vector2 snapDir = (anchorPos - playerPos).normalized;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                fadingPositions[i] = GetPathPoint(path, t, 1f);
                Vector2 nudge = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.2f, 0.2f));

                if (isSuperJumpFade)
                {
                    float snapIntensity = Mathf.Lerp(18f, 3f, t);
                    nudge += snapDir * snapIntensity;
                    nudge += Custom.PerpendicularVector(snapDir) * Random.Range(-8f, 8f);
                }

                fadingLastPositions[i] = fadingPositions[i] - nudge;
            }
            segmentLength = Vector2.Distance(fadingPositions[0], fadingPositions[1]);
            fadeAlpha = 1f;
            fadingActive = true;
        }

        private void UpdateFadingSilkWithPhysics(Vector2 camPos)
        {
            if (player.room == null || fadingPositions == null) return;

            float currentFriction = isSuperJumpFade ? 0.96f : FADE_FRICTION;
            float currentGravity = isSuperJumpFade ? 0.2f : FADE_GRAVITY;

            for (int i = 0; i < fadingPositions.Length; i++)
            {
                Vector2 vel = (fadingPositions[i] - fadingLastPositions[i]) * currentFriction;
                fadingLastPositions[i] = fadingPositions[i];
                fadingPositions[i] += vel;
                fadingPositions[i].y -= currentGravity;
            }

            for (int iter = 0; iter < PHYSICS_ITERATIONS; iter++)
            {
                for (int i = 0; i < fadingPositions.Length - 1; i++)
                {
                    float d = Vector2.Distance(fadingPositions[i], fadingPositions[i + 1]);
                    if (d > 0)
                    {
                        float targetLen = isSuperJumpFade ? segmentLength * 1.25f : segmentLength;
                        float diff = (targetLen - d) / d;
                        Vector2 offset = (fadingPositions[i] - fadingPositions[i + 1]) * diff * 0.5f;
                        fadingPositions[i] += offset;
                        fadingPositions[i + 1] -= offset;
                    }
                }

                for (int i = 0; i < fadingPositions.Length; i++)
                {
                    IntVector2 tp = player.room.GetTilePosition(fadingPositions[i]);
                    if (player.room.GetTile(tp).Solid)
                    {
                        FloatRect rect = player.room.TileRect(tp);
                        Vector2 pos = fadingPositions[i];
                        float dL = pos.x - rect.left;
                        float dR = rect.right - pos.x;
                        float dB = pos.y - rect.bottom;
                        float dT = rect.top - pos.y;
                        float m = Mathf.Min(dL, Mathf.Min(dR, Mathf.Min(dB, dT)));
                        if (m == dL) fadingPositions[i].x = rect.left - 0.1f;
                        else if (m == dR) fadingPositions[i].x = rect.right + 0.1f;
                        else if (m == dB) fadingPositions[i].y = rect.bottom - 0.1f;
                        else fadingPositions[i].y = rect.top + 0.1f;
                        fadingLastPositions[i] = Vector2.Lerp(fadingLastPositions[i], fadingPositions[i], 0.6f);
                    }
                }
            }
            UpdateUmbilicalStyleMesh(new List<Vector2>(fadingPositions), 1f, camPos);

            float decay = isSuperJumpFade ? FADE_ALPHA_DECAY * 1.2f : FADE_ALPHA_DECAY;
            fadeAlpha -= decay;
            if (fadeAlpha <= 0) fadingActive = false;
        }

        private void CacheCurrentRenderPath()
        {
            var path = silk.GetRopePath();
            if (path != null && path.Count >= 2) lastRenderedPathCache = new List<Vector2>(path);
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
            if (isSuperJumpFade || (silk.superJumpTimer > 0 && !fadingActive))
                silkColor = Color.Lerp(silkColor, new Color(0.7f, 1f, 1f), 0.5f);
            else if (silk.pullingObject)
                silkColor = Color.Lerp(silkColor, new Color(0.4f, 1f, 0.4f), 0.3f);

            silkColor.a = fadingActive ? fadeAlpha : 1f;
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
            if (!spritesInitiated) return;
            FNode[] sprites = { lineMesh, tensionIndicator, pullIndicator };
            foreach (var sprite in sprites) if (sprite != null) sprite.RemoveFromContainer();
            spritesInitiated = false;
        }

        private void HideAllSprites()
        {
            FNode[] sprites = { lineMesh, tensionIndicator, pullIndicator };
            foreach (var sprite in sprites) if (sprite != null) sprite.isVisible = false;
        }
    }
}