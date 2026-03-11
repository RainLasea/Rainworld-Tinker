using RWCustom;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace Tinker.Silk.Bridge
{
    public static class SilkBridgeGraphics
    {
        private static readonly Dictionary<Room, List<BridgeRenderer>> roomRenderers = new Dictionary<Room, List<BridgeRenderer>>();
        private static readonly Dictionary<Player, AnimatedSilkRenderer> animatedSilkRenderers = new Dictionary<Player, AnimatedSilkRenderer>();
        private static bool initialized = false;

        public static void Initialize()
        {
            if (initialized) return;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
            On.Room.Loaded += Room_Loaded;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.Room.Loaded -= Room_Loaded;

            foreach (var pair in roomRenderers)
                foreach (var renderer in pair.Value)
                    renderer.Destroy();
            roomRenderers.Clear();

            foreach (var renderer in animatedSilkRenderers.Values)
                renderer.Destroy();
            animatedSilkRenderers.Clear();

            initialized = false;
        }

        private static void Room_Loaded(On.Room.orig_Loaded orig, Room self)
        {
            orig(self);
            if (roomRenderers.ContainsKey(self))
            {
                foreach (var renderer in roomRenderers[self])
                    renderer.Destroy();
                roomRenderers[self].Clear();
            }
            else
                roomRenderers[self] = new List<BridgeRenderer>();
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            if (self.room == null) return;

            foreach (var pair in roomRenderers.Where(pair => pair.Key != self.room))
            {
                foreach (var renderer in pair.Value)
                    renderer.SetVisible(false);
            }

            List<SilkBridge> bridges = SilkBridgeManager.GetBridgesInRoom(self.room);
            if (!roomRenderers.ContainsKey(self.room))
                roomRenderers[self.room] = new List<BridgeRenderer>();
            List<BridgeRenderer> renderers = roomRenderers[self.room];

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

            foreach (var player in self.room.game.Players)
            {
                if (player?.realizedCreature is Player p)
                {
                    var bridgeState = SilkBridgeManager.GetBridgeModeState(p);
                    if (bridgeState != null && (bridgeState.animating || bridgeState.virtualSilkActive))
                    {
                        if (!animatedSilkRenderers.ContainsKey(p))
                            animatedSilkRenderers[p] = new AnimatedSilkRenderer(self);
                        animatedSilkRenderers[p].Draw(bridgeState, p, self, timeStacker);
                    }
                    else if (animatedSilkRenderers.ContainsKey(p))
                    {
                        animatedSilkRenderers[p].Destroy();
                        animatedSilkRenderers.Remove(p);
                    }
                }
            }
        }

        private class BridgeRenderer
        {
            private TriangleMesh mesh;
            private const int MAX_SEGMENTS = 60;

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

                List<Vector2> renderPath = bridge.GetRenderPath();
                int segmentCount = Mathf.Min(renderPath.Count - 1, MAX_SEGMENTS - 1);

                float currentDist = Vector2.Distance(bridge.startPoint, bridge.endPoint);
                float stretchFactor = Mathf.Clamp01(currentDist / 600f);
                float baseWidth = Mathf.Lerp(2.5f, 1.7f, stretchFactor);

                for (int i = 0; i < segmentCount; i++)
                {
                    Vector2 segStart = renderPath[i];
                    Vector2 segEnd = renderPath[i + 1];
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
            private const int MAX_SEGMENTS = 60;

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
                Vector2 startPos = bridgeState.GetRenderD1Position();
                Vector2 endPos = bridgeState.point2;
                float distance = Vector2.Distance(startPos, endPos);

                if (distance < 1f) { lineMesh.isVisible = false; return; }
                lineMesh.isVisible = true;

                int segmentCount = Mathf.Min(Mathf.CeilToInt(distance / 8f), MAX_SEGMENTS - 1);
                segmentCount = Mathf.Max(segmentCount, 2);

                Vector2 camPos = cam.pos;
                float playerVelMod = p.mainBodyChunk.vel.magnitude;
                float shakeIntensity = Mathf.Min(playerVelMod * 1.5f, 15f);

                float baseWidth = Mathf.Lerp(2.6f, 1.8f, Mathf.Clamp01(distance / 500f)); // 增加1f宽度

                for (int i = 0; i < segmentCount; i++)
                {
                    float t = (float)i / segmentCount;
                    float nextT = (float)(i + 1) / segmentCount;

                    Vector2 p1 = Vector2.Lerp(startPos, endPos, t);
                    Vector2 p2 = Vector2.Lerp(startPos, endPos, nextT);
                    Vector2 dir = (p2 - p1).normalized;
                    Vector2 perp = Custom.PerpendicularVector(dir);

                    float sinBase = Mathf.Sin(t * Mathf.PI * 8f + Time.time * 65f);
                    float offsetForce = sinBase * shakeIntensity * Mathf.Sin(t * Mathf.PI);

                    p1 += perp * offsetForce;
                    p2 += perp * (Mathf.Sin(nextT * Mathf.PI * 8f + Time.time * 65f) * shakeIntensity * Mathf.Sin(nextT * Mathf.PI));

                    float widthWave = sinBase * 0.6f * (shakeIntensity / 5f);
                    float width = (baseWidth + widthWave) * (1f - Mathf.Abs(t * 2f - 1f) * 0.15f);

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