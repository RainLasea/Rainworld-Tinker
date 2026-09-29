using RWCustom;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace Tinker.Silk.Bridge
{
    internal sealed class SilkTargeting
    {
        internal struct Target
        {
            internal bool Valid;
            internal Vector2 Point;
            internal SilkBridge Bridge;
            internal BodyChunk Chunk;
            private int segment;
            private float fraction;
            private Vector2 offset;

            internal void Capture()
            {
                if (Bridge != null) Bridge.GetClosestPoint(Point, out segment, out fraction);
                if (Chunk != null) offset = Point - Chunk.pos;
            }

            internal bool Refresh(Room room)
            {
                if (!Valid) return false;
                if (Bridge != null)
                {
                    if (!Bridge.IsActive || Bridge.room != room) return false;
                    Point = Bridge.GetPointOnSegment(segment, fraction);
                }
                if (Chunk != null)
                {
                    if (Chunk.owner.slatedForDeletetion || Chunk.owner.room != room) return false;
                    Point = Chunk.pos + offset;
                }
                return true;
            }
        }

        // A 1200 px diagonal crosses at most 122 of Rain World's 20 px tiles.
        private readonly IntVector2[] tiles = new IntVector2[256];

        internal Target Select(Player player, Room room, Vector2 origin, Vector2 cursor,
            SilkBridge sourceBridge, PhysicalObject sourceObject)
        {
            Vector2 direction = cursor - origin;
            if (direction.sqrMagnitude < SilkTargetGeometry.MinRange * SilkTargetGeometry.MinRange) return default;
            var best = Find(player, room, origin, origin + direction.normalized * SilkTargetGeometry.MaxRange,
                origin, cursor, sourceBridge, sourceObject, true);
            best.Capture();
            return best;
        }

        internal Target Trace(Player player, Room room, Vector2 from, Vector2 to, Vector2 origin,
            SilkBridge sourceBridge, PhysicalObject sourceObject) =>
            Find(player, room, from, to, origin, from, sourceBridge, sourceObject, false);

        private Target Find(Player player, Room room, Vector2 from, Vector2 to, Vector2 origin, Vector2 cursor,
            SilkBridge sourceBridge, PhysicalObject sourceObject, bool assist)
        {
            Target best = default;
            if (room == null || (to - from).sqrMagnitude < 0.000001f) return best;
            if (TerrainHit(room, from, to, out Vector2 wall))
            {
                to = wall;
                Consider(ref best, wall, origin, cursor, null, null);
            }

            foreach (var bridge in SilkBridgeManager.GetBridgesInRoom(room))
            {
                if (bridge == null || !bridge.IsActive || bridge.room != room || bridge == sourceBridge) continue;
                var path = bridge.GetRenderPath();
                for (int i = 0; i + 1 < path.Count; i++)
                    Segment(room, from, to, origin, cursor, path[i], path[i + 1], bridge, assist, ref best);
            }

            int count = SharedPhysics.RayTracedTilesArray(from, to, tiles);
            for (int i = 0; i < Mathf.Min(count, tiles.Length); i++)
                Beam(room, tiles[i], from, to, origin, cursor, assist, ref best);
            if (assist)
            {
                var tile = room.GetTilePosition(cursor);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    Beam(room, new IntVector2(tile.x + x, tile.y + y), from, to, origin, cursor, true, ref best);
            }

            foreach (var layer in room.physicalObjects)
            foreach (var obj in layer)
            {
                if (obj == null || obj == player || obj == sourceObject || obj.slatedForDeletetion ||
                    obj.room != room || obj.bodyChunks == null || !Attachable(obj)) continue;
                foreach (var chunk in obj.bodyChunks)
                {
                    if (SilkTargetGeometry.CircleHit(from, to, chunk.pos, chunk.rad, out var hit))
                        Consider(ref best, hit, origin, cursor, null, chunk);
                    if (!assist) continue;
                    Vector2 offset = cursor - chunk.pos;
                    Vector2 point = chunk.pos + Vector2.ClampMagnitude(offset, chunk.rad);
                    if (SilkTargetGeometry.CanAssist(origin, cursor, point) && Visible(room, origin, point))
                    {
                        // Select the facing surface, so a near miss cannot pull
                        // the endpoint through the body or a wall behind it.
                        if (SilkTargetGeometry.CircleHit(origin, point, chunk.pos, chunk.rad, out hit))
                            Consider(ref best, hit, origin, cursor, null, chunk);
                    }
                }
            }
            return best;
        }

        private static bool Attachable(PhysicalObject obj) => obj is Weapon || obj is DangleFruit ||
            obj is SporePlant || obj is DataPearl || obj is Rock || obj is ScavengerBomb ||
            obj is Spear || obj is FirecrackerPlant;

        private static void Consider(ref Target best, Vector2 point, Vector2 origin, Vector2 cursor,
            SilkBridge bridge, BodyChunk chunk)
        {
            float range = (point - origin).sqrMagnitude;
            if (range < SilkTargetGeometry.MinRange * SilkTargetGeometry.MinRange ||
                range > SilkTargetGeometry.MaxRange * SilkTargetGeometry.MaxRange + 0.1f) return;
            if (best.Valid && !SilkTargetGeometry.Better(point, best.Point, origin, cursor)) return;
            best = new Target { Valid = true, Point = point, Bridge = bridge, Chunk = chunk };
        }

        private static void Segment(Room room, Vector2 from, Vector2 to, Vector2 origin, Vector2 cursor,
            Vector2 a, Vector2 b, SilkBridge bridge, bool assist, ref Target best)
        {
            if (SilkTargetGeometry.SegmentHit(from, to, a, b, out var hit))
                Consider(ref best, hit, origin, cursor, bridge, null);
            if (!assist) return;
            Vector2 point = SilkTargetGeometry.ClosestPoint(cursor, a, b);
            if (SilkTargetGeometry.CanAssist(origin, cursor, point) && Visible(room, origin, point))
                Consider(ref best, point, origin, cursor, bridge, null);
        }

        private static void Beam(Room room, IntVector2 tile, Vector2 from, Vector2 to, Vector2 origin,
            Vector2 cursor, bool assist, ref Target best)
        {
            var data = room.GetTile(tile);
            if (data.Solid) return;
            Vector2 center = room.MiddleOfTile(tile);
            if (data.horizontalBeam)
                Segment(room, from, to, origin, cursor, center - Vector2.right * 10f,
                    center + Vector2.right * 10f, null, assist, ref best);
            if (data.verticalBeam)
                Segment(room, from, to, origin, cursor, center - Vector2.up * 10f,
                    center + Vector2.up * 10f, null, assist, ref best);
        }

        internal static bool TerrainHit(Room room, Vector2 from, Vector2 to, out Vector2 point)
        {
            point = from;
            // Leave a surface anchor without immediately colliding with its
            // starting tile. Shots directed into that tile remain blocked.
            Vector2 start = from + Vector2.ClampMagnitude(to - from, 0.05f);
            var tile = SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(room, start, to);
            if (!tile.HasValue) return false;
            var rect = room.TileRect(tile.Value);
            return SilkTargetGeometry.RectHit(start, to, new Vector2(rect.left, rect.bottom),
                new Vector2(rect.right, rect.top), out point);
        }

        private static bool Visible(Room room, Vector2 origin, Vector2 point) =>
            !TerrainHit(room, origin, point, out var hit) || (hit - point).sqrMagnitude < 0.01f;
    }
}
