using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    // Fine binding threads sample the same material coordinates as the main
    // silk, so they stay attached through vibration, stretching and camera zoom.
    internal sealed class SilkBridgeDetails
    {
        private const int Capacity = 1536;
        private readonly TriangleMesh mesh;
        private readonly List<SilkBridge> members = new List<SilkBridge>();
        private readonly List<Junction> junctions = new List<Junction>();
        private readonly List<Vector2> bindingPoints = new List<Vector2>();
        private int drawn;

        private sealed class Arm
        {
            internal SilkBridge Bridge;
            internal float Coordinate;
            internal int Direction;
            internal float EndCoordinate;

            internal Vector2 Point(float distance, float timeStacker)
            {
                float unit = Mathf.Max(0.001f, Bridge.MaterialDistance(0f, 1f));
                float coordinate = Coordinate + Direction * distance / unit;
                return Bridge.GetVisualPoint(coordinate / Bridge.SegmentCount, timeStacker);
            }

            internal Vector2 BindingPoint(float offset, float timeStacker)
            {
                float rest = Bridge.MaterialDistance(Coordinate, EndCoordinate);
                Vector2 origin = Point(0f, timeStacker);
                Vector2 end = Bridge.GetVisualPoint(EndCoordinate / Bridge.SegmentCount, timeStacker);
                float current = Vector2.Distance(origin, end);
                float reach = SilkJunctionPattern.Reach(offset, rest, current);
                // Convert physical reach back to material position so the small
                // thread remains on the sagging, vibrating Rain World strand.
                return Point(current > 0.001f ? reach / current * rest : 0f, timeStacker);
            }
        }

        private sealed class Junction
        {
            internal SilkBridge Parent;
            internal int CoordinateKey;
            internal float[] Offsets;
            internal readonly List<Arm> Arms = new List<Arm>();
        }

        internal SilkBridgeDetails(RoomCamera camera)
        {
            mesh = CreateMesh(Capacity, SilkBridgeGraphics.MainSilkColor);
            camera.ReturnFContainer("Midground").AddChild(mesh);
        }

        internal static TriangleMesh CreateMesh(int capacity, Color color)
        {
            var triangles = new TriangleMesh.Triangle[capacity * 2];
            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4;
                triangles[i * 2] = new TriangleMesh.Triangle(v, v + 1, v + 2);
                triangles[i * 2 + 1] = new TriangleMesh.Triangle(v + 1, v + 2, v + 3);
            }
            return new TriangleMesh("Futile_White", triangles, false, false) { color = color };
        }

        internal static void DrawLine(TriangleMesh mesh, int index, Vector2 a, Vector2 b, float width, Vector2 camera)
        {
            Vector2 delta = (b - a).normalized;
            Vector2 normal = new Vector2(-delta.y, delta.x) * (width * 0.5f);
            int v = index * 4;
            mesh.MoveVertice(v, a - normal - camera);
            mesh.MoveVertice(v + 1, a + normal - camera);
            mesh.MoveVertice(v + 2, b - normal - camera);
            mesh.MoveVertice(v + 3, b + normal - camera);
        }

        internal static void ClearUnused(TriangleMesh mesh, int first, int capacity)
        {
            for (int i = first * 4; i < capacity * 4; i++) mesh.MoveVertice(i, Vector2.zero);
        }

        private void AddAnchor(SilkBridge child, BridgeAnchor anchor, bool start)
        {
            if (anchor.type != BridgeAnchor.AnchorType.BridgeSegment || anchor.attachedBridge == null) return;
            SilkBridge parent = anchor.attachedBridge;
            float coordinate = anchor.segmentIndex + anchor.segmentT;
            // Collapse endpoint-to-endpoint attachment chains into one knot.
            for (int i = 0; i < members.Count; i++)
            {
                BridgeAnchor next = coordinate < 0.001f ? parent.startAnchor :
                    coordinate > parent.SegmentCount - 0.001f ? parent.endAnchor : null;
                if (next == null || next.type != BridgeAnchor.AnchorType.BridgeSegment || next.attachedBridge == null) break;
                parent = next.attachedBridge; coordinate = next.segmentIndex + next.segmentT;
            }
            int key = Mathf.RoundToInt(coordinate * 4096f);
            Junction junction = junctions.Find(value => value.Parent == parent && value.CoordinateKey == key);
            if (junction == null)
            {
                int seed;
                unchecked
                {
                    // Anchor creation positions give the same knot the same
                    // offsets across cameras and unrelated topology rebuilds.
                    seed = key * 397 ^ parent.startAnchor.terrainPos.x.GetHashCode();
                    seed = seed * 397 ^ parent.startAnchor.terrainPos.y.GetHashCode();
                    seed = seed * 397 ^ parent.endAnchor.terrainPos.x.GetHashCode();
                    seed = seed * 397 ^ parent.endAnchor.terrainPos.y.GetHashCode();
                }
                junction = new Junction { Parent = parent, CoordinateKey = key,
                    Offsets = SilkJunctionPattern.CreateOffsets(seed) };
                junctions.Add(junction);
                if (coordinate > 0.001f) AddArm(junction, parent, coordinate, -1);
                if (coordinate < parent.SegmentCount - 0.001f) AddArm(junction, parent, coordinate, 1);
            }
            AddArm(junction, child, start ? 0f : child.SegmentCount, start ? 1 : -1);
        }

        private static void AddArm(Junction junction, SilkBridge bridge, float coordinate, int direction)
        {
            if (junction.Arms.Exists(a => a.Bridge == bridge && a.Direction == direction && Mathf.Abs(a.Coordinate - coordinate) < 0.001f)) return;
            junction.Arms.Add(new Arm { Bridge = bridge, Coordinate = coordinate, Direction = direction,
                EndCoordinate = direction > 0 ? bridge.SegmentCount : 0f });
        }

        internal void Draw(List<SilkBridge> bridges, RoomCamera camera, float timeStacker)
        {
            bool rebuild = members.Count != bridges.Count;
            for (int i = 0; !rebuild && i < bridges.Count; i++) rebuild = members[i] != bridges[i];
            if (rebuild)
            {
                members.Clear(); members.AddRange(bridges); junctions.Clear();
                foreach (SilkBridge bridge in bridges)
                {
                    AddAnchor(bridge, bridge.startAnchor, true);
                    AddAnchor(bridge, bridge.endAnchor, false);
                }
                // A parent's two sides are separate local branches, ending at
                // the next knot rather than reaching through nearby junctions.
                foreach (Junction junction in junctions)
                    foreach (Arm arm in junction.Arms)
                        foreach (Junction other in junctions)
                        {
                            if (other == junction || other.Parent != arm.Bridge) continue;
                            float coordinate = other.CoordinateKey / 4096f;
                            if ((coordinate - arm.Coordinate) * arm.Direction > 0.001f &&
                                Mathf.Abs(coordinate - arm.Coordinate) < Mathf.Abs(arm.EndCoordinate - arm.Coordinate))
                                arm.EndCoordinate = coordinate;
                        }
            }
            drawn = 0;
            foreach (Junction junction in junctions)
            {
                if (!junction.Parent.IsActive) continue;
                bindingPoints.Clear();
                for (int i = 0; i < junction.Arms.Count; i++)
                {
                    Arm arm = junction.Arms[i];
                    if (!arm.Bridge.IsActive) continue;
                    bindingPoints.Add(arm.BindingPoint(junction.Offsets[i % 4], timeStacker));
                }
                // Webbed walks its stored attachment list; it doesn't build
                // three nested rings or sort a regular fan around each sector.
                int edges = SilkJunctionPattern.EdgeCount(bindingPoints.Count);
                for (int i = 0; i < edges; i++)
                    DrawBinding(bindingPoints[i], bindingPoints[(i + 1) % edges], camera);
            }
            ClearUnused(mesh, drawn, Capacity);
            mesh.isVisible = drawn > 0;
        }

        private void DrawBinding(Vector2 a, Vector2 b, RoomCamera camera)
        {
            if (drawn >= Capacity || (a - b).sqrMagnitude < 2f) return;
            if (camera.room.GetTile(a).Solid || camera.room.GetTile(b).Solid ||
                SharedPhysics.RayTraceTilesForTerrainReturnFirstSolid(camera.room, a, b).HasValue) return;
            DrawLine(mesh, drawn++, a, b, 1f, camera.pos);
        }

        internal void Destroy() => mesh.RemoveFromContainer();
    }
}
