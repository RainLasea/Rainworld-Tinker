using RWCustom;
using System.Collections.Generic;
using Tinker.Silk.Bridge;
using Tinker.Silk.Collision;
using UnityEngine;

namespace tinker.Silk
{
    public enum SilkMode { Retracted, ShootingOut, AttachedToTerrain, AttachedToObject, Retracting }

    public class SilkPhysics
    {
        public Vector2 pos, lastPos, vel;
        public SilkMode mode = SilkMode.Retracted;
        public Player player;
        public BodyChunk baseChunk;
        public Vector2 terrainStuckPos;
        public PhysicalObject attachedObject;
        public SilkBridge attachedBridge;
        private int attachedBridgeSeg = -1;
        private float attachedBridgeT = 0f;
        public float idealRopeLength, requestedRopeLength, elastic;
        public int attachedTime;
        public bool returning, pullingObject, instantDisappear;
        private List<Vector2> ropeSegmentPoints = new List<Vector2>();
        private List<int> segmentLastChanged = new List<int>();
        private List<SegmentBridgeInfo> segmentBridgeAttachments = new List<SegmentBridgeInfo>();
        private const float MAX_ROPE_LENGTH = 1200f;
        private const float SHOOT_SPEED = 50f;
        private const float GRAVITY = 0.9f;
        private const float OBJECT_PULL_FORCE = 1.8f;
        private const int COOLDOWN_FRAMES = 4;
        private const float WALL_OFFSET = 1.5f;
        private const int MAX_SEGMENTS = 50;
        private readonly ICollisionProvider collisionProvider;
        private int frameCounter;

        public SilkPhysics(Player player, ICollisionProvider collisionProvider = null)
        {
            this.player = player;
            this.baseChunk = player.bodyChunks[0];
            this.collisionProvider = collisionProvider ?? StaticCollisionProvider.Instance;
            ResetState();
        }

        public bool Attached => mode == SilkMode.AttachedToTerrain || mode == SilkMode.AttachedToObject;
        public bool AttachedToItem => mode == SilkMode.AttachedToObject && attachedObject != null;

        public void Update()
        {
            if ((player.room == null || player.slatedForDeletetion) && Attached) { Release(true); return; }
            lastPos = pos;
            frameCounter++;
            attachedTime = Attached ? attachedTime + 1 : 0;
            switch (mode)
            {
                case SilkMode.Retracted: UpdateRetracted(); break;
                case SilkMode.ShootingOut: UpdateShootingOut(); break;
                case SilkMode.AttachedToTerrain: UpdateAttachedToTerrain(); break;
                case SilkMode.AttachedToObject: UpdateAttachedToObject(); break;
                case SilkMode.Retracting: Release(); break;
            }
            if (mode != SilkMode.Retracted && Attached)
            {
                Elasticity();
                UpdateRopeLength();
                UpdateRopeLogic();
            }
        }

        public void Shoot(Vector2 direction)
        {
            ResetState();
            pos = lastPos = baseChunk.pos;
            vel = direction.normalized * SHOOT_SPEED;
            mode = SilkMode.ShootingOut;
            idealRopeLength = MAX_ROPE_LENGTH;
        }

        public void Release(bool instant = false) { instantDisappear = instant; ResetState(); }

        public void DetachPhysicsOnly()
        {
            if (!Attached) return;
            mode = SilkMode.Retracted;
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

        public List<Vector2> GetRopePath()
        {
            var list = new List<Vector2> { baseChunk.pos };
            list.AddRange(ropeSegmentPoints);
            list.Add(pos);
            return list;
        }

        private void ResetState()
        {
            mode = SilkMode.Retracted;
            attachedObject = null;
            attachedBridge = null;
            requestedRopeLength = elastic = 0f;
            pullingObject = returning = false;
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private void UpdateRetracted() => pos = lastPos = baseChunk.pos;

        private void UpdateShootingOut()
        {
            vel.y -= GRAVITY * Mathf.InverseLerp(0.8f, 0f, elastic);
            Vector2 nextPos = pos + vel;
            if (Vector2.Distance(baseChunk.pos, nextPos) > MAX_ROPE_LENGTH) { Release(); return; }
            if (collisionProvider.RayTraceBridgesReturnFirstIntersection(player.room, pos, nextPos, out var bridge, out var hitP, out _))
            {
                AttachToBridge(bridge, hitP);
                return;
            }
            if (TraceTerrainCollision(pos, nextPos, out Vector2 terrainHit, out Vector2 normal))
            {
                Vector2 safePos = terrainHit + normal * WALL_OFFSET;
                if (collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, baseChunk.pos, safePos) == null)
                    AttachToTerrain(safePos);
                else
                {
                    pos = safePos;
                    Release();
                }
                return;
            }
            PhysicalObject hitObj = CheckObjectCollision(nextPos);
            if (hitObj != null && !Custom.DistLess(baseChunk.pos, nextPos, 60f))
            {
                pos = nextPos;
                AttachToObject(hitObj);
                return;
            }
            pos = nextPos;
            if (returning || Vector2.Dot(Custom.DirVec(baseChunk.pos, pos), vel.normalized) < -0.1f)
            {
                returning = true;
                pos += (baseChunk.pos - pos).normalized * 5f;
                if (Custom.DistLess(baseChunk.pos, pos, 40f)) ResetState();
            }
        }

        private void UpdateRopeLogic()
        {
            for (int i = 0; i < segmentBridgeAttachments.Count; i++)
            {
                var info = segmentBridgeAttachments[i];
                if (info?.bridge != null && info.bridge.room == player.room)
                    ropeSegmentPoints[i] = info.bridge.GetPointOnSegment(info.segIndex, info.t);
            }
            RemoveUnnecessarySegments();
            AddNecessarySegments();
            SlideNodesOffObstacles();
        }

        private void AttachToTerrain(Vector2 hitPos)
        {
            terrainStuckPos = pos = hitPos;
            vel = Vector2.zero;
            mode = SilkMode.AttachedToTerrain;
            SetupRopeAfterAttach();
        }

        private void AttachToBridge(SilkBridge bridge, Vector2 point)
        {
            attachedBridge = bridge;
            terrainStuckPos = pos = bridge.GetClosestPoint(point, out attachedBridgeSeg, out attachedBridgeT);
            mode = SilkMode.AttachedToTerrain;
            SetupRopeAfterAttach();
        }

        private void AttachToObject(PhysicalObject obj)
        {
            attachedObject = obj;
            pos = obj.bodyChunks[0].pos;
            mode = SilkMode.AttachedToObject;
            SetupRopeAfterAttach();
        }

        private void SetupRopeAfterAttach()
        {
            idealRopeLength = requestedRopeLength = Mathf.Clamp(Vector2.Distance(baseChunk.pos, pos), 0, MAX_ROPE_LENGTH);
            ropeSegmentPoints.Clear();
            segmentLastChanged.Clear();
            segmentBridgeAttachments.Clear();
        }

        private void Elasticity()
        {
            float totalLen = GetTotalRopeLength();
            if (totalLen > requestedRopeLength)
            {
                Vector2 target = ropeSegmentPoints.Count > 0 ? ropeSegmentPoints[0] : pos;
                Vector2 pullDir = (target - baseChunk.pos).normalized;
                float pull = Mathf.Min((totalLen - requestedRopeLength) * 0.6f, 15f);
                baseChunk.pos += pullDir * pull;
                baseChunk.vel -= Vector2.Dot(baseChunk.vel, pullDir) * pullDir * 0.4f;
                elastic = Mathf.Min(elastic + 0.15f, 0.8f);
            }
        }

        private float GetTotalRopeLength()
        {
            float len = Vector2.Distance(baseChunk.pos, ropeSegmentPoints.Count > 0 ? ropeSegmentPoints[0] : pos);
            for (int i = 0; i < ropeSegmentPoints.Count; i++)
            {
                Vector2 next = (i == ropeSegmentPoints.Count - 1) ? pos : ropeSegmentPoints[i + 1];
                len += Vector2.Distance(ropeSegmentPoints[i], next);
            }
            return len;
        }

        private void UpdateRopeLength()
        {
            if (pullingObject) return;
            elastic = Mathf.Max(0f, elastic - 0.05f);
            requestedRopeLength = Mathf.MoveTowards(requestedRopeLength, idealRopeLength, (1f - elastic) * 10f);
        }

        private PhysicalObject CheckObjectCollision(Vector2 checkPos)
        {
            if (player.room?.physicalObjects == null) return null;
            for (int i = 0; i < player.room.physicalObjects.Length; i++)
            {
                for (int j = 0; j < player.room.physicalObjects[i].Count; j++)
                {
                    PhysicalObject item = player.room.physicalObjects[i][j];
                    if (item == player || !IsPullable(item)) continue;
                    for (int k = 0; k < item.bodyChunks.Length; k++)
                        if (Custom.DistLess(checkPos, item.bodyChunks[k].pos, item.bodyChunks[k].rad + 5f)) return item;
                }
            }
            return null;
        }

        private bool IsPullable(PhysicalObject o) => o is Weapon || o is DangleFruit || o is Rock || o is Spear || o is DataPearl;

        private void UpdateAttachedToTerrain()
        {
            if (attachedBridge != null)
            {
                if (attachedBridge.room != player.room) { Release(); return; }
                pos = terrainStuckPos = attachedBridge.GetPointOnSegment(attachedBridgeSeg, attachedBridgeT);
                if (pullingObject) attachedBridge.ApplyForceAt(pos, (baseChunk.pos - pos).normalized * 5f, 32f);
            }
            else pos = terrainStuckPos;
        }

        private void UpdateAttachedToObject()
        {
            if (attachedObject == null || attachedObject.room != player.room) { Release(); return; }
            pos = attachedObject.bodyChunks[0].pos;
            if (pullingObject) PullAttachedObject();
        }

        private void PullAttachedObject()
        {
            Vector2 dir = (baseChunk.pos - pos).normalized;
            if (Custom.DistLess(baseChunk.pos, pos, 20f)) { pullingObject = false; return; }
            for (int i = 0; i < attachedObject.bodyChunks.Length; i++)
            {
                BodyChunk chunk = attachedObject.bodyChunks[i];
                chunk.vel += dir * (OBJECT_PULL_FORCE / Mathf.Max(chunk.mass, 0.5f));
                if (chunk.vel.magnitude > 25f) chunk.vel = chunk.vel.normalized * 25f;
            }
        }

        private bool TraceTerrainCollision(Vector2 from, Vector2 to, out Vector2 hitPoint, out Vector2 normal)
        {
            hitPoint = to;
            normal = Vector2.zero;
            if (Custom.DistLess(from, to, 0.1f)) return false;
            IntVector2? hitTile = collisionProvider.RayTraceTilesForTerrainReturnFirstSolid(player.room, from, to);
            if (!hitTile.HasValue) return false;
            FloatRect rect = player.room.TileRect(hitTile.Value);
            Vector2 dir = to - from;
            Vector2 invDir = new Vector2(1f / (Mathf.Abs(dir.x) < 0.0001f ? 0.0001f * Mathf.Sign(dir.x) : dir.x), 1f / (Mathf.Abs(dir.y) < 0.0001f ? 0.0001f * Mathf.Sign(dir.y) : dir.y));
            float t1 = (rect.left - from.x) * invDir.x;
            float t2 = (rect.right - from.x) * invDir.x;
            float t3 = (rect.bottom - from.y) * invDir.y;
            float t4 = (rect.top - from.y) * invDir.y;
            float tMin = Mathf.Max(Mathf.Min(t1, t2), Mathf.Min(t3, t4));
            float tMax = Mathf.Min(Mathf.Max(t1, t2), Mathf.Max(t3, t4));
            if (tMax < 0 || tMin > tMax)
            {
                hitPoint.x = Mathf.Clamp(to.x, rect.left + 0.1f, rect.right - 0.1f);
                hitPoint.y = Mathf.Clamp(to.y, rect.bottom + 0.1f, rect.top - 0.1f);
                normal = (from - to).normalized;
                return true;
            }
            hitPoint = from + dir * Mathf.Clamp01(tMin);
            if (Mathf.Abs(hitPoint.x - rect.left) < 0.1f) normal = Vector2.left;
            else if (Mathf.Abs(hitPoint.x - rect.right) < 0.1f) normal = Vector2.right;
            else if (Mathf.Abs(hitPoint.y - rect.bottom) < 0.1f) normal = Vector2.down;
            else normal = Vector2.up;
            return true;
        }

        private void RemoveUnnecessarySegments()
        {
            for (int i = ropeSegmentPoints.Count - 1; i >= 0; i--)
            {
                if (segmentBridgeAttachments[i] != null || frameCounter - segmentLastChanged[i] < COOLDOWN_FRAMES) continue;
                Vector2 prev = (i == 0) ? baseChunk.pos : ropeSegmentPoints[i - 1];
                Vector2 next = (i == ropeSegmentPoints.Count - 1) ? pos : ropeSegmentPoints[i + 1];
                if (!TraceTerrainCollision(prev, next, out _, out _) && !collisionProvider.RayTraceBridgesReturnFirstIntersection(player.room, prev, next, out _, out _, out _))
                {
                    ropeSegmentPoints.RemoveAt(i);
                    segmentLastChanged.RemoveAt(i);
                    segmentBridgeAttachments.RemoveAt(i);
                }
            }
        }

        private void AddNecessarySegments()
        {
            if (ropeSegmentPoints.Count > MAX_SEGMENTS) return;
            for (int i = 0; i <= ropeSegmentPoints.Count; i++)
            {
                Vector2 start = (i == 0) ? baseChunk.pos : ropeSegmentPoints[i - 1];
                Vector2 end = (i == ropeSegmentPoints.Count) ? pos : ropeSegmentPoints[i];
                if (Custom.DistLess(start, end, 1f)) continue;
                if (collisionProvider.RayTraceBridgesReturnFirstIntersection(player.room, start, end, out var bridge, out var hit, out _))
                {
                    var stable = bridge.GetClosestPoint(hit, out int seg, out float t);
                    InsertSegment(i, stable, new SegmentBridgeInfo { bridge = bridge, segIndex = seg, t = t });
                    return;
                }
                if (TraceTerrainCollision(start, end, out Vector2 hitP, out Vector2 normal))
                {
                    Vector2 offsetP = hitP + normal * WALL_OFFSET;
                    if (!Custom.DistLess(offsetP, start, 3f) && !Custom.DistLess(offsetP, end, 3f))
                    {
                        InsertSegment(i, offsetP, null);
                        return;
                    }
                }
            }
        }

        private void InsertSegment(int index, Vector2 point, SegmentBridgeInfo bridgeInfo)
        {
            if (ropeSegmentPoints.Count >= MAX_SEGMENTS) return;
            ropeSegmentPoints.Insert(Mathf.Clamp(index, 0, ropeSegmentPoints.Count), point);
            segmentLastChanged.Insert(Mathf.Clamp(index, 0, segmentLastChanged.Count), frameCounter);
            segmentBridgeAttachments.Insert(Mathf.Clamp(index, 0, segmentBridgeAttachments.Count), bridgeInfo);
        }

        private void SlideNodesOffObstacles()
        {
            for (int i = 0; i < ropeSegmentPoints.Count; i++)
            {
                if (segmentBridgeAttachments[i] != null || frameCounter - segmentLastChanged[i] < COOLDOWN_FRAMES) continue;
                Vector2 point = ropeSegmentPoints[i];
                IntVector2 tilePos = player.room.GetTilePosition(point);
                if (player.room.GetTile(tilePos).Solid)
                {
                    FloatRect rect = player.room.TileRect(tilePos);
                    Vector2 center = new Vector2(Mathf.Clamp(point.x, rect.left, rect.right), Mathf.Clamp(point.y, rect.bottom, rect.top));
                    Vector2 dir = (point - center).normalized;
                    if (dir.magnitude < 0.01f) dir = Vector2.up;
                    if (Mathf.Abs(point.x - center.x) > Mathf.Abs(point.y - center.y))
                        dir = new Vector2(Mathf.Sign(point.x - (rect.left + rect.right) * 0.5f), 0);
                    else
                        dir = new Vector2(0, Mathf.Sign(point.y - (rect.bottom + rect.top) * 0.5f));
                    ropeSegmentPoints[i] += dir * 1f;
                }
            }
        }
        private class SegmentBridgeInfo { public SilkBridge bridge; public int segIndex; public float t; }
    }
}