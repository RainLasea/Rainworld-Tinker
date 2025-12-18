using RWCustom;
using System.Collections.Generic;
using Tinker.Silk.Collision;
using UnityEngine;
using Tinker.Silk.Bridge;

namespace tinker.Silk
{
    public enum SilkMode
    {
        Retracted,
        ShootingOut,
        AttachedToTerrain,
        AttachedToObject,
        Retracting
    }

    public class SilkPhysics
    {
        public Vector2 pos, lastPos, vel;
        public SilkMode mode;
        public Player player;
        public BodyChunk baseChunk;
        public Vector2 terrainStuckPos;
        public BodyChunk attachedChunk;
        public PhysicalObject attachedObject;
        public float idealRopeLength;
        public float requestedRopeLength;
        public float elastic;
        public int attachedTime;
        public bool returning;
        private IntVector2[] _cachedRtList = new IntVector2[20];
        public bool pullingObject;

        public SilkBridge attachedBridge = null;
        private int attachedBridgeSeg = -1;
        private float attachedBridgeT = 0f;

        private List<Vector2> ropeSegmentPoints = new List<Vector2>();

        private class SegmentBridgeInfo
        {
            public SilkBridge bridge;
            public int segIndex;
            public float t;
        }
        private List<SegmentBridgeInfo> segmentBridgeAttachments = new List<SegmentBridgeInfo>();

        private const float MIN_ROPE_LENGTH = 0f;
        private const float MAX_ROPE_LENGTH = 800f;
        private const float SHOOT_SPEED = 50f;
        private const float GRAVITY = 0.9f;
        private const float OBJECT_PULL_FORCE = 1.8f;

        private int frameCounter = 0;
        private List<int> segmentLastChanged = new List<int>();

        private const float ADD_MIN_DISTANCE = 6f;
        private const float REMOVE_MAX_DISTANCE_TO_LINE = 8f;
        private const float ANGLE_REMOVE_COS = 0.985f;
        private const int COOLDOWN_FRAMES = 4;
        private const int MAX_CHANGES_PER_UPDATE = 2;


        private const float WALL_CLEARANCE = 3f;
        private const float CORNER_DUAL_SIDE_EPS = 0.9f;
        private const float PARALLEL_DOT_THRESHOLD = 0.35f;
        private const float SLIDE_CLEAR_FORCE = 4f;
        private const float MAX_NODE_WALL_PENETRATION = 5f;

        private readonly ICollisionProvider collisionProvider;

        public SilkPhysics(Player player) : this(player, null) { }

        public SilkPhysics(Player player, ICollisionProvider collisionProvider)
        {
            this.player = player;
            this.baseChunk = player.bodyChunks[0];
            pos = lastPos = baseChunk.pos;
            mode = SilkMode.Retracted;
            elastic = 0f;
            attachedTime = 0;
            pullingObject = false;
            attachedBridge = null;
            attachedBridgeSeg = -1;
            attachedBridgeT = 0f;

            this.collisionProvider = collisionProvider ?? StaticCollisionProvider.Instance;
        }

        public bool Attached => mode == SilkMode.AttachedToTerrain || mode == SilkMode.AttachedToObject;
        public bool AttachedToItem => mode == SilkMode.AttachedToObject && attachedObject != null;

        public List<Vector2> GetRopePath()
        {
            List<Vector2> path = new List<Vector2>();
            path.Add(baseChunk.pos);
            path.AddRange(ropeSegmentPoints);
            path.Add(pos);
            return path;
        }

        public void Release()
        {
            if (mode == SilkMode.Retracted) return;

            mode = SilkMode.Retracted;
            attachedChunk = null;
            attachedObject = null;
            attachedBridge = null;
            attachedBridgeSeg = -1;
            attachedBridgeT = 0f;
            requestedRopeLength = 0f;
            elastic = 0f;
            pullingObject = false;
            returning = false;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        public void Shoot(Vector2 direction)
        {
            if (mode != SilkMode.Retracted) return;

            pos = lastPos = baseChunk.pos;
            vel = direction.normalized * SHOOT_SPEED;
            mode = SilkMode.ShootingOut;
            idealRopeLength = MAX_ROPE_LENGTH;
            requestedRopeLength = 0f;
            elastic = 0f;
            returning = false;
            pullingObject = false;
            attachedBridge = null;
            attachedBridgeSeg = -1;
            attachedBridgeT = 0f;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        public void Update()
        {
            lastPos = pos;
            frameCounter++;

            if (Attached) attachedTime++;
            else attachedTime = 0;

            switch (mode)
            {
                case SilkMode.Retracted:
                    UpdateRetracted();
                    break;
                case SilkMode.ShootingOut:
                    UpdateShootingOut();
                    break;
                case SilkMode.AttachedToTerrain:
                    UpdateAttachedToTerrain();
                    break;
                case SilkMode.AttachedToObject:
                    UpdateAttachedToObject();
                    break;
                case SilkMode.Retracting:
                    mode = SilkMode.Retracted;
                    ropeSegmentPoints.Clear();
                    segmentLastChanged.Clear();
                    segmentBridgeAttachments.Clear();
                    break;
            }

            if (mode != SilkMode.Retracted) Elasticity();

            if (Attached)
            {
                UpdateRopeLength();
                UpdateBridgeAttachedSegments();
                UpdateRopeSegments();
            }
        }

        private void UpdateRetracted()
        {
            requestedRopeLength = 0f;
            pos = baseChunk.pos;
            vel = baseChunk.vel;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private void UpdateShootingOut()
        {
            vel.y -= GRAVITY * Mathf.InverseLerp(0.8f, 0f, elastic);
            pos += vel;

            bool collisionOccurred = false;

            if (player.room == null) return;

            if (collisionProvider.RayTraceBridgesReturnFirstIntersection(player.room, lastPos, pos, out SilkBridge hitBridge, out Vector2 hitPoint, out float hitT))
            {
                AttachToBridge(hitBridge, hitPoint);
                collisionOccurred = true;
            }

            if (!collisionOccurred)
            {

                Vector2? terrainHitPoint = GetPreciseTerrainCollision(lastPos, pos);
                if (terrainHitPoint.HasValue)
                {
                    IntVector2? pathCheck = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, baseChunk.pos, terrainHitPoint.Value);
                    if (pathCheck == null)
                    {
                        AttachToTerrain(terrainHitPoint.Value);
                        collisionOccurred = true;
                    }
                    else
                    {
                        pos = terrainHitPoint.Value;
                        Release();
                        collisionOccurred = true;
                    }
                }
            }

            if (!collisionOccurred && !Custom.DistLess(baseChunk.pos, pos, 60f))
            {
                PhysicalObject hitObject = CheckObjectCollision();
                if (hitObject != null)
                {
                    AttachToObject(hitObject);
                    collisionOccurred = true;
                }
            }

            if (!collisionOccurred)
            {
                if (returning)
                {
                    pos += Custom.RNV() / 1000f;
                    int rayCount = collisionProvider.RayTracedTilesArray(lastPos, pos, _cachedRtList);

                    for (int i = 0; i < rayCount; i++)
                    {
                        if (player.room.GetTile(_cachedRtList[i]).horizontalBeam)
                        {
                            float midY = player.room.MiddleOfTile(_cachedRtList[i]).y;
                            float crossX = Custom.HorizontalCrossPoint(lastPos, pos, midY).x;
                            float clampedX = Mathf.Clamp(crossX, player.room.MiddleOfTile(_cachedRtList[i]).x - 10f,
                                                          player.room.MiddleOfTile(_cachedRtList[i]).x + 10f);
                            Vector2 attachPoint = new Vector2(clampedX, midY);

                            IntVector2? pathCheck = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, baseChunk.pos, attachPoint);
                            if (pathCheck == null)
                            {
                                AttachToTerrain(attachPoint);
                            }
                            else
                            {
                                pos = attachPoint;
                                Release();
                            }
                            break;
                        }
                        if (player.room.GetTile(_cachedRtList[i]).verticalBeam)
                        {
                            float midX = player.room.MiddleOfTile(_cachedRtList[i]).x;
                            float crossY = Custom.VerticalCrossPoint(lastPos, pos, midX).y;
                            float clampedY = Mathf.Clamp(crossY, player.room.MiddleOfTile(_cachedRtList[i]).y - 10f,
                                                          player.room.MiddleOfTile(_cachedRtList[i]).y + 10f);
                            Vector2 attachPoint = new Vector2(midX, clampedY);

                            IntVector2? pathCheck = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, baseChunk.pos, attachPoint);
                            if (pathCheck == null)
                            {
                                AttachToTerrain(attachPoint);
                            }
                            else
                            {
                                pos = attachPoint;
                                Release();
                            }
                            break;
                        }
                    }

                    if (Custom.DistLess(baseChunk.pos, pos, 40f))
                    {
                        mode = SilkMode.Retracted;
                        ropeSegmentPoints.Clear();
                        segmentLastChanged.Clear();
                        segmentBridgeAttachments.Clear();
                    }
                }
                else if (Vector2.Dot(Custom.DirVec(baseChunk.pos, pos), vel.normalized) < 0f)
                {
                    returning = true;
                }
            }
        }


        private bool TryTerrainHit(Vector2 from, Vector2 to, out Vector2 hitPoint, out Vector2 wallNormal, out bool isCorner)
        {
            hitPoint = Vector2.zero;
            wallNormal = Vector2.zero;
            isCorner = false;
            if (player.room == null) return false;

            IntVector2? hitTile = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, from, to);
            if (!hitTile.HasValue) return false;

            FloatRect rect = player.room.TileRect(hitTile.Value);
            Vector2? raw = GetRayRectIntersection(from, to, rect);
            if (!raw.HasValue)
            {

                hitPoint = new Vector2(rect.left + rect.Width / 2f, rect.bottom + rect.Height / 2f);
                wallNormal = (from - to).normalized;
                return true;
            }

            Vector2 p = raw.Value;

            float leftDist = Mathf.Abs(p.x - rect.left);
            float rightDist = Mathf.Abs(p.x - rect.right);
            float bottomDist = Mathf.Abs(p.y - rect.bottom);
            float topDist = Mathf.Abs(p.y - rect.top);

            bool hitLeft = leftDist < 0.01f;
            bool hitRight = rightDist < 0.01f;
            bool hitBottom = bottomDist < 0.01f;
            bool hitTop = topDist < 0.01f;

            int sideCount = (hitLeft ? 1 : 0) + (hitRight ? 1 : 0) + (hitBottom ? 1 : 0) + (hitTop ? 1 : 0);
            isCorner = sideCount >= 2;

            if (isCorner)
            {
                Vector2 n = Vector2.zero;
                if (hitLeft) n += Vector2.left;
                if (hitRight) n += Vector2.right;
                if (hitBottom) n += Vector2.down;
                if (hitTop) n += Vector2.up;
                wallNormal = n.normalized;
            }
            else
            {
                if (hitLeft) wallNormal = Vector2.left;
                else if (hitRight) wallNormal = Vector2.right;
                else if (hitBottom) wallNormal = Vector2.down;
                else if (hitTop) wallNormal = Vector2.up;
                else wallNormal = (from - to).normalized;
            }


            hitPoint = p + wallNormal * WALL_CLEARANCE;
            return true;
        }

        private Vector2? GetPreciseTerrainCollision(Vector2 from, Vector2 to)
        {
            if (TryTerrainHit(from, to, out Vector2 hp, out Vector2 normal, out bool corner))
            {
                return hp;
            }
            return null;
        }

        private Vector2? GetRayRectIntersection(Vector2 from, Vector2 to, FloatRect rect)
        {
            Vector2 direction = to - from;
            Vector2 closest = from;
            float closestDist = float.MaxValue;
            bool found = false;

            if (direction.x != 0)
            {
                float t = (rect.left - from.x) / direction.x;
                if (t >= 0 && t <= 1)
                {
                    float y = from.y + t * direction.y;
                    if (y >= rect.bottom && y <= rect.top)
                    {
                        Vector2 point = new Vector2(rect.left, y);
                        float dist = Vector2.Distance(from, point);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = point;
                            found = true;
                        }
                    }
                }
            }

            if (direction.x != 0)
            {
                float t = (rect.right - from.x) / direction.x;
                if (t >= 0 && t <= 1)
                {
                    float y = from.y + t * direction.y;
                    if (y >= rect.bottom && y <= rect.top)
                    {
                        Vector2 point = new Vector2(rect.right, y);
                        float dist = Vector2.Distance(from, point);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = point;
                            found = true;
                        }
                    }
                }
            }

            if (direction.y != 0)
            {
                float t = (rect.bottom - from.y) / direction.y;
                if (t >= 0 && t <= 1)
                {
                    float x = from.x + t * direction.x;
                    if (x >= rect.left && x <= rect.right)
                    {
                        Vector2 point = new Vector2(x, rect.bottom);
                        float dist = Vector2.Distance(from, point);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = point;
                            found = true;
                        }
                    }
                }
            }

            if (direction.y != 0)
            {
                float t = (rect.top - from.y) / direction.y;
                if (t >= 0 && t <= 1)
                {
                    float x = from.x + t * direction.x;
                    if (x >= rect.left && x <= rect.right)
                    {
                        Vector2 point = new Vector2(x, rect.top);
                        float dist = Vector2.Distance(from, point);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = point;
                            found = true;
                        }
                    }
                }
            }

            return found ? (Vector2?)closest : null;
        }

        private void UpdateBridgeAttachedSegments()
        {
            while (segmentBridgeAttachments.Count < ropeSegmentPoints.Count)
                segmentBridgeAttachments.Add(null);
            while (segmentBridgeAttachments.Count > ropeSegmentPoints.Count)
                segmentBridgeAttachments.RemoveAt(segmentBridgeAttachments.Count - 1);

            for (int i = 0; i < ropeSegmentPoints.Count; i++)
            {
                if (segmentBridgeAttachments[i] != null)
                {
                    var info = segmentBridgeAttachments[i];

                    if (info.bridge == null || info.bridge.room != player.room)
                    {
                        segmentBridgeAttachments[i] = null;
                        continue;
                    }

                    Vector2 newPos = info.bridge.GetPointOnSegment(info.segIndex, info.t);
                    ropeSegmentPoints[i] = newPos;
                }
            }
        }

        private void UpdateRopeSegments()
        {
            if (player.room == null || !Attached) return;

            while (segmentLastChanged.Count < ropeSegmentPoints.Count) segmentLastChanged.Add(0);
            while (segmentLastChanged.Count > ropeSegmentPoints.Count) segmentLastChanged.RemoveAt(segmentLastChanged.Count - 1);

            SlideNodesOffObstacles();
            RemoveUnnecessarySegments();
            AddNecessarySegments();

            while (segmentLastChanged.Count < ropeSegmentPoints.Count) segmentLastChanged.Add(0);
            while (segmentLastChanged.Count > ropeSegmentPoints.Count) segmentLastChanged.RemoveAt(segmentLastChanged.Count - 1);
            while (segmentBridgeAttachments.Count < ropeSegmentPoints.Count) segmentBridgeAttachments.Add(null);
            while (segmentBridgeAttachments.Count > ropeSegmentPoints.Count) segmentBridgeAttachments.RemoveAt(segmentBridgeAttachments.Count - 1);
        }


        private void SlideNodesOffObstacles()
        {
            for (int i = 0; i < ropeSegmentPoints.Count; i++)
            {
                if (segmentBridgeAttachments[i] != null) continue;
                if (frameCounter - segmentLastChanged[i] < COOLDOWN_FRAMES) continue;

                Vector2 point = ropeSegmentPoints[i];
                Vector2 prevPoint = (i == 0) ? baseChunk.pos : ropeSegmentPoints[i - 1];
                Vector2 nextPoint = (i == ropeSegmentPoints.Count - 1) ? pos : ropeSegmentPoints[i + 1];

                Vector2 toPrev = (prevPoint - point).normalized;
                Vector2 toNext = (nextPoint - point).normalized;
                float dotProduct = Vector2.Dot(toPrev, toNext);


                IntVector2? tileProbe = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, prevPoint, point);
                if (tileProbe.HasValue)
                {
                    FloatRect rect = player.room.TileRect(tileProbe.Value);
                    Vector2 normal;
                    bool corner;

                    TryTerrainHit(prevPoint, point, out Vector2 hitP, out normal, out corner);
                    Vector2 slideTarget = point + normal * Mathf.Min(WALL_CLEARANCE * 2f, MAX_NODE_WALL_PENETRATION);
                    IntVector2? blockA = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, prevPoint, slideTarget);
                    IntVector2? blockB = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, nextPoint, slideTarget);

                    if (!blockA.HasValue && !blockB.HasValue)
                    {
                        ropeSegmentPoints[i] = slideTarget;
                        segmentLastChanged[i] = frameCounter;
                        continue;
                    }
                }

                if (dotProduct < -0.5f)
                {
                    Vector2 resultantForce = (toPrev + toNext).normalized;
                    Vector2 slideTarget = point + resultantForce * SLIDE_CLEAR_FORCE;

                    IntVector2? obstacleFromPrev = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, prevPoint, slideTarget);
                    IntVector2? obstacleFromNext = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, nextPoint, slideTarget);

                    if (!obstacleFromPrev.HasValue && !obstacleFromNext.HasValue)
                    {
                        ropeSegmentPoints[i] = slideTarget;
                        segmentLastChanged[i] = frameCounter;
                    }
                }
            }
        }

        private void RemoveUnnecessarySegments()
        {
            for (int i = ropeSegmentPoints.Count - 1; i >= 0; i--)
            {
                if (frameCounter - segmentLastChanged[i] < COOLDOWN_FRAMES) continue;

                Vector2 start = (i == 0) ? baseChunk.pos : ropeSegmentPoints[i - 1];
                Vector2 end = (i == ropeSegmentPoints.Count - 1) ? pos : ropeSegmentPoints[i + 1];
                Vector2 point = ropeSegmentPoints[i];

                IntVector2? hitTile = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, start, end);
                bool bridgeHit = collisionProvider.RayTraceBridgesReturnFirstIntersection(player.room, start, end, out SilkBridge bridge, out Vector2 bridgePoint, out float t);

                if (!hitTile.HasValue && !bridgeHit)
                {
                    bool necessaryCorner = IsNecessaryCornerPoint(point, start, end);

                    if (!necessaryCorner)
                    {
                        float distToLine = DistancePointToLine(point, start, end);
                        Vector2 dirA = (point - start);
                        Vector2 dirB = (end - point);

                        if (dirA.sqrMagnitude > 0.0001f && dirB.sqrMagnitude > 0.0001f)
                        {
                            float cos = Vector2.Dot(dirA.normalized, dirB.normalized);

                            if (distToLine <= REMOVE_MAX_DISTANCE_TO_LINE && cos >= ANGLE_REMOVE_COS)
                            {
                                ropeSegmentPoints.RemoveAt(i);
                                segmentLastChanged.RemoveAt(i);
                                segmentBridgeAttachments.RemoveAt(i);
                            }
                        }
                        else
                        {
                            ropeSegmentPoints.RemoveAt(i);
                            segmentLastChanged.RemoveAt(i);
                            segmentBridgeAttachments.RemoveAt(i);
                        }
                    }
                }
                else
                {

                    if (hitTile.HasValue)
                    {
                        FloatRect rect = player.room.TileRect(hitTile.Value);
                        Vector2? testHit = GetRayRectIntersection(start, end, rect);
                        if (!testHit.HasValue)
                        {
                            ropeSegmentPoints.RemoveAt(i);
                            segmentLastChanged.RemoveAt(i);
                            segmentBridgeAttachments.RemoveAt(i);
                        }
                    }
                }
            }
        }

        private bool IsNecessaryCornerPoint(Vector2 point, Vector2 start, Vector2 end)
        {
            if (player.room == null) return false;

            IntVector2? directPath = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, start, end);
            if (!directPath.HasValue) return false;


            Vector2 toPoint = (point - start).normalized;
            Vector2 fromPoint = (end - point).normalized;
            float angleDot = Vector2.Dot(toPoint, fromPoint);
            if (angleDot > 0.8f) return false;


            IntVector2? startToPoint = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, start, point);
            IntVector2? pointToEnd = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, point, end);
            if (!startToPoint.HasValue || !pointToEnd.HasValue) return false;

            return true;
        }

        private void AddNecessarySegments()
        {
            Vector2 currentStart = baseChunk.pos;
            int iterations = 0;
            int MAX_ITERATIONS = 100;

            for (int i = 0; i <= ropeSegmentPoints.Count && iterations < MAX_ITERATIONS; i++, iterations++)
            {
                Vector2 next = (i < ropeSegmentPoints.Count) ? ropeSegmentPoints[i] : pos;
                float dist = Vector2.Distance(currentStart, next);

                if (dist >= ADD_MIN_DISTANCE)
                {
                    bool bridgeHit = collisionProvider.RayTraceBridgesReturnFirstIntersection(
                        player.room, currentStart, next,
                        out SilkBridge bridge, out Vector2 bridgePoint, out float t);

                    if (bridgeHit)
                    {
                        int segIndex;
                        float tval;
                        Vector2 stable = bridge.GetClosestPoint(bridgePoint, out segIndex, out tval);

                        ropeSegmentPoints.Insert(i, stable);
                        segmentLastChanged.Insert(i, frameCounter);
                        segmentBridgeAttachments.Insert(i, new SegmentBridgeInfo
                        {
                            bridge = bridge,
                            segIndex = segIndex,
                            t = tval
                        });

                        currentStart = stable;
                        continue;
                    }


                    if (TryTerrainHit(currentStart, next, out Vector2 hitP, out Vector2 normal, out bool isCorner))
                    {
                        Vector2 dir = (next - currentStart).normalized;
                        float parallelDot = Mathf.Abs(Vector2.Dot(dir, normal));


                        if (isCorner && parallelDot < PARALLEL_DOT_THRESHOLD)
                        {

                            IntVector2? recheck = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, currentStart, next);
                            if (!recheck.HasValue)
                            {


                                continue;
                            }
                        }

                        ropeSegmentPoints.Insert(i, hitP);
                        segmentLastChanged.Insert(i, frameCounter);
                        segmentBridgeAttachments.Insert(i, null);
                        currentStart = hitP;
                        continue;
                    }
                }

                if (i < ropeSegmentPoints.Count)
                {
                    currentStart = ropeSegmentPoints[i];
                }
            }
        }

        public void DetachPhysicsOnly()
        {
            if (!Attached) return;

            mode = SilkMode.Retracted;
            attachedChunk = null;
            attachedObject = null;
            attachedBridge = null;
            attachedBridgeSeg = -1;
            attachedBridgeT = 0f;
            vel = Vector2.zero;
            elastic = 0f;
            pullingObject = false;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private PhysicalObject CheckObjectCollision()
        {
            if (player.room == null) return null;

            foreach (var obj in player.room.physicalObjects)
            {
                foreach (var item in obj)
                {
                    if (item == player) continue;

                    bool isPullableItem = item is Weapon ||
                                         item is DangleFruit ||
                                         item is SporePlant ||
                                         item is DataPearl ||
                                         item is Rock ||
                                         item is ScavengerBomb ||
                                         item is Spear ||
                                         item is FirecrackerPlant;

                    if (!isPullableItem) continue;

                    for (int i = 0; i < item.bodyChunks.Length; i++)
                    {
                        BodyChunk chunk = item.bodyChunks[i];
                        float distance = Vector2.Distance(pos, chunk.pos);

                        if (distance < chunk.rad + 5f)
                        {
                            return item;
                        }
                    }
                }
            }

            return null;
        }

        private void UpdateAttachedToTerrain()
        {
            if (attachedBridge != null)
            {
                if (attachedBridge.room != player.room || !SilkBridgeManager.GetBridgesInRoom(player.room).Contains(attachedBridge))
                {
                    attachedBridge = null;
                    attachedBridgeSeg = -1;
                    attachedBridgeT = 0f;
                    mode = SilkMode.Retracting;
                    ropeSegmentPoints.Clear();
                    segmentLastChanged.Clear();
                    segmentBridgeAttachments.Clear();
                    return;
                }

                Vector2 newAttachPos = attachedBridge.GetPointOnSegment(attachedBridgeSeg, attachedBridgeT);
                Vector2 bridgeMovement = newAttachPos - terrainStuckPos;
                vel = bridgeMovement;

                terrainStuckPos = newAttachPos;
                pos = newAttachPos;
            }
            else
            {
                pos = terrainStuckPos;
                vel = Vector2.zero;
            }

            if (attachedBridge != null && pullingObject)
            {
                PullAttachedBridge();
            }
        }

        private void UpdateAttachedToObject()
        {
            if (attachedObject != null)
            {
                pos = attachedObject.bodyChunks[0].pos;
                vel = attachedObject.bodyChunks[0].vel;

                if (attachedObject.room != player.room)
                {
                    mode = SilkMode.Retracting;
                    attachedObject = null;
                    attachedChunk = null;
                    ropeSegmentPoints.Clear();
                    segmentLastChanged.Clear();
                    segmentBridgeAttachments.Clear();
                }

                if (pullingObject) PullAttachedObject();
            }
            else if (attachedChunk != null)
            {
                pos = attachedChunk.pos;
                vel = attachedChunk.vel;

                if (attachedChunk.owner.room != player.room)
                {
                    mode = SilkMode.Retracting;
                    attachedChunk = null;
                    ropeSegmentPoints.Clear();
                    segmentLastChanged.Clear();
                    segmentBridgeAttachments.Clear();
                }
            }
            else
            {
                mode = SilkMode.Retracting;
                ropeSegmentPoints.Clear();
                segmentLastChanged.Clear();
                segmentBridgeAttachments.Clear();
            }
        }

        private void PullAttachedBridge()
        {
            if (attachedBridge == null) return;

            Vector2 toPlayer = baseChunk.pos - pos;
            float dist = toPlayer.magnitude;
            if (dist < 10f) return;

            Vector2 pullDir = toPlayer.normalized;
            float pullStrength = Mathf.Clamp(dist * 0.2f, 0.5f, 20f);

            Vector2 force = pullDir * pullStrength;
            attachedBridge.ApplyForceAt(pos, force, 32f);
        }

        private void PullAttachedObject()
        {
            if (attachedObject == null || attachedObject.bodyChunks == null) return;

            Vector2 toPlayer = baseChunk.pos - pos;
            float distance = toPlayer.magnitude;

            if (distance < 20f)
            {
                pullingObject = false;
                return;
            }

            Vector2 pullDirection = toPlayer.normalized;

            foreach (BodyChunk chunk in attachedObject.bodyChunks)
            {
                float mass = chunk.mass;
                float adjustedForce = OBJECT_PULL_FORCE / Mathf.Max(mass, 0.5f);
                chunk.vel += pullDirection * adjustedForce;
                if (chunk.vel.magnitude > 25f)
                {
                    chunk.vel = chunk.vel.normalized * 25f;
                }
            }
        }

        private void UpdateRopeLength()
        {
            if (pullingObject) return;

            elastic = Mathf.Max(0f, elastic - 0.05f);

            if (requestedRopeLength < idealRopeLength)
            {
                requestedRopeLength = Mathf.Min(requestedRopeLength + (1f - elastic) * 10f, idealRopeLength);
            }
            else if (requestedRopeLength > idealRopeLength)
            {
                requestedRopeLength = Mathf.Max(requestedRopeLength - (1f - elastic) * 10f, idealRopeLength);
            }

            requestedRopeLength = Mathf.Clamp(requestedRopeLength, MIN_ROPE_LENGTH, MAX_ROPE_LENGTH);
        }

        public void Elasticity()
        {
            if (mode == SilkMode.Retracted) return;
            if (requestedRopeLength <= 0f) return;

            List<Vector2> ropePath = GetRopePath();
            float totalRopeLength = 0f;

            for (int i = 0; i < ropePath.Count - 1; i++)
            {
                totalRopeLength += Vector2.Distance(ropePath[i], ropePath[i + 1]);
            }

            if (totalRopeLength > requestedRopeLength)
            {
                float excessLength = totalRopeLength - requestedRopeLength;

                Vector2 firstTarget = ropeSegmentPoints.Count > 0 ? ropeSegmentPoints[0] : pos;
                Vector2 delta = firstTarget - baseChunk.pos;
                Vector2 pullDir = delta.normalized;
                float pullAmount = Mathf.Min(excessLength * 0.6f, 15f);
                Vector2 correction = pullDir * pullAmount;

                baseChunk.pos += correction;
                baseChunk.vel -= Vector2.Dot(baseChunk.vel, pullDir) * pullDir * 0.4f;

                if (attachedBridge != null && mode == SilkMode.AttachedToTerrain)
                {
                    Vector2 reactionForce = -correction * 2f;
                    attachedBridge.ApplyForceAt(pos, reactionForce, 24f);
                }

                elastic = Mathf.Min(elastic + 0.15f, 0.8f);
            }
        }

        private void AttachToTerrain(Vector2 pos)
        {
            terrainStuckPos = pos;
            this.pos = pos;
            vel = Vector2.zero;
            mode = SilkMode.AttachedToTerrain;

            float currentDist = Vector2.Distance(baseChunk.pos, terrainStuckPos);
            idealRopeLength = Mathf.Clamp(currentDist, MIN_ROPE_LENGTH, MAX_ROPE_LENGTH);
            requestedRopeLength = idealRopeLength;
            elastic = 0f;
            pullingObject = false;
            attachedBridge = null;
            attachedBridgeSeg = -1;
            attachedBridgeT = 0f;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private void AttachToBridge(SilkBridge bridge, Vector2 point)
        {
            if (bridge == null) return;
            attachedBridge = bridge;

            attachedBridgeSeg = 0;
            attachedBridgeT = 0f;
            Vector2 hit = bridge.GetClosestPoint(point, out int seg, out float t);
            attachedBridgeSeg = seg;
            attachedBridgeT = t;

            terrainStuckPos = hit;
            pos = hit;
            vel = Vector2.zero;
            mode = SilkMode.AttachedToTerrain;

            float currentDist = Vector2.Distance(baseChunk.pos, terrainStuckPos);
            idealRopeLength = Mathf.Clamp(currentDist, MIN_ROPE_LENGTH, MAX_ROPE_LENGTH);
            requestedRopeLength = idealRopeLength;
            elastic = 0f;
            pullingObject = false;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private void AttachToObject(PhysicalObject obj)
        {
            attachedObject = obj;
            attachedChunk = obj.bodyChunks[0];
            pos = attachedChunk.pos;
            vel = attachedChunk.vel;
            mode = SilkMode.AttachedToObject;

            float currentDist = Vector2.Distance(baseChunk.pos, pos);
            idealRopeLength = Mathf.Clamp(currentDist, MIN_ROPE_LENGTH, MAX_ROPE_LENGTH);
            requestedRopeLength = idealRopeLength;
            elastic = 0f;
            pullingObject = false;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private float DistancePointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLength = line.magnitude;
            if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);

            Vector2 normalizedLine = line / lineLength;
            Vector2 toPoint = point - lineStart;
            float t = Mathf.Clamp01(Vector2.Dot(toPoint, normalizedLine) / lineLength);
            Vector2 projection = lineStart + t * line;

            return Vector2.Distance(point, projection);
        }
    }
}