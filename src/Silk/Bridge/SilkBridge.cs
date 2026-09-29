using RWCustom;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using tinker;
using tinker.Silk;
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
                        return attachedBridge.GetBasePoint(segmentIndex, segmentT);
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
        public int attachedChunkIndex;
        public Vector2 localOffset;
        public float attachTime;
        public bool isPlayerDetachable = true;
        internal float stepDistance;
    }

    public partial class SilkBridge : IClimbableSilk
    {

        public BridgeAnchor startAnchor;
        public BridgeAnchor endAnchor;
        public Room room;
        public bool slatedForDeletetion;
        public float health;

        private float maxBridgeLength;
        private int nodeCount;
        private SilkDynamics.Rope rope;
        private SilkDynamics.Node[] physicsNodes => rope.Nodes;
        private Vector2[] previousRenderPoints;
        private readonly SilkDynamics.LaunchTightening launchTightening = new SilkDynamics.LaunchTightening();
        private SilkWaves.Strand waves;
        private Vector2[] previousExternalForces, externalForces;
        private static readonly ConditionalWeakTable<List<SilkBridge>, WaveRoom> waveRooms =
            new ConditionalWeakTable<List<SilkBridge>, WaveRoom>();

        private sealed class WaveRoom
        {
            private readonly List<SilkBridge> members = new List<SilkBridge>();
            private SilkWaves network;

            internal void Update(List<SilkBridge> bridges)
            {
                bool rebuild = network == null || members.Count != bridges.Count;
                for (int i = 0; !rebuild && i < bridges.Count; i++)
                    rebuild = members[i] != bridges[i] || bridges[i].waves.Coordinates.Length != bridges[i].rope.Coordinates.Length;
                if (rebuild)
                {
                    members.Clear(); members.AddRange(bridges);
                    network = new SilkWaves();
                    foreach (var bridge in bridges)
                        bridge.waves = network.AddStrand(bridge.rope.Coordinates, bridge.rope.SegmentLength,
                            bridge.startAnchor.type != BridgeAnchor.AnchorType.BridgeSegment,
                            bridge.endAnchor.type != BridgeAnchor.AnchorType.BridgeSegment, bridge.waves);
                    foreach (var bridge in bridges)
                    {
                        Join(bridge, bridge.startAnchor, 0);
                        Join(bridge, bridge.endAnchor, bridge.waves.Nodes.Length - 1);
                    }
                    network.Seal();
                }
                network.Update();
            }

            private void Join(SilkBridge bridge, BridgeAnchor anchor, int endpoint)
            {
                if (anchor.type != BridgeAnchor.AnchorType.BridgeSegment || anchor.attachedBridge == null) return;
                float coordinate = Mathf.Round((anchor.segmentIndex + Mathf.Clamp01(anchor.segmentT)) * 4096f) / 4096f;
                int index = System.Array.BinarySearch(anchor.attachedBridge.waves.Coordinates, coordinate);
                if (index >= 0) network.Join(bridge.waves, endpoint, anchor.attachedBridge.waves, index);
            }
        }
        public Vector2[] RenderPoints { get; private set; }

        private List<AttachedObjectInfo> attachedObjects = new List<AttachedObjectInfo>();
        private const float OBJECT_ATTACH_DISTANCE = 15f;
        private const float OBJECT_DETACH_DISTANCE = 20f;
        private const float OBJECT_ATTACH_FORCE = 0.3f;
        private const float INITIAL_HEALTH = 120f;


        public SilkBridge(BridgeAnchor start, BridgeAnchor end, Room room, float maxLength, int nodeCount = 15)
        {
            this.startAnchor = start;
            this.endAnchor = end;
            this.room = room;
            this.maxBridgeLength = maxLength;
            this.nodeCount = Mathf.Max(2, nodeCount);
            this.slatedForDeletetion = false;
            this.health = INITIAL_HEALTH;

            InitializePhysicsNodes();
            RegisterAnchor(startAnchor, physicsNodes[0]);
            RegisterAnchor(endAnchor, physicsNodes[physicsNodes.Length - 1]);
            waves = new SilkWaves.Strand(rope.Coordinates, rope.SegmentLength,
                startAnchor.type != BridgeAnchor.AnchorType.BridgeSegment,
                endAnchor.type != BridgeAnchor.AnchorType.BridgeSegment, null);
            previousExternalForces = new Vector2[this.nodeCount];
            externalForces = new Vector2[this.nodeCount];
            
            RenderPoints = new Vector2[this.nodeCount];
            previousRenderPoints = new Vector2[this.nodeCount];
            UpdateRenderPoints();
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
            rope = new SilkDynamics.Rope(startPoint, endPoint, nodeCount,
                startAnchor.type != BridgeAnchor.AnchorType.BridgeSegment,
                endAnchor.type != BridgeAnchor.AnchorType.BridgeSegment);
        }

        // Creation only initializes geometry. Room updates solve every connected
        // strand together; solving one complete strand at a time delays reactions.
        public void Update()
        {
            UpdateRenderPoints();
        }

        internal static void UpdateNetwork(List<SilkBridge> bridges)
        {
            // Remove invalid parents and their dependants before integrating.
            bool removed;
            do
            {
                removed = false;
                for (int i = bridges.Count - 1; i >= 0; i--)
                {
                    var bridge = bridges[i];
                    if (bridge.IsActive && bridge.health > 0f) continue;
                    if (!bridge.slatedForDeletetion)
                    {
                        if (bridge.health <= 0f)
                            bridge.Break(bridge.GetPointOnSegment(bridge.SegmentCount / 2, 0.5f));
                        else
                            BrokenSilkManager.TriggerFadeAnimation(bridge.GetRenderPath(), bridge.room,
                                color: SilkBridgeGraphics.MainSilkColor,
                                width: Mathf.Lerp(1f, 0.65f, Mathf.Clamp01(Vector2.Distance(bridge.startPoint, bridge.endPoint) / 600f)));
                    }
                    bridge.slatedForDeletetion = true;
                    bridge.startAnchor.attachedBridge?.rope.RemoveJunction(bridge.physicsNodes[0]);
                    bridge.endAnchor.attachedBridge?.rope.RemoveJunction(bridge.physicsNodes[bridge.physicsNodes.Length - 1]);
                    bridge.ReleaseAllAttachedObjects();
                    bridges.RemoveAt(i);
                    removed = true;
                }
            } while (removed);

            foreach (var bridge in bridges)
            {
                bridge.rope.BeginFrame();
                bridge.CheckAndAttachNearbyObjects();
                bridge.ApplyObjectWeight();
                // Pulls excite a wave only when the applied load changes. A
                // sustained pull or weight must not become an endless oscillator.
                for (int i = 0; i < bridge.nodeCount; i++)
                {
                    Vector2 change = bridge.externalForces[i] - bridge.previousExternalForces[i];
                    if (change.sqrMagnitude > 0.01f) bridge.ExciteAt(i, change * 0.55f);
                    bridge.previousExternalForces[i] = bridge.externalForces[i];
                    bridge.externalForces[i] = Vector2.zero;
                }
            }

            float dt = 1f / SilkDynamics.Substeps;
            for (int step = 0; step < SilkDynamics.Substeps; step++)
            {
                foreach (var bridge in bridges)
                {
                    bridge.PinExternalAnchors();
                    bridge.rope.Predict(dt, bridge.room.gravity);
                }
                for (int iteration = 0; iteration < SilkDynamics.Iterations; iteration++)
                {
                    bool reverse = (iteration & 1) != 0;
                    for (int n = 0; n < bridges.Count; n++)
                    {
                        var bridge = bridges[reverse ? bridges.Count - 1 - n : n];
                        bridge.rope.Solve(dt, reverse);
                    }
                    for (int n = 0; n < bridges.Count; n++)
                    {
                        var bridge = bridges[reverse ? bridges.Count - 1 - n : n];
                        bridge.SolveAnchor(bridge.startAnchor, bridge.physicsNodes[0]);
                        bridge.SolveAnchor(bridge.endAnchor, bridge.physicsNodes[bridge.physicsNodes.Length - 1]);
                    }
                    foreach (var bridge in bridges) bridge.ApplyTerrainCollision();
                }
            }
            waveRooms.GetValue(bridges, _ => new WaveRoom()).Update(bridges);
            foreach (var bridge in bridges)
            {
                bridge.UpdateAttachedObjects();
                bridge.launchTightening.Update();
                bridge.UpdateRenderPoints();
                bridge.rope.EndFrame();
            }
        }

        private void PinExternalAnchors()
        {
            if (startAnchor.type != BridgeAnchor.AnchorType.BridgeSegment)
                physicsNodes[0].Pin(startAnchor.GetWorldPosition());
            if (endAnchor.type != BridgeAnchor.AnchorType.BridgeSegment)
                physicsNodes[physicsNodes.Length - 1].Pin(endAnchor.GetWorldPosition());
        }

        private static void RegisterAnchor(BridgeAnchor anchor, SilkDynamics.Node endpoint)
        {
            if (anchor.type == BridgeAnchor.AnchorType.BridgeSegment && anchor.attachedBridge != null)
                anchor.attachedBridge.rope.RegisterJunction(endpoint, anchor.segmentIndex, anchor.segmentT);
        }

        internal void BeginLaunchTightening(SilkDynamics.LaunchSpring spring)
        {
            launchTightening.Begin(spring);
        }

        private void SolveAnchor(BridgeAnchor anchor, SilkDynamics.Node endpoint)
        {
            if (anchor.type == BridgeAnchor.AnchorType.BridgeSegment && anchor.attachedBridge != null)
                SilkDynamics.Join(endpoint, anchor.attachedBridge.rope, anchor.segmentIndex, anchor.segmentT);
        }

        private void ApplyObjectWeight()
        {
            foreach (var info in attachedObjects)
            {
                if (info.obj == null || info.obj.slatedForDeletetion || info.obj.room != room ||
                    info.attachedNodeIndex < 0 || info.attachedNodeIndex >= physicsNodes.Length) continue;
                physicsNodes[info.attachedNodeIndex].Force += Vector2.down * (info.obj.TotalMass * info.obj.gravity);
                BodyChunk chunk = info.obj.bodyChunks[Mathf.Clamp(info.attachedChunkIndex, 0, info.obj.bodyChunks.Length - 1)];
                Vector2 relative = chunk.pos - chunk.lastPos - GetVelocityOnSegment(Mathf.Min(info.attachedNodeIndex, SegmentCount - 1),
                    info.attachedNodeIndex == SegmentCount ? 1f : 0f);
                Vector2 tangent = (rope.Sample(info.attachedNodeIndex + 0.1f, 1f) -
                    rope.Sample(info.attachedNodeIndex - 0.1f, 1f)).normalized;
                float movement = Mathf.Abs(Vector2.Dot(relative, tangent));
                if (movement > 0.6f)
                {
                    info.stepDistance += Mathf.Min(movement, 5f);
                    if (info.stepDistance >= 8f)
                    {
                        info.stepDistance %= 8f;
                        ExciteAt(info.attachedNodeIndex, (Vector2.down + relative * 0.15f) *
                            Mathf.Clamp(info.obj.TotalMass, 0.25f, 2f), gentle: true);
                    }
                }
            }
        }

        private void ApplyTerrainCollision()
        {
            foreach (var node in rope.Particles)
            {
                if (node.InverseMass == 0f) continue;
                Vector2 from = node.Previous;
                Vector2 to = node.Position;
                // Trace movement too: an endpoint-only test misses a thin wall
                // crossed by a fast-moving node or by a constraint correction.
                IntVector2? hit = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, from, to);
                if (!hit.HasValue && !room.GetTile(to).Solid) continue;
                IntVector2 tile = hit ?? room.GetTilePosition(to);
                FloatRect rect = room.TileRect(tile);
                Vector2 normal, surface;
                if (from.x < rect.left)
                {
                    normal = Vector2.left;
                    float t = Mathf.Clamp01((rect.left - from.x) / Mathf.Max(0.0001f, to.x - from.x));
                    surface = Vector2.Lerp(from, to, t); surface.x = rect.left - 0.5f;
                }
                else if (from.x > rect.right)
                {
                    normal = Vector2.right;
                    float t = Mathf.Clamp01((from.x - rect.right) / Mathf.Max(0.0001f, from.x - to.x));
                    surface = Vector2.Lerp(from, to, t); surface.x = rect.right + 0.5f;
                }
                else if (from.y < rect.bottom)
                {
                    normal = Vector2.down;
                    float t = Mathf.Clamp01((rect.bottom - from.y) / Mathf.Max(0.0001f, to.y - from.y));
                    surface = Vector2.Lerp(from, to, t); surface.y = rect.bottom - 0.5f;
                }
                else if (from.y > rect.top)
                {
                    normal = Vector2.up;
                    float t = Mathf.Clamp01((from.y - rect.top) / Mathf.Max(0.0001f, from.y - to.y));
                    surface = Vector2.Lerp(from, to, t); surface.y = rect.top + 0.5f;
                }
                else
                {
                    // Spawned/dragged inside terrain: project to the nearest face,
                    // not a circle of radius 10 that still lies inside a square tile.
                    surface = to; normal = Vector2.left;
                    float nearest = Mathf.Abs(to.x - rect.left);
                    surface.x = rect.left - 0.5f;
                    if (Mathf.Abs(to.x - rect.right) < nearest)
                    { nearest = Mathf.Abs(to.x - rect.right); normal = Vector2.right; surface = new Vector2(rect.right + 0.5f, to.y); }
                    if (Mathf.Abs(to.y - rect.bottom) < nearest)
                    { nearest = Mathf.Abs(to.y - rect.bottom); normal = Vector2.down; surface = new Vector2(to.x, rect.bottom - 0.5f); }
                    if (Mathf.Abs(to.y - rect.top) < nearest)
                    { normal = Vector2.up; surface = new Vector2(to.x, rect.top + 0.5f); }
                }
                SilkDynamics.ResolveContact(node, surface, normal);
            }
        }

        public void CheckAndAttachNearbyObjects()
        {
            if (room == null || !IsActive) return;

            for (int i = 0; i < room.physicalObjects.Length; i++)
            {
                foreach (PhysicalObject obj in room.physicalObjects[i])
                {
                    if (obj is Weapon || obj.slatedForDeletetion || obj is Player || obj.grabbedBy.Count > 0 ||
                        !tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(obj)) continue;

                    if (IsObjectAttached(obj)) continue;

                    foreach (BodyChunk chunk in obj.bodyChunks)
                    {
                        int closestNode = -1;
                        float closestDist = float.MaxValue;

                        for (int nodeIdx = 0; nodeIdx < physicsNodes.Length; nodeIdx++)
                        {
                            float dist = Vector2.Distance(chunk.pos, GetPointOnSegment(Mathf.Min(nodeIdx, SegmentCount - 1), nodeIdx == SegmentCount ? 1f : 0f));
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
                                attachedChunkIndex = System.Array.IndexOf(obj.bodyChunks, chunk),
                                localOffset = chunk.pos - GetPointOnSegment(Mathf.Min(closestNode, SegmentCount - 1), closestNode == SegmentCount ? 1f : 0f),
                                attachTime = room.game == null ? 0f : room.game.clock / 40f
                            };

                            attachedObjects.Add(info);

                            ExciteAt(closestNode, chunk.vel * chunk.mass);

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

                if (info.obj == null || info.obj.slatedForDeletetion || info.obj.grabbedBy.Count > 0 ||
                    !tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(info.obj))
                {
                    attachedObjects.RemoveAt(i);
                    continue;
                }

                if (info.obj.room != room)
                {
                    attachedObjects.RemoveAt(i);
                    continue;
                }

                if (info.attachedNodeIndex >= 0 && info.attachedNodeIndex < physicsNodes.Length)
                {
                    SilkDynamics.Node node = physicsNodes[info.attachedNodeIndex];
                    int chunkIndex = Mathf.Clamp(info.attachedChunkIndex, 0, info.obj.bodyChunks.Length - 1);
                    BodyChunk primaryChunk = info.obj.bodyChunks[chunkIndex];

                    int supportSegment = Mathf.Min(info.attachedNodeIndex, SegmentCount - 1);
                    float supportT = info.attachedNodeIndex == SegmentCount ? 1f : 0f;
                    Vector2 targetPos = GetPointOnSegment(supportSegment, supportT) + info.localOffset;
                    Vector2 correction = (targetPos - primaryChunk.pos) * OBJECT_ATTACH_FORCE;

                    primaryChunk.pos += correction;
                    Vector2 supportVelocity = GetVelocityOnSegment(supportSegment, supportT);
                    if (networkCurve != null)
                        tinker.Silk.RainMeadow.RainMeadowBridge.RelayForce(this, targetPos,
                            Vector2.down * (info.obj.TotalMass * info.obj.gravity));
                    primaryChunk.vel = Vector2.Lerp(primaryChunk.vel, supportVelocity, 0.18f);

                    if (info.obj.bodyChunks.Length > 1)
                    {
                        Vector2 offset = correction;
                        for (int j = 0; j < info.obj.bodyChunks.Length; j++)
                        {
                            if (j == chunkIndex) continue;
                            info.obj.bodyChunks[j].pos += offset;
                        }
                    }


                }
            }
        }

        public bool TryDetachObject(Player player, out PhysicalObject detachedObject)
        {
            detachedObject = null;
            if (tinker.Silk.RainMeadow.RainMeadowBridge.IsOnlineAndRemote(player)) return false;

            foreach (AttachedObjectInfo info in attachedObjects)
            {
                if (info.obj == null || !info.isPlayerDetachable ||
                    !tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(info.obj)) continue;

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
                if (info.obj != null && info.obj.bodyChunks != null &&
                    tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(info.obj))
                {
                    int index = Mathf.Clamp(info.attachedNodeIndex, 0, physicsNodes.Length - 1);
                    Vector2 supportVelocity = physicsNodes[index].Position - physicsNodes[index].FramePosition;
                    supportVelocity += WaveOffset(index, 1f) - WaveOffset(index, 0f);
                    foreach (var chunk in info.obj.bodyChunks)
                        chunk.vel += Vector2.ClampMagnitude(supportVelocity, 8f);
                }
            }
            attachedObjects.Clear();
        }

        private void UpdateRenderPoints()
        {
            if (physicsNodes == null || RenderPoints == null) return;

            for (int i = 0; i < physicsNodes.Length; i++)
            {
                previousRenderPoints[i] = physicsNodes[i].FramePosition;
                RenderPoints[i] = physicsNodes[i].Position;
            }
        }

        public List<Vector2> GetRenderPath()
        {
            if (networkCurve != null) return networkCurve.Path();
            if (!waves.Active)
            {
                var restingPath = new List<Vector2>(rope.Particles.Length);
                foreach (var node in rope.Particles) restingPath.Add(node.Position);
                return restingPath;
            }
            int samples = Mathf.Clamp(Mathf.CeilToInt(rope.SegmentLength * SegmentCount / 6f), SegmentCount, 160);
            var path = new List<Vector2>(samples + rope.Coordinates.Length);
            // Include every junction exactly, even between the regular samples.
            for (int edge = 0; edge < rope.Coordinates.Length - 1; edge++)
            {
                float from = rope.Coordinates[edge], to = rope.Coordinates[edge + 1];
                int count = Mathf.Max(1, Mathf.CeilToInt((to - from) / SegmentCount * samples));
                for (int i = 0; i < count; i++)
                {
                    float coordinate = Mathf.Lerp(from, to, (float)i / count);
                    path.Add(rope.Sample(coordinate, 1f) + WaveOffset(coordinate, 1f));
                }
            }
            path.Add(rope.Sample(SegmentCount, 1f) + WaveOffset(SegmentCount, 1f));
            return path;
        }

        public Vector2 GetClosestPoint(Vector2 worldPos, out int segIndex, out float t)
        {
            if (networkCurve != null) return networkCurve.Closest(worldPos, out segIndex, out t);
            // Refine on the displaced support, retaining material coordinates
            // so climbing and bridge attachments never slide as a wave passes.
            Vector2 bestPoint = rope.ClosestPoint(worldPos, out segIndex, out t);
            if (!waves.Active) return bestPoint;
            float best = float.MaxValue;
            for (int edge = 0; edge < rope.Coordinates.Length - 1; edge++)
            {
                float from = rope.Coordinates[edge], to = rope.Coordinates[edge + 1];
                int count = Mathf.Max(1, Mathf.CeilToInt((to - from) * rope.SegmentLength / 6f));
                Vector2 a = rope.Sample(from, 1f) + WaveOffset(from, 1f);
                for (int i = 1; i <= count; i++)
                {
                    float coordinate = Mathf.Lerp(from, to, (float)i / count);
                    Vector2 b = rope.Sample(coordinate, 1f) + WaveOffset(coordinate, 1f);
                    Vector2 delta = b - a;
                    float f = delta.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector2.Dot(worldPos - a, delta) / delta.sqrMagnitude) : 0f;
                    Vector2 point = a + delta * f;
                    float distance = (worldPos - point).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance; bestPoint = point;
                        float material = Mathf.Lerp(from, to, (i - 1f + f) / count);
                        segIndex = Mathf.Min((int)material, SegmentCount - 1); t = material - segIndex;
                    }
                    a = b;
                }
            }
            return bestPoint;
        }

        public void ApplyForceAt(Vector2 worldPos, Vector2 force, float radius)
        {
            if (!IsActive) return;
            if (tinker.Silk.RainMeadow.RainMeadowBridge.RelayForce(this, worldPos, force)) return;
            Vector2 point = GetClosestPoint(worldPos, out int segment, out float t);
            if (Vector2.Distance(point, worldPos) > radius) return;
            // Queue once, then distribute to the two supporting nodes. Moving
            // every node in a radius multiplied the impulse by mesh resolution.
            rope.AddForce(segment, t, Vector2.ClampMagnitude(force, 30f));
            Vector2 limited = Vector2.ClampMagnitude(force, 30f);
            externalForces[segment] += limited * (1f - t);
            externalForces[segment + 1] += limited * t;
        }

        internal void ExciteMotion(int segment, float t, Vector2 impulse, bool gentle = false)
        {
            if (tinker.Silk.RainMeadow.RainMeadowBridge.RelayForce(this, GetPointOnSegment(segment, t), impulse)) return;
            if (IsActive) ExciteAt(segment + Mathf.Clamp01(t), impulse, gentle);
        }

        private void ExciteAt(float coordinate, Vector2 impulse, bool gentle = false)
        {
            Vector2 tangent = (rope.Sample(coordinate + 0.05f, 1f) - rope.Sample(coordinate - 0.05f, 1f)).normalized;
            Vector2 transverse = impulse - tangent * Vector2.Dot(impulse, tangent);
            waves.PluckAt(coordinate, transverse, gentle);
        }

        private Vector2 WaveOffset(float coordinate, float timeStacker)
        {
            if (waves == null) return Vector2.zero;
            Vector2 offset = waves.Sample(coordinate, timeStacker);
            if (offset.sqrMagnitude < 0.000001f || room == null) return offset;
            Vector2 basis = rope.Sample(coordinate, timeStacker);
            // Clip the displacement against terrain, including thin intervening
            // tiles, without feeding a render correction into the sag solver.
            if (room.GetTile(basis + offset).Solid ||
                SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, basis, basis + offset).HasValue)
                return Vector2.zero;
            return offset;
        }

        internal Vector2 GetBasePoint(int segment, float t) => networkCurve != null
            ? networkCurve.Sample(segment + Mathf.Clamp01(t), 1f, false) : rope.Point(segment, t);

        internal Vector2 GetSupportOffset(int segment, float t) => networkCurve != null
            ? networkCurve.Sample(segment + t, 1f, true) - networkCurve.Sample(segment + t, 1f, false)
            : WaveOffset(segment + Mathf.Clamp01(t), 1f);

        internal float MaterialDistance(float from, float to) => Mathf.Abs(to - from) * rope.SegmentLength;

        public Vector2 GetVelocityOnSegment(int segment, float t)
        {
            if (networkCurve != null) return networkCurve.Sample(segment + t, 1f, true) - networkCurve.Sample(segment + t, 0f, true);
            segment = Mathf.Clamp(segment, 0, physicsNodes.Length - 2);
            float coordinate = segment + Mathf.Clamp01(t);
            return rope.Sample(coordinate, 1f) - rope.Sample(coordinate, 0f) +
                WaveOffset(coordinate, 1f) - WaveOffset(coordinate, 0f);
        }

        public Vector2 GetRenderPoint(int index, float timeStacker)
        {
            if (networkCurve != null) return networkCurve.Sample(index, timeStacker, false);
            return Vector2.Lerp(previousRenderPoints[index], RenderPoints[index], timeStacker);
        }

        public int VisualEdgeCount => networkCurve != null ? networkCurve.Count - 1 : rope.Particles.Length - 1;

        public float GetVisualParameter(float fraction)
        {
            if (networkCurve != null) return networkCurve.Parameter(fraction) / SegmentCount;
            float along = Mathf.Clamp01(fraction) * VisualEdgeCount;
            int edge = Mathf.Min((int)along, VisualEdgeCount - 1);
            return Mathf.Lerp(rope.Coordinates[edge], rope.Coordinates[edge + 1], along - edge) / SegmentCount;
        }

        public Vector2 GetVisualPoint(float t, float timeStacker)
        {
            if (networkCurve != null) return networkCurve.Sample(Mathf.Clamp01(t) * SegmentCount, timeStacker, true);
            float along = Mathf.Clamp01(t) * SegmentCount;
            int segment = Mathf.Min(Mathf.FloorToInt(along), SegmentCount - 1);
            float localT = along - segment;
            Vector2 a = GetRenderPoint(Mathf.Max(0, segment - 1), timeStacker);
            Vector2 b = GetRenderPoint(segment, timeStacker);
            Vector2 c = GetRenderPoint(segment + 1, timeStacker);
            Vector2 d = GetRenderPoint(Mathf.Min(SegmentCount, segment + 2), timeStacker);
            if (segment == 0) a = b * 2f - c;
            if (segment == SegmentCount - 1) d = c * 2f - b;
            Vector2 linear = rope.Sample(along, timeStacker);
            // A junction is a hinge. Spline tangents must not round it into a brace.
            Vector2 point = rope.HasJunctions ? linear : SilkDynamics.Curve(a, b, c, d, localT);
            float wave = rope.HasJunctions ? 0f : launchTightening.Offset(t, Vector2.Distance(GetRenderPoint(0, timeStacker),
                GetRenderPoint(SegmentCount, timeStacker)), timeStacker);
            point += Custom.PerpendicularVector((c - b).normalized) * wave;
            point += WaveOffset(along, timeStacker);
            // Reject a visual correction that ends inside terrain.
            return room != null && room.GetTile(point).Solid ? linear : point;
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
            if (tinker.Silk.RainMeadow.RainMeadowBridge.RelayDamage(this, amount, impactPoint)) return;
            health -= amount;
            if (health <= 0) Break(impactPoint);
        }

        private void Break(Vector2 impactPoint)
        {
            if (slatedForDeletetion) return;
            slatedForDeletetion = true;
            ReleaseAllAttachedObjects();
            BrokenSilkManager.TriggerBreakAnimation(GetRenderPath(), room, impactPoint, startAnchor, endAnchor);
            room?.PlaySound(SoundID.Spear_Stick_In_Wall, startPoint, 0.8f, 1.2f);
            room?.PlaySound(SoundID.Spear_Stick_In_Wall, endPoint, 0.8f, 1.2f);
        }

        public Vector2 GetPointOnSegment(int segIndex, float t)
        {
            if (networkCurve != null) return networkCurve.Sample(segIndex + Mathf.Clamp01(t), 1f, true);
            if (RenderPoints == null || RenderPoints.Length < 2) return startPoint;

            segIndex = Mathf.Clamp(segIndex, 0, RenderPoints.Length - 2);
            t = Mathf.Clamp01(t);
            return rope.Point(segIndex, t) + WaveOffset(segIndex + t, 1f);
        }

        public bool IsPlayerNearBridge(Player player, float threshold = 20f)
        {
            if (player?.bodyChunks == null) return false;

            Vector2 playerPos = player.bodyChunks[0].pos;
            return Vector2.Distance(playerPos, GetClosestPoint(playerPos, out _, out _)) < threshold;
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
        public bool IsActive => room != null && !slatedForDeletetion && rope != null && physicsNodes.Length > 0 &&
                                (networkCurve != null || (startAnchor.IsValid(room) && endAnchor.IsValid(room)));

        Vector2 IClimbableSilk.GetPointOnSegment(int segIndex, float t)
        {
            return GetPointOnSegment(segIndex, t);
        }

        public void ApplyClimbForce(Vector2 worldPos, Vector2 force)
        {
            // Static support bypasses transient excitation.
            if (!IsActive) return;
            if (tinker.Silk.RainMeadow.RainMeadowBridge.RelayForce(this, worldPos, force)) return;
            Vector2 point = GetClosestPoint(worldPos, out int segment, out float t);
            if (Vector2.Distance(point, worldPos) <= 24f)
                rope.AddForce(segment, t, Vector2.ClampMagnitude(force, 30f));
        }

    }
}
