using RWCustom;
using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace Tinker.Silk.Bridge
{
    public static class SilkBridgeGraphics
    {
        private sealed class CameraRenderers
        {
            public Room room;
            public readonly List<BridgeRenderer> bridges = new();
            public readonly Dictionary<Player, AnimatedSilkRenderer> animated = new();

            public void Clear()
            {
                foreach (var renderer in bridges) renderer.Destroy();
                foreach (var renderer in animated.Values) renderer.Destroy();
                bridges.Clear();
                animated.Clear();
                room = null;
            }
        }

        private static ConditionalWeakTable<RoomCamera, CameraRenderers> cameraRenderers = new();
        private static readonly List<WeakReference<CameraRenderers>> trackedRenderers = new();
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized) return;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
            On.Room.Unloaded += Room_Unloaded;
            On.RainWorldGame.ShutDownProcess += Game_ShutDownProcess;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.Room.Unloaded -= Room_Unloaded;
            On.RainWorldGame.ShutDownProcess -= Game_ShutDownProcess;
            ClearRenderers();
            initialized = false;
        }

        private static void ClearRenderers()
        {
            foreach (var reference in trackedRenderers)
                if (reference.TryGetTarget(out var state)) state.Clear();
            trackedRenderers.Clear();
            cameraRenderers = new();
        }

        private static void Game_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
        {
            ClearRenderers();
            orig(self);
        }

        private static void Room_Unloaded(On.Room.orig_Unloaded orig, Room self)
        {
            foreach (var reference in trackedRenderers)
                if (reference.TryGetTarget(out var state) && state.room == self) state.Clear();
            orig(self);
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            var state = cameraRenderers.GetValue(self, camera =>
            {
                var value = new CameraRenderers();
                trackedRenderers.RemoveAll(reference => !reference.TryGetTarget(out _));
                trackedRenderers.Add(new WeakReference<CameraRenderers>(value));
                return value;
            });
            if (state.room != self.room)
            {
                state.Clear();
                state.room = self.room;
            }
            if (self.room == null) return;

            List<SilkBridge> bridges = SilkBridgeManager.GetBridgesInRoom(self.room);
            var renderers = state.bridges;
            while (renderers.Count > bridges.Count)
            {
                int lastIndex = renderers.Count - 1;
                renderers[lastIndex].Destroy();
                renderers.RemoveAt(lastIndex);
            }
            while (renderers.Count < bridges.Count)
                renderers.Add(new BridgeRenderer(self));
            for (int i = 0; i < bridges.Count; i++)
                renderers[i].Draw(bridges[i], self, timeStacker);

            foreach (var player in state.animated.Keys.Where(player => player.slatedForDeletetion || player.room != self.room).ToArray())
            {
                state.animated[player].Destroy();
                state.animated.Remove(player);
            }
            foreach (var creature in self.room.game.Players)
            {
                if (creature?.realizedCreature is not Player player || player.room != self.room) continue;
                var bridgeState = SilkBridgeManager.GetBridgeModeState(player);
                if (bridgeState != null && (bridgeState.animating || bridgeState.virtualSilkActive))
                {
                    if (!state.animated.TryGetValue(player, out var renderer))
                        state.animated[player] = renderer = new AnimatedSilkRenderer(self);
                    renderer.Draw(bridgeState, player, self, timeStacker);
                }
                else if (state.animated.TryGetValue(player, out var renderer))
                {
                    renderer.Destroy();
                    state.animated.Remove(player);
                }
            }
        }

        private class BridgeRenderer
        {
            private TriangleMesh mesh;
            private const int MAX_SEGMENTS = 161;

            public BridgeRenderer(RoomCamera cam)
            {
                TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[(MAX_SEGMENTS - 1) * 2];
                for (int i = 0; i < MAX_SEGMENTS - 1; i++)
                {
                    int vertIndex = i * 4;
                    tris[i * 2] = new TriangleMesh.Triangle(vertIndex, vertIndex + 1, vertIndex + 2);
                    tris[i * 2 + 1] = new TriangleMesh.Triangle(vertIndex + 1, vertIndex + 2, vertIndex + 3);
                }
                mesh = new TriangleMesh("Futile_White", tris, false, false);
                mesh.color = new Color(0.95f, 0.95f, 0.95f, 1f);
                cam.ReturnFContainer("Midground").AddChild(mesh);
            }

            public void Draw(SilkBridge bridge, RoomCamera cam, float timeStacker)
            {
                if (bridge == null || bridge.room != cam.room)
                {
                    mesh.isVisible = false;
                    return;
                }

                Vector2 camPos = cam.pos;
                mesh.isVisible = true;

                float currentDist = Vector2.Distance(bridge.startPoint, bridge.endPoint);
                int edges = bridge.VisualEdgeCount;
                int subdivisions = Mathf.Clamp(Mathf.CeilToInt(currentDist / (6f * edges)), 1, Mathf.Max(1, (MAX_SEGMENTS - 1) / edges));
                int segmentCount = Mathf.Min(edges * subdivisions, MAX_SEGMENTS - 1);
                float stretchFactor = Mathf.Clamp01(currentDist / 600f);
                float baseWidth = Mathf.Lerp(2f, 1.3f, stretchFactor);

                for (int i = 0; i < segmentCount; i++)
                {
                    Vector2 segStart = bridge.GetVisualPoint(bridge.GetVisualParameter((float)i / segmentCount), timeStacker);
                    Vector2 segEnd = bridge.GetVisualPoint(bridge.GetVisualParameter((float)(i + 1) / segmentCount), timeStacker);
                    Vector2 segDir = (segEnd - segStart).normalized;
                    Vector2 perpendicular = Custom.PerpendicularVector(segDir);

                    float t = (float)i / segmentCount;
                    float width = baseWidth * (1f - Mathf.Abs(t * 2f - 1f) * 0.15f);

                    int vertIndex = i * 4;
                    mesh.MoveVertice(vertIndex, segStart - perpendicular * width * 0.5f - camPos);
                    mesh.MoveVertice(vertIndex + 1, segStart + perpendicular * width * 0.5f - camPos);
                    mesh.MoveVertice(vertIndex + 2, segEnd - perpendicular * width * 0.5f - camPos);
                    mesh.MoveVertice(vertIndex + 3, segEnd + perpendicular * width * 0.5f - camPos);
                }

                for (int i = segmentCount; i < MAX_SEGMENTS - 1; i++)
                {
                    int v = i * 4;
                    mesh.MoveVertice(v, Vector2.zero); mesh.MoveVertice(v + 1, Vector2.zero);
                    mesh.MoveVertice(v + 2, Vector2.zero); mesh.MoveVertice(v + 3, Vector2.zero);
                }
            }

            public void SetVisible(bool visible) { if (mesh != null) mesh.isVisible = visible; }
            public void Destroy() { if (mesh != null) { mesh.RemoveFromContainer(); mesh = null; } }
        }

        private class AnimatedSilkRenderer
        {
            private TriangleMesh lineMesh;
            private const int MAX_SEGMENTS = 121;

            public AnimatedSilkRenderer(RoomCamera cam)
            {
                TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[(MAX_SEGMENTS - 1) * 2];
                for (int i = 0; i < MAX_SEGMENTS - 1; i++)
                {
                    int vertIndex = i * 4;
                    tris[i * 2] = new TriangleMesh.Triangle(vertIndex, vertIndex + 1, vertIndex + 2);
                    tris[i * 2 + 1] = new TriangleMesh.Triangle(vertIndex + 1, vertIndex + 2, vertIndex + 3);
                }
                lineMesh = new TriangleMesh("Futile_White", tris, false, false);
                lineMesh.color = new Color(1f, 1f, 1f, 1f);
                cam.ReturnFContainer("Midground").AddChild(lineMesh);
            }

            public void Draw(BridgeModeState bridgeState, Player p, RoomCamera cam, float timeStacker)
            {
                Vector2 startPos = bridgeState.GetRenderD1Position(timeStacker);
                Vector2 endPos = bridgeState.point2;
                float distance = Vector2.Distance(startPos, endPos);

                if (distance < 1f) { lineMesh.isVisible = false; return; }
                lineMesh.isVisible = true;

                int segmentCount = Mathf.Min(Mathf.CeilToInt(distance / 6f), MAX_SEGMENTS - 1);
                segmentCount = Mathf.Max(segmentCount, 2);

                Vector2 camPos = cam.pos;
                float baseWidth = Mathf.Lerp(2f, 1.3f, Mathf.Clamp01(distance / 600f));

                for (int i = 0; i < segmentCount; i++)
                {
                    float t = (float)i / segmentCount;
                    float nextT = (float)(i + 1) / segmentCount;

                    Vector2 p1 = bridgeState.GetLaunchVisualPoint(t, timeStacker);
                    Vector2 p2 = bridgeState.GetLaunchVisualPoint(nextT, timeStacker);
                    Vector2 perp = Custom.PerpendicularVector((p2 - p1).normalized);
                    float width = baseWidth * (1f - Mathf.Abs(t * 2f - 1f) * 0.15f);

                    int v = i * 4;
                    lineMesh.MoveVertice(v, p1 - perp * width * 0.5f - camPos);
                    lineMesh.MoveVertice(v + 1, p1 + perp * width * 0.5f - camPos);
                    lineMesh.MoveVertice(v + 2, p2 - perp * width * 0.5f - camPos);
                    lineMesh.MoveVertice(v + 3, p2 + perp * width * 0.5f - camPos);
                }

                for (int i = segmentCount; i < MAX_SEGMENTS - 1; i++)
                {
                    int v = i * 4;
                    lineMesh.MoveVertice(v, Vector2.zero); lineMesh.MoveVertice(v + 1, Vector2.zero);
                    lineMesh.MoveVertice(v + 2, Vector2.zero); lineMesh.MoveVertice(v + 3, Vector2.zero);
                }
            }

            public void Destroy() { if (lineMesh != null) { lineMesh.RemoveFromContainer(); lineMesh = null; } }
        }
    }
}
