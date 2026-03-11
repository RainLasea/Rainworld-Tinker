using RWCustom;
using System.Collections.Generic;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    public interface IClimbableSilk
    {
        Vector2 GetPointOnSegment(int segIndex, float t);
        int SegmentCount { get; }
        bool IsActive { get; }
        void ApplyClimbForce(Vector2 worldPos, Vector2 force);
    }


    public class BridgeAnchor
    {
        public enum AnchorType
        {
            Terrain,
            BridgeSegment,
            PhysicalObject
        }

        public AnchorType type;
        public Vector2 terrainPos;


        public SilkBridge attachedBridge;
        public int segmentIndex;
        public float segmentT;


        public PhysicalObject attachedObject;
        public int chunkIndex;
        public Vector2 localOffset;

        public Vector2 GetWorldPosition()
        {
            switch (type)
            {
                case AnchorType.Terrain:
                    return terrainPos;

                case AnchorType.BridgeSegment:
                    if (attachedBridge != null && attachedBridge.IsActive)
                    {
                        return attachedBridge.GetPointOnSegment(segmentIndex, segmentT);
                    }
                    return terrainPos;

                case AnchorType.PhysicalObject:
                    if (attachedObject != null && attachedObject.bodyChunks != null &&
                        chunkIndex >= 0 && chunkIndex < attachedObject.bodyChunks.Length)
                    {
                        BodyChunk chunk = attachedObject.bodyChunks[chunkIndex];
                        return chunk.pos + localOffset;
                    }
                    return terrainPos;

                default:
                    return terrainPos;
            }
        }

        public bool IsValid(Room room)
        {
            switch (type)
            {
                case AnchorType.Terrain:
                    return true;

                case AnchorType.BridgeSegment:
                    return attachedBridge != null &&
                           attachedBridge.IsActive &&
                           attachedBridge.room == room;

                case AnchorType.PhysicalObject:
                    return attachedObject != null &&
                           !attachedObject.slatedForDeletetion &&
                           attachedObject.room == room;

                default:
                    return false;
            }
        }


        public static BridgeAnchor CreateTerrainAnchor(Vector2 pos)
        {
            return new BridgeAnchor
            {
                type = AnchorType.Terrain,
                terrainPos = pos
            };
        }

        public static BridgeAnchor CreateBridgeAnchor(SilkBridge bridge, Vector2 worldPos)
        {
            int segIndex;
            float t;
            Vector2 closestPoint = bridge.GetClosestPoint(worldPos, out segIndex, out t);

            return new BridgeAnchor
            {
                type = AnchorType.BridgeSegment,
                attachedBridge = bridge,
                segmentIndex = segIndex,
                segmentT = t,
                terrainPos = closestPoint
            };
        }

        public static BridgeAnchor CreateObjectAnchor(PhysicalObject obj, Vector2 worldPos)
        {
            if (obj.bodyChunks == null || obj.bodyChunks.Length == 0)
                return CreateTerrainAnchor(worldPos);


            int closestIndex = 0;
            float closestDist = float.MaxValue;

            for (int i = 0; i < obj.bodyChunks.Length; i++)
            {
                float dist = Vector2.Distance(worldPos, obj.bodyChunks[i].pos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }
            }

            Vector2 offset = worldPos - obj.bodyChunks[closestIndex].pos;

            return new BridgeAnchor
            {
                type = AnchorType.PhysicalObject,
                attachedObject = obj,
                chunkIndex = closestIndex,
                localOffset = offset,
                terrainPos = worldPos
            };
        }
    }

    public class AttachedObjectInfo
    {
        public PhysicalObject obj;
        public int attachedNodeIndex;
        public Vector2 localOffset;
        public float attachTime;
        public bool isPlayerDetachable = true;
    }

    public class SilkBridge : IClimbableSilk
    {

        public BridgeAnchor startAnchor;
        public BridgeAnchor endAnchor;
        public Room room;
        public bool slatedForDeletetion;
        public float health;

        private float maxBridgeLength;
        private int nodeCount;
        private List<BridgeNode> physicsNodes;
        public Vector2[] RenderPoints { get; private set; }

        private List<AttachedObjectInfo> attachedObjects = new List<AttachedObjectInfo>();
        private const float OBJECT_ATTACH_DISTANCE = 15f;
        private const float OBJECT_DETACH_DISTANCE = 20f;
        private const float OBJECT_ATTACH_FORCE = 0.3f;
        private const float OBJECT_MASS_INFLUENCE = 0.05f;

        private const float NODE_MASS = 0.1f;
        private const float GRAVITY = 0.1f;
        private const float DAMPING = 0.975f;
        private const float INITIAL_HEALTH = 120f;


        public SilkBridge(BridgeAnchor start, BridgeAnchor end, Room room, float maxLength, int nodeCount = 15)
        {
            this.startAnchor = start;
            this.endAnchor = end;
            this.room = room;
            this.maxBridgeLength = maxLength;
            this.nodeCount = nodeCount;
            this.slatedForDeletetion = false;
            this.health = INITIAL_HEALTH;

            Vector2 sPos = startAnchor.GetWorldPosition();
            Vector2 ePos = endAnchor.GetWorldPosition();
            
            InitializePhysicsNodes();
            
            RenderPoints = new Vector2[nodeCount];
        }


        public SilkBridge(Vector2 start, Vector2 end, Room room, float maxLength, int nodeCount = 15)
            : this(BridgeAnchor.CreateTerrainAnchor(start),
                   BridgeAnchor.CreateTerrainAnchor(end),
                   room, maxLength, nodeCount)
        {
        }


        public Vector2 startPoint => startAnchor.GetWorldPosition();
        public Vector2 endPoint => endAnchor.GetWorldPosition();

        private void InitializePhysicsNodes()
        {
            physicsNodes = new List<BridgeNode>();
            Vector2 start = startAnchor.GetWorldPosition();
            Vector2 end = endAnchor.GetWorldPosition();

            for (int i = 0; i < nodeCount; i++)
            {
                float t = (nodeCount <= 1) ? 0.5f : (float)i / (nodeCount - 1);
                Vector2 initialPos = Vector2.Lerp(start, end, t);

                BridgeNode newNode = new BridgeNode(initialPos);

                if (i > 0 && i < nodeCount - 1 && Vector2.Distance(start, end) > 10f)
                {
                    newNode.pos += Custom.RNV() * 5f;
                }
                physicsNodes.Add(newNode);
            }
        }

        public void Update()
        {
            if (room == null || slatedForDeletetion) return;

            if (!startAnchor.IsValid(room) || !endAnchor.IsValid(room) || health <= 0f)
            {
                if (health <= 0f && !slatedForDeletetion)
                {
                    slatedForDeletetion = true;
                    Vector2 breakPoint = GetPointOnSegment(SegmentCount / 2, 0.5f);
                    ReleaseAllAttachedObjects();
                    BrokenSilkManager.TriggerBreakAnimation(GetRenderPath(), room, breakPoint);
                }
                else
                {
                    slatedForDeletetion = true;
                    ReleaseAllAttachedObjects();
                }
                return;
            }

            physicsNodes[0].pos = startAnchor.GetWorldPosition();
            physicsNodes[0].lastPos = physicsNodes[0].pos;
            physicsNodes[0].isFixed = true;

            physicsNodes[physicsNodes.Count - 1].pos = endAnchor.GetWorldPosition();
            physicsNodes[physicsNodes.Count - 1].lastPos = physicsNodes[physicsNodes.Count - 1].pos;
            physicsNodes[physicsNodes.Count - 1].isFixed = true;

            float gravity = 0.6f;
            float friction = 0.95f;
            for (int i = 0; i < physicsNodes.Count; i++)
            {
                if (physicsNodes[i].isFixed) continue;

                Vector2 vel = (physicsNodes[i].pos - physicsNodes[i].lastPos) * friction;
                physicsNodes[i].lastPos = physicsNodes[i].pos;
                physicsNodes[i].pos += vel;
                physicsNodes[i].pos.y -= gravity;
            }
            CheckAndAttachNearbyObjects();

            int iterations = 10;
            float segmentLength = (Vector2.Distance(physicsNodes[0].pos, physicsNodes[physicsNodes.Count - 1].pos) / (physicsNodes.Count - 1)) * 0.85f;

            for (int n = 0; n < iterations; n++)
            {
                for (int i = 0; i < physicsNodes.Count - 1; i++)
                {
                    var a = physicsNodes[i];
                    var b = physicsNodes[i + 1];
                    float d = Vector2.Distance(a.pos, b.pos);
                    if (d < 0.1f) continue;

                    float difference = (segmentLength - d) / d;
                    Vector2 offset = (a.pos - b.pos) * difference * 0.5f;

                    if (!a.isFixed) a.pos += offset;
                    if (!b.isFixed) b.pos -= offset;
                }

                ApplyTerrainCollision();
                UpdateAttachedObjects();
            }
            UpdateRenderPoints();
        }

        private void ApplyDistanceConstraints()
        {
            if (physicsNodes.Count == 0) return;


            Vector2 start = startPoint;
            Vector2 end = endPoint;

            int totalSegments = nodeCount + 1;
            float targetTotalLength = Vector2.Distance(start, end) * 1.02f;
            float segmentLength = targetTotalLength / totalSegments;

            List<Vector2> points = new List<Vector2> { start };
            foreach (var node in physicsNodes) points.Add(node.pos);
            points.Add(end);


            for (int iter = 0; iter < 5; iter++)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector2 delta = points[i + 1] - points[i];
                    float dist = delta.magnitude;
                    if (dist > 0.001f)
                    {
                        float diff = dist - segmentLength;
                        Vector2 correction = delta.normalized * diff * 0.5f;


                        if (i != 0) points[i] += correction;
                        if (i + 1 != points.Count - 1) points[i + 1] -= correction;
                    }
                }
            }


            for (int i = 0; i < physicsNodes.Count; i++)
                physicsNodes[i].pos = points[i + 1];
        }

        private void ApplyTerrainCollision()
        {
            if (room == null) return;

            foreach (BridgeNode node in physicsNodes)
            {
                IntVector2 tilePos = room.GetTilePosition(node.pos);

                if (room.GetTile(tilePos).Solid)
                {
                    Vector2 tileCenter = room.MiddleOfTile(tilePos);
                    Vector2 pushDir = (node.pos - tileCenter).normalized;
                    node.pos = tileCenter + pushDir * 10f;
                    node.vel = Vector2.Reflect(node.vel, pushDir) * 0.3f;
                }
            }
        }

        public void CheckAndAttachNearbyObjects()
        {
            if (room == null || !IsActive) return;

            for (int i = 0; i < room.physicalObjects.Length; i++)
            {
                foreach (PhysicalObject obj in room.physicalObjects[i])
                {
                    if (obj is Weapon || obj.slatedForDeletetion || obj is Player) continue;

                    if (IsObjectAttached(obj)) continue;

                    foreach (BodyChunk chunk in obj.bodyChunks)
                    {
                        int closestNode = -1;
                        float closestDist = float.MaxValue;

                        for (int nodeIdx = 0; nodeIdx < physicsNodes.Count; nodeIdx++)
                        {
                            float dist = Vector2.Distance(chunk.pos, physicsNodes[nodeIdx].pos);
                            if (dist < closestDist && dist < OBJECT_ATTACH_DISTANCE)
                            {
                                closestDist = dist;
                                closestNode = nodeIdx;
                            }
                        }

                        if (closestNode >= 0 && chunk.vel.magnitude < 5f)
                        {
                            AttachedObjectInfo info = new AttachedObjectInfo
                            {
                                obj = obj,
                                attachedNodeIndex = closestNode,
                                localOffset = chunk.pos - physicsNodes[closestNode].pos,
                                attachTime = Time.time
                            };

                            attachedObjects.Add(info);

                            chunk.vel *= 0.3f;
                            break;
                        }
                    }
                }
            }
        }

        public void UpdateAttachedObjects()
        {
            for (int i = attachedObjects.Count - 1; i >= 0; i--)
            {
                AttachedObjectInfo info = attachedObjects[i];

                if (info.obj == null || info.obj.slatedForDeletetion)
                {
                    attachedObjects.RemoveAt(i);
                    continue;
                }

                if (info.obj.room != room)
                {
                    attachedObjects.RemoveAt(i);
                    continue;
                }

                if (info.attachedNodeIndex >= 0 && info.attachedNodeIndex < physicsNodes.Count)
                {
                    BridgeNode node = physicsNodes[info.attachedNodeIndex];
                    BodyChunk primaryChunk = info.obj.bodyChunks[0];

                    Vector2 targetPos = node.pos + info.localOffset;

                    primaryChunk.pos = Vector2.Lerp(primaryChunk.pos, targetPos, OBJECT_ATTACH_FORCE);
                    primaryChunk.vel *= 0.7f;

                    if (info.obj.bodyChunks.Length > 1)
                    {
                        Vector2 offset = targetPos - primaryChunk.lastPos;
                        for (int j = 1; j < info.obj.bodyChunks.Length; j++)
                        {
                            info.obj.bodyChunks[j].pos += offset * 0.5f;
                        }
                    }

                    if (!node.isFixed)
                    {
                        float weightFactor = Mathf.Clamp(info.obj.TotalMass * OBJECT_MASS_INFLUENCE, 0.1f, 1f);
                        Vector2 pullForce = Vector2.down * weightFactor;
                        node.vel += pullForce;
                    }
                }
            }
        }

        public bool TryDetachObject(Player player, out PhysicalObject detachedObject)
        {
            detachedObject = null;

            foreach (AttachedObjectInfo info in attachedObjects)
            {
                if (info.obj == null || !info.isPlayerDetachable) continue;

                float distToPlayer = Vector2.Distance(player.bodyChunks[0].pos, info.obj.bodyChunks[0].pos);
                if (distToPlayer < OBJECT_DETACH_DISTANCE)
                {
                    detachedObject = info.obj;
                    attachedObjects.Remove(info);

                    Vector2 pushDir = (info.obj.bodyChunks[0].pos - player.bodyChunks[0].pos).normalized;
                    info.obj.bodyChunks[0].vel += pushDir * 3f;

                    return true;
                }
            }

            return false;
        }

        public bool IsObjectAttached(PhysicalObject obj)
        {
            foreach (AttachedObjectInfo info in attachedObjects)
            {
                if (info.obj == obj) return true;
            }
            return false;
        }

        private void ReleaseAllAttachedObjects()
        {
            foreach (AttachedObjectInfo info in attachedObjects)
            {
                if (info.obj != null && info.obj.bodyChunks != null)
                {
                    Vector2 randomDir = Custom.RNV() * Random.value * 2f;
                    info.obj.bodyChunks[0].vel += randomDir + Vector2.down * 1f;
                }
            }
            attachedObjects.Clear();
        }

        private void UpdateRenderPoints()
        {
            if (physicsNodes == null || RenderPoints == null) return;

            for (int i = 0; i < physicsNodes.Count; i++)
            {
                RenderPoints[i] = physicsNodes[i].pos;
            }
        }

        public List<Vector2> GetRenderPath() => new List<Vector2>(RenderPoints);

        public Vector2 GetClosestPoint(Vector2 worldPos, out int segIndex, out float t)
        {
            segIndex = 0;
            t = 0f;

            if (RenderPoints == null || RenderPoints.Length < 2)
                return startPoint;

            float bestDist = float.MaxValue;
            Vector2 bestPoint = startPoint;

            for (int i = 0; i < RenderPoints.Length - 1; i++)
            {
                Vector2 a = RenderPoints[i];
                Vector2 b = RenderPoints[i + 1];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float localT = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(worldPos - a, ab) / len2) : 0f;

                Vector2 proj = a + ab * localT;
                float d = Vector2.SqrMagnitude(worldPos - proj);

                if (d < bestDist)
                {
                    bestDist = d;
                    bestPoint = proj;
                    segIndex = i;
                    t = localT;
                }
            }

            return bestPoint;
        }

        public void ApplyForceAt(Vector2 worldPos, Vector2 force, float radius)
        {
            if (physicsNodes == null) return;

            for (int i = 0; i < physicsNodes.Count; i++)
            {
                float dist = Vector2.Distance(worldPos, physicsNodes[i].pos);
                if (dist < radius)
                {
                    physicsNodes[i].pos += force * 0.2f;
                }
            }
        }

        private void TryShakeOffObjects(Vector2 impactPoint, Vector2 force)
        {
            for (int i = attachedObjects.Count - 1; i >= 0; i--)
            {
                AttachedObjectInfo info = attachedObjects[i];
                if (info.obj == null) continue;

                float dist = Vector2.Distance(impactPoint, info.obj.bodyChunks[0].pos);
                float shakeChance = Mathf.InverseLerp(50f, 10f, dist) * (force.magnitude / 15f);

                if (Random.value < shakeChance)
                {
                    Vector2 randomDir = Custom.RNV() * Random.value * 2f;
                    info.obj.bodyChunks[0].vel += randomDir + Vector2.down * 1f;

                    attachedObjects.RemoveAt(i);
                }
            }
        }

        public void TakeDamage(float amount, Vector2 impactPoint)
        {
            if (slatedForDeletetion) return;
            health -= amount;
            if (health <= 0)
            {
                slatedForDeletetion = true;
                ReleaseAllAttachedObjects();
                BrokenSilkManager.TriggerBreakAnimation(GetRenderPath(), room, impactPoint);
                room?.PlaySound(SoundID.Spear_Stick_In_Wall, startPoint, 0.8f, 1.2f);
                room?.PlaySound(SoundID.Spear_Stick_In_Wall, endPoint, 0.8f, 1.2f);
            }
        }

        public Vector2 GetPointOnSegment(int segIndex, float t)
        {
            var path = GetRenderPath();
            if (path.Count < 2) return startPoint;

            segIndex = Mathf.Clamp(segIndex, 0, path.Count - 2);
            t = Mathf.Clamp01(t);
            return Vector2.Lerp(path[segIndex], path[segIndex + 1], t);
        }

        public bool IsPlayerNearBridge(Player player, float threshold = 20f)
        {
            if (player?.bodyChunks == null) return false;

            Vector2 playerPos = player.bodyChunks[0].pos;

            for (int i = 0; i < RenderPoints.Length - 1; i++)
            {
                float dist = DistanceToSegment(playerPos, RenderPoints[i], RenderPoints[i + 1]);
                if (dist < threshold) return true;
            }

            return false;
        }

        private float DistanceToSegment(Vector2 point, Vector2 segStart, Vector2 segEnd)
        {
            Vector2 line = segEnd - segStart;
            float len = line.magnitude;
            if (len < 0.01f) return Vector2.Distance(point, segStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - segStart, line) / (len * len));
            Vector2 projection = segStart + t * line;
            return Vector2.Distance(point, projection);
        }


        public int SegmentCount => RenderPoints != null ? RenderPoints.Length - 1 : 0;
        public bool IsActive => room != null && !slatedForDeletetion && physicsNodes != null && physicsNodes.Count > 0 &&
                                startAnchor.IsValid(room) && endAnchor.IsValid(room);

        Vector2 IClimbableSilk.GetPointOnSegment(int segIndex, float t)
        {
            return GetPointOnSegment(segIndex, t);
        }

        public void ApplyClimbForce(Vector2 worldPos, Vector2 force)
        {
            ApplyForceAt(worldPos, force, 24f);
        }

        private class BridgeNode
        {
            public Vector2 pos;
            public Vector2 lastPos;
            public Vector2 vel;
            public bool isFixed;

            public BridgeNode(Vector2 position)
            {
                this.pos = position;
                this.lastPos = position;
                this.isFixed = false;
            }

            public void Update(float gravity, float friction)
            {
                if (isFixed) return;

                Vector2 vel = (pos - lastPos) * friction;
                lastPos = pos;
                pos += vel;
                pos.y -= gravity;
            }
        }
    }
}