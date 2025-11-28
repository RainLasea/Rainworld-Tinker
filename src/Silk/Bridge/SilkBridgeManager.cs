using RWCustom;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using tinker.Silk;
using UnityEngine;

namespace Weaver.Silk.Bridge
{
    public class BridgeModeState
    {
        public bool active;
        public Vector2 point2;
        public bool virtualSilkActive;
        public Vector2 virtualSilkPos;
        public Vector2 virtualSilkVel;
        public bool animating;
        public Vector2 targetLD1;
        public bool hasTarget;

        private SilkBridge attachedBridge = null;
        private int attachedSegIndex = -1;
        private float attachedT = 0f;


        public PhysicalObject d1AttachedObject = null;
        public SilkBridge d1AttachedBridge = null;

        public PhysicalObject d2AttachedObject = null;
        public SilkBridge d2AttachedBridge = null;

        public SilkBridge ignoreBridgeForD1 = null;

        private Vector2? aimTarget = null;
        private const float AIM_TARGET_PRIORITY_DISTANCE = 150f;

        private const float SHOOT_SPEED = 50f;
        private const float GRAVITY = 0.9f;
        private IntVector2[] _cachedRtList = new IntVector2[20];

        public void Activate(Vector2 pos)
        {
            active = true;
            point2 = pos;
            virtualSilkActive = false;
            animating = false;
            hasTarget = false;

            attachedBridge = null;
            attachedSegIndex = -1;
            attachedT = 0f;
            aimTarget = null;
        }

        public void AttachD2ToBridge(SilkBridge bridge, int segIndex, float t)
        {
            attachedBridge = bridge;
            d2AttachedBridge = bridge;
            attachedSegIndex = segIndex;
            attachedT = t;
            d2AttachedObject = null;


            if (bridge != null)
            {
                point2 = bridge.GetPointOnSegment(segIndex, t);
            }
        }

        public void AttachD2ToObject(PhysicalObject obj)
        {
            d2AttachedObject = obj;
            d2AttachedBridge = null;
            attachedBridge = null;
            attachedSegIndex = -1;
            attachedT = 0f;
        }

        public void UpdateD2Position(Room room)
        {
            if (!active) return;


            if (d2AttachedObject != null)
            {

                if (d2AttachedObject.slatedForDeletetion || d2AttachedObject.room != room)
                {
                    d2AttachedObject = null;
                    return;
                }


                if (d2AttachedObject.bodyChunks != null && d2AttachedObject.bodyChunks.Length > 0)
                {
                    point2 = d2AttachedObject.bodyChunks[0].pos;
                }
            }

            else if (attachedBridge != null)
            {

                if (attachedBridge.room != room || !SilkBridgeManager.GetBridgesInRoom(room).Contains(attachedBridge))
                {
                    attachedBridge = null;
                    d2AttachedBridge = null;
                    attachedSegIndex = -1;
                    attachedT = 0f;
                    return;
                }


                point2 = attachedBridge.GetPointOnSegment(attachedSegIndex, attachedT);
            }
        }

        public void Deactivate()
        {
            active = false;
            point2 = Vector2.zero;
            virtualSilkActive = false;
            animating = false;
            hasTarget = false;
            attachedBridge = null;
            attachedSegIndex = -1;
            attachedT = 0f;
            d1AttachedObject = null;
            d1AttachedBridge = null;
            d2AttachedObject = null;
            d2AttachedBridge = null;
            aimTarget = null;
        }

        public void ShootVirtualSilk(Vector2 direction, Vector2 startPos, Room room, Vector2? targetPos)
        {
            if (room == null) return;

            virtualSilkActive = true;
            virtualSilkPos = startPos;


            aimTarget = targetPos;

            if (targetPos.HasValue)
            {
                Vector2 dir = targetPos.Value - startPos;
                virtualSilkVel = dir.sqrMagnitude > 0.0001f ? dir.normalized * SHOOT_SPEED : Vector2.zero;
            }
            else
            {
                virtualSilkVel = direction.normalized * SHOOT_SPEED;
            }
            animating = true;
            hasTarget = false;
        }

        public void UpdateVirtualSilk(Player player)
        {
            if (player?.room == null)
            {
                if (!animating && !hasTarget)
                {
                    SilkBridgeManager.CancelPlayerBuildMode(player);
                }
                return;
            }


            if (animating && hasTarget && !virtualSilkActive)
            {
                virtualSilkPos = targetLD1;

                float distance = Vector2.Distance(targetLD1, point2);
                if (distance > 1200f)
                {
                    SilkBridgeManager.CancelPlayerBuildMode(player);
                    animating = false;
                    return;
                }

                SilkBridgeManager.CreateBridge(player, targetLD1, point2);
                SilkBridgeManager.CancelPlayerBuildMode(player);
                animating = false;
                return;
            }


            if (virtualSilkActive)
            {
                Vector2 lastPos = virtualSilkPos;
                virtualSilkVel.y -= GRAVITY;
                virtualSilkPos += virtualSilkVel;


                bool prioritizeTerrain = ShouldPrioritizeTerrainCollision();

                if (prioritizeTerrain)
                {
                    if (TryHandleTerrainCollision(player, lastPos, virtualSilkPos) ||
                        TryHandleBeamCollision(player, lastPos) ||
                        TryHandleObjectCollision(player))
                    {
                        return;
                    }
                }
                else
                {
                    if (TryHandleBridgeCollision(player, lastPos) ||
                        TryHandleTerrainCollision(player, lastPos, virtualSilkPos) ||
                        TryHandleObjectCollision(player) ||
                        TryHandleBeamCollision(player, lastPos))
                    {
                        return;
                    }
                }

                HandleReturnLogic(player);
            }
        }

        private bool ShouldPrioritizeTerrainCollision()
        {
            if (!aimTarget.HasValue) return false;

            float distanceToAim = Vector2.Distance(virtualSilkPos, aimTarget.Value);

            if (distanceToAim < AIM_TARGET_PRIORITY_DISTANCE)
            {
                Vector2 toAim = (aimTarget.Value - virtualSilkPos).normalized;
                float dot = Vector2.Dot(virtualSilkVel.normalized, toAim);

                if (dot > 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHandleBridgeCollision(Player player, Vector2 lastPos)
        {
            if (player?.room == null) return false;

            if (aimTarget.HasValue && ShouldPrioritizeTerrainCollision())
            {
                return false;
            }

            SilkBridge ignoreD2Bridge = d2AttachedBridge;

            var bridges = SilkBridgeManager.GetBridgesInRoom(player.room);
            if (bridges == null || bridges.Count == 0) return false;

            SilkBridge closestBridge = null;
            Vector2 closestHitPoint = Vector2.zero;
            float closestDist = float.MaxValue;

            foreach (var bridge in bridges)
            {
                if (bridge == null || bridge.room != player.room) continue;

                if (ignoreD2Bridge != null && bridge == ignoreD2Bridge)
                {
                    continue;
                }

                var path = bridge.GetRenderPath();
                if (path == null || path.Count < 2) continue;

                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 segStart = path[i];
                    Vector2 segEnd = path[i + 1];

                    if (SilkBridgeManager.SegmentIntersection(lastPos, virtualSilkPos, segStart, segEnd, out Vector2 hitPoint, out float t))
                    {
                        float dist = Vector2.Distance(lastPos, hitPoint);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestBridge = bridge;
                            closestHitPoint = hitPoint;
                        }
                    }
                }
            }

            if (closestBridge != null)
            {
                targetLD1 = closestHitPoint;
                hasTarget = true;
                d1AttachedBridge = closestBridge;
                d1AttachedObject = null;
                animating = true;
                virtualSilkPos = closestHitPoint;
                virtualSilkActive = false;
                return true;
            }

            return false;
        }

        private bool TryHandleTerrainCollision(Player player, Vector2 lastPos, Vector2 curPos)
        {
            if (player?.room == null) return false;

            IntVector2? hitTile = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(player.room, lastPos, curPos);
            if (!hitTile.HasValue) return false;

            FloatRect tileRect = player.room.TileRect(hitTile.Value);
            Vector2 precise = GetTileCollisionPoint(lastPos, curPos, tileRect);

            targetLD1 = precise;
            hasTarget = true;
            d1AttachedObject = null;
            d1AttachedBridge = null;
            animating = true;
            virtualSilkPos = precise;
            virtualSilkActive = false;
            return true;
        }

        private bool TryHandleObjectCollision(Player player)
        {
            if (player.room == null) return false;

            foreach (var obj in player.room.physicalObjects)
            {
                foreach (var item in obj)
                {
                    if (item == player) continue;

                    bool ok = item is Weapon || item is DangleFruit || item is SporePlant ||
                              item is DataPearl || item is Rock || item is ScavengerBomb ||
                              item is Spear || item is FirecrackerPlant;
                    if (!ok) continue;

                    foreach (var chunk in item.bodyChunks)
                    {
                        if (Vector2.Distance(virtualSilkPos, chunk.pos) < chunk.rad + 5f)
                        {
                            targetLD1 = chunk.pos;
                            hasTarget = true;
                            d1AttachedObject = item;
                            d1AttachedBridge = null;
                            animating = true;
                            virtualSilkPos = chunk.pos;
                            virtualSilkActive = false;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool TryHandleBeamCollision(Player player, Vector2 lastPos)
        {
            int rayCount = SharedPhysics.RayTracedTilesArray(lastPos, virtualSilkPos, _cachedRtList);

            for (int i = 0; i < rayCount; i++)
            {
                if (player.room.GetTile(_cachedRtList[i]).horizontalBeam)
                {
                    float midY = player.room.MiddleOfTile(_cachedRtList[i]).y;
                    float crossX = Custom.HorizontalCrossPoint(lastPos, virtualSilkPos, midY).x;
                    float clampedX = Mathf.Clamp(crossX,
                        player.room.MiddleOfTile(_cachedRtList[i]).x - 10f,
                        player.room.MiddleOfTile(_cachedRtList[i]).x + 10f);

                    Vector2 hitPoint = new Vector2(clampedX, midY);
                    targetLD1 = hitPoint;
                    hasTarget = true;
                    d1AttachedObject = null;
                    d1AttachedBridge = null;
                    animating = true;
                    virtualSilkPos = hitPoint;
                    virtualSilkActive = false;
                    return true;
                }
                if (player.room.GetTile(_cachedRtList[i]).verticalBeam)
                {
                    float midX = player.room.MiddleOfTile(_cachedRtList[i]).x;
                    float crossY = Custom.VerticalCrossPoint(lastPos, virtualSilkPos, midX).y;
                    float clampedY = Mathf.Clamp(crossY,
                        player.room.MiddleOfTile(_cachedRtList[i]).y - 10f,
                        player.room.MiddleOfTile(_cachedRtList[i]).y + 10f);

                    Vector2 hitPoint = new Vector2(midX, clampedY);
                    targetLD1 = hitPoint;
                    hasTarget = true;
                    d1AttachedObject = null;
                    d1AttachedBridge = null;
                    animating = true;
                    virtualSilkPos = hitPoint;
                    virtualSilkActive = false;
                    return true;
                }
            }
            return false;
        }

        public void SetD1TargetBridge(SilkBridge bridge, Vector2 hitPoint)
        {
            d1AttachedBridge = bridge;
            d1AttachedObject = null;
            ignoreBridgeForD1 = bridge;
            targetLD1 = hitPoint;
            hasTarget = true;
            animating = true;
            virtualSilkPos = hitPoint;
            virtualSilkActive = false;
        }

        public void SetD1TargetTerrain(Vector2 hitPoint)
        {
            d1AttachedBridge = null;
            d1AttachedObject = null;
            ignoreBridgeForD1 = null;
            targetLD1 = hitPoint;
            hasTarget = true;
            animating = true;
            virtualSilkPos = hitPoint;
            virtualSilkActive = false;
        }

        public void SetD1TargetObject(PhysicalObject obj, Vector2 hitPoint)
        {
            d1AttachedBridge = null;
            d1AttachedObject = obj;
            ignoreBridgeForD1 = null;
            targetLD1 = hitPoint;
            hasTarget = true;
            animating = true;
            virtualSilkPos = hitPoint;
            virtualSilkActive = false;
        }

        public Vector2 GetRenderD1Position() => virtualSilkPos;

        public Vector2 GetCurrentD2BridgePos()
        {
            if (d2AttachedBridge != null && attachedSegIndex >= 0 && attachedSegIndex < d2AttachedBridge.SegmentCount)
            {
                int seg = Mathf.Clamp(attachedSegIndex, 0, d2AttachedBridge.SegmentCount - 1);
                float t = Mathf.Clamp01(attachedT);
                return d2AttachedBridge.GetPointOnSegment(seg, t);
            }
            return point2;
        }

        private void HandleReturnLogic(Player player)
        {
            if (Vector2.Dot(Custom.DirVec(point2, virtualSilkPos), virtualSilkVel.normalized) < -0.6f)
            {
                SilkBridgeManager.CancelPlayerBuildMode(player);
            }
        }

        private Vector2 GetTileCollisionPoint(Vector2 start, Vector2 end, FloatRect tileRect)
        {
            FloatRect expanded = tileRect.Grow(2f);
            FloatRect collision = Custom.RectCollision(end, start, expanded);

            Vector2 collisionPoint = new Vector2(collision.left, collision.bottom);
            collisionPoint.x = Mathf.Clamp(collisionPoint.x, tileRect.left, tileRect.right);
            collisionPoint.y = Mathf.Clamp(collisionPoint.y, tileRect.bottom, tileRect.top);

            return collisionPoint;
        }
    }

    public static class SilkBridgeManager
    {
        private static readonly Dictionary<Room, List<SilkBridge>> roomBridges = new Dictionary<Room, List<SilkBridge>>();
        private static readonly ConditionalWeakTable<Player, BridgeModeState> playerBridgeStates = new ConditionalWeakTable<Player, BridgeModeState>();

        public static void Initialize()
        {
            On.RainWorldGame.ShutDownProcess += RainWorldGame_ShutDownProcess;
            On.Room.Loaded += Room_Loaded;
            On.RoomCamera.Update += RoomCamera_Update;
        }

        public static void Cleanup()
        {
            On.RainWorldGame.ShutDownProcess -= RainWorldGame_ShutDownProcess;
            On.Room.Loaded -= Room_Loaded;
            On.RoomCamera.Update -= RoomCamera_Update;
            ClearAllBridges();
        }

        public static BridgeModeState GetBridgeModeState(Player player)
        {
            return player == null ? null : playerBridgeStates.GetValue(player, p => new BridgeModeState());
        }

        public static void CancelPlayerBuildMode(Player player)
        {
            tinkerSilkData.Get(player)?.DetachPhysicsOnly();
            GetBridgeModeState(player)?.Deactivate();
        }

        public static void CreateBridge(Player player, Vector2 point1, Vector2 point2)
        {
            if (player?.room == null) return;

            float straightDistance = Vector2.Distance(point1, point2);
            if (straightDistance > 1200f) return;

            int nodeCount = CalculateNodeCount(straightDistance);
            float maxBridgeLength = straightDistance * 1.15f;

            BridgeModeState bridgeState = GetBridgeModeState(player);

            BridgeAnchor anchor1;
            if (bridgeState?.d1AttachedObject != null)
            {
                anchor1 = BridgeAnchor.CreateObjectAnchor(bridgeState.d1AttachedObject, point1);
            }
            else if (bridgeState?.d1AttachedBridge != null)
            {
                anchor1 = BridgeAnchor.CreateBridgeAnchor(bridgeState.d1AttachedBridge, point1);
            }
            else
            {
                anchor1 = BridgeAnchor.CreateTerrainAnchor(point1);
            }

            BridgeAnchor anchor2;
            if (bridgeState?.d2AttachedObject != null)
            {
                anchor2 = BridgeAnchor.CreateObjectAnchor(bridgeState.d2AttachedObject, point2);
            }
            else if (bridgeState?.d2AttachedBridge != null)
            {
                anchor2 = BridgeAnchor.CreateBridgeAnchor(bridgeState.d2AttachedBridge, point2);
            }
            else
            {
                anchor2 = BridgeAnchor.CreateTerrainAnchor(point2);
            }

            SilkBridge bridge = new SilkBridge(anchor1, anchor2, player.room, maxBridgeLength, nodeCount);

            bridge.Update();

            if (!roomBridges.ContainsKey(player.room))
                roomBridges[player.room] = new List<SilkBridge>();

            roomBridges[player.room].Add(bridge);
            CancelPlayerBuildMode(player);
        }

        private static int CalculateNodeCount(float distance)
        {
            if (distance < 50f) return 3;
            if (distance < 100f) return 5;
            if (distance < 200f) return 8;
            if (distance < 400f) return 12;
            return 15;
        }

        public static List<SilkBridge> GetBridgesInRoom(Room room)
        {
            return room == null ? new List<SilkBridge>() :
                   roomBridges.GetValueOrDefault(room, new List<SilkBridge>());
        }

        public static void ClearBridgesInRoom(Room room)
        {
            if (room != null && roomBridges.ContainsKey(room))
                roomBridges[room].Clear();
        }

        public static void ClearAllBridges() => roomBridges.Clear();


        public static SilkBridge GetClosestBridge(Room room, Vector2 pos, float range = 30f)
        {
            if (!roomBridges.ContainsKey(room)) return null;

            SilkBridge closest = null;
            float bestDist = range;

            foreach (var bridge in roomBridges[room])
            {
                if (!bridge.IsActive) continue;

                int segIndex;
                float t;
                Vector2 closestPoint = bridge.GetClosestPoint(pos, out segIndex, out t);
                float d = Vector2.Distance(closestPoint, pos);

                if (d < bestDist)
                {
                    bestDist = d;
                    closest = bridge;
                }
            }

            return closest;
        }

        public static bool RayTraceBridgesReturnFirstIntersection(Room room, Vector2 A, Vector2 B,
            out SilkBridge hitBridge, out Vector2 hitPoint, out float tOut)
        {
            return RayTraceBridgesInternal(room, A, B, null, out hitBridge, out hitPoint, out tOut);
        }

        public static bool RayTraceBridgesIgnoreSelf(Room room, Vector2 A, Vector2 B, Vector2 ignorePoint,
            out SilkBridge hitBridge, out Vector2 hitPoint, out float tOut)
        {
            return RayTraceBridgesInternal(room, A, B, ignorePoint, out hitBridge, out hitPoint, out tOut);
        }

        private static bool RayTraceBridgesInternal(Room room, Vector2 A, Vector2 B, Vector2? ignorePoint,
            out SilkBridge hitBridge, out Vector2 hitPoint, out float tOut)
        {
            hitBridge = null;
            hitPoint = Vector2.zero;
            tOut = 1f + 1e-6f;

            if (room == null || !roomBridges.ContainsKey(room))
                return false;

            float bestT = 1f + 1e-6f;
            SilkBridge bestBridge = null;
            Vector2 bestPoint = Vector2.zero;

            foreach (var bridge in roomBridges[room])
            {
                if (ignorePoint.HasValue && IsPointOnBridge(bridge, ignorePoint.Value, 5f))
                    continue;

                var path = bridge.GetRenderPath();
                for (int i = 0; i < path.Count - 1; i++)
                {
                    if (SegmentIntersection(A, B, path[i], path[i + 1], out Vector2 inter, out float tOnAB) &&
                        tOnAB >= 0f && tOnAB <= 1f && tOnAB < bestT)
                    {
                        bestT = tOnAB;
                        bestBridge = bridge;
                        bestPoint = inter;
                    }
                }
            }

            if (bestBridge != null)
            {
                hitBridge = bestBridge;
                hitPoint = bestPoint;
                tOut = bestT;
                return true;
            }

            return false;
        }

        private static bool IsPointOnBridge(SilkBridge bridge, Vector2 point, float tolerance = 5f)
        {
            var path = bridge.GetRenderPath();
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (DistanceToSegment(point, path[i], path[i + 1]) < tolerance)
                    return true;
            }
            return false;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 segStart, Vector2 segEnd)
        {
            Vector2 line = segEnd - segStart;
            float len = line.magnitude;
            if (len < 0.01f) return Vector2.Distance(point, segStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - segStart, line) / (len * len));
            Vector2 projection = segStart + t * line;
            return Vector2.Distance(point, projection);
        }

        public static bool SegmentIntersection(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2,
            out Vector2 intersection, out float tOnAB)
        {
            intersection = Vector2.zero;
            tOnAB = 0f;

            Vector2 r = p2 - p;
            Vector2 s = q2 - q;
            float rxs = Cross(r, s);

            if (Mathf.Abs(rxs) < 1e-6f) return false;

            float t = Cross(q - p, s) / rxs;
            float u = Cross(q - p, r) / rxs;

            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
            {
                intersection = p + t * r;
                tOnAB = t;
                return true;
            }

            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void RainWorldGame_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
        {
            ClearAllBridges();
            orig(self);
        }

        private static void Room_Loaded(On.Room.orig_Loaded orig, Room self)
        {
            orig(self);
            if (!roomBridges.ContainsKey(self))
                roomBridges[self] = new List<SilkBridge>();
        }

        private static void RoomCamera_Update(On.RoomCamera.orig_Update orig, RoomCamera self)
        {
            orig(self);
            if (self.room == null) return;

            if (roomBridges.ContainsKey(self.room))
            {
                foreach (var bridge in roomBridges[self.room])
                    bridge.Update();
            }

            foreach (var player in self.room.game.Players)
            {
                if (player?.realizedCreature is Player p)
                {
                    var bridgeState = GetBridgeModeState(p);

                    if (bridgeState?.active == true)
                    {
                        bridgeState.UpdateD2Position(p.room);
                    }

                    if (bridgeState?.virtualSilkActive == true || bridgeState?.animating == true)
                        bridgeState.UpdateVirtualSilk(p);
                }
            }
        }

        public static bool IsSilkAt(Room room, int x, int y, out bool isVertical)
        {
            foreach (var bridge in GetBridgesInRoom(room))
            {
                var path = bridge.GetRenderPath();
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1];

                    if (PointNearSegment(new Vector2(x + 0.5f, y + 0.5f) * 20f, a, b, 8f))
                    {
                        isVertical = Mathf.Abs(a.y - b.y) > Mathf.Abs(a.x - b.x);
                        return true;
                    }
                }
            }
            isVertical = false;
            return false;
        }

        private static bool PointNearSegment(Vector2 p, Vector2 a, Vector2 b, float threshold)
        {
            float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
            Vector2 proj = a + (b - a) * t;
            return Vector2.Distance(p, proj) < threshold;
        }
    }
}