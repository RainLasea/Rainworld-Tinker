using RWCustom;
using System.Collections.Generic;
using UnityEngine;

namespace Weaver.Silk.Bridge
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

    public class SilkBridge : IClimbableSilk
    {

        public BridgeAnchor startAnchor;
        public BridgeAnchor endAnchor;
        public Room room;

        private float maxBridgeLength;
        private int nodeCount;
        private List<BridgeNode> physicsNodes;
        private Vector2[] renderPoints;

        private const float NODE_MASS = 0.1f;
        private const float GRAVITY = 0.1f;
        private const float DAMPING = 0.975f;


        public SilkBridge(BridgeAnchor start, BridgeAnchor end, Room room, float maxLength, int nodeCount = 15)
        {
            startAnchor = start;
            endAnchor = end;
            this.room = room;
            maxBridgeLength = maxLength;
            this.nodeCount = nodeCount;

            InitializePhysicsNodes();
            renderPoints = new Vector2[nodeCount + 2];
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
                float t = (i + 1f) / (nodeCount + 1f);
                Vector2 pos = Vector2.Lerp(start, end, t);
                physicsNodes.Add(new BridgeNode(pos));
            }
        }

        public void Update()
        {
            if (room == null) return;


            if (!startAnchor.IsValid(room) || !endAnchor.IsValid(room))
            {

                return;
            }


            foreach (var node in physicsNodes)
            {
                node.lastPos = node.pos;
                node.vel.y -= GRAVITY;
                node.pos += node.vel;
                node.vel *= DAMPING;
            }


            for (int iteration = 0; iteration < 8; iteration++)
            {
                ApplyDistanceConstraints();
                ApplyTerrainCollision();
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

        private void UpdateRenderPoints()
        {
            renderPoints[0] = startPoint;

            for (int i = 0; i < physicsNodes.Count; i++)
                renderPoints[i + 1] = physicsNodes[i].pos;

            renderPoints[nodeCount + 1] = endPoint;
        }

        public List<Vector2> GetRenderPath() => new List<Vector2>(renderPoints);

        public Vector2 GetClosestPoint(Vector2 worldPos, out int segIndex, out float t)
        {
            segIndex = 0;
            t = 0f;

            if (renderPoints == null || renderPoints.Length < 2)
                return startPoint;

            float bestDist = float.MaxValue;
            Vector2 bestPoint = startPoint;

            for (int i = 0; i < renderPoints.Length - 1; i++)
            {
                Vector2 a = renderPoints[i];
                Vector2 b = renderPoints[i + 1];
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

        public void ApplyForceAt(Vector2 worldPos, Vector2 force, float radius = 24f)
        {
            if (physicsNodes == null || physicsNodes.Count == 0) return;

            int segIndex;
            float t;
            Vector2 hitPoint = GetClosestPoint(worldPos, out segIndex, out t);

            int leftNode = Mathf.Clamp(segIndex - 1, 0, physicsNodes.Count - 1);
            int rightNode = Mathf.Clamp(segIndex, 0, physicsNodes.Count - 1);

            if (segIndex == 0) leftNode = 0;
            if (segIndex >= physicsNodes.Count) rightNode = physicsNodes.Count - 1;

            float leftDist = Vector2.Distance(hitPoint, physicsNodes[leftNode].pos);
            float rightDist = Vector2.Distance(hitPoint, physicsNodes[rightNode].pos);
            float total = leftDist + rightDist;
            float leftWeight = total > 1e-6f ? 1f - leftDist / total : 0.5f;
            float rightWeight = total > 1e-6f ? 1f - rightDist / total : 0.5f;

            float forceScale = 0.03f;

            if (leftNode >= 0 && leftNode < physicsNodes.Count)
                physicsNodes[leftNode].vel += force * (leftWeight * forceScale / NODE_MASS);

            if (rightNode >= 0 && rightNode < physicsNodes.Count)
                physicsNodes[rightNode].vel += force * (rightWeight * forceScale / NODE_MASS);

            int spread = 2;
            for (int s = 1; s <= spread; s++)
            {
                float atten = 1f / (1f + s * 2f);
                int ln = leftNode - s;
                int rn = rightNode + s;

                if (ln >= 0) physicsNodes[ln].vel += force * (0.25f * atten * forceScale / NODE_MASS);
                if (rn < physicsNodes.Count) physicsNodes[rn].vel += force * (0.25f * atten * forceScale / NODE_MASS);
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

            for (int i = 0; i < renderPoints.Length - 1; i++)
            {
                float dist = DistanceToSegment(playerPos, renderPoints[i], renderPoints[i + 1]);
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


        public int SegmentCount => renderPoints != null ? renderPoints.Length - 1 : 0;
        public bool IsActive => room != null && physicsNodes != null && physicsNodes.Count > 0 &&
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

            public BridgeNode(Vector2 position)
            {
                pos = position;
                lastPos = position;
                vel = Vector2.zero;
            }
        }
    }
}