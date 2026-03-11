using RWCustom;
using System.Collections.Generic;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace Tinker.Silk.Bridge
{
    public static class BrokenSilkManager
    {
        private static List<BrokenSilkAnimation> animations = new List<BrokenSilkAnimation>();
        private static bool initialized = false;

        public static void Initialize()
        {
            if (initialized) return;
            On.Room.Update += Room_Update;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
            On.Room.Unloaded += Room_Unloaded;
            On.Creature.Update += Creature_Update;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.Room.Update -= Room_Update;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.Room.Unloaded -= Room_Unloaded;
            On.Creature.Update -= Creature_Update;

            foreach (var anim in animations)
            {
                anim.Destroy();
            }
            animations.Clear();
            initialized = false;
        }

        private static void Creature_Update(On.Creature.orig_Update orig, Creature self, bool eu)
        {
            orig(self, eu);

            if (self == null || self is Player || self.room == null || self.slatedForDeletetion)
            {
                return;
            }

            var bridges = SilkBridgeManager.GetBridgesInRoom(self.room);
            if (bridges == null || bridges.Count == 0)
            {
                return;
            }

            foreach (var chunk in self.bodyChunks)
            {
                foreach (var bridge in bridges)
                {
                    if (!bridge.IsActive) continue;

                    var bridgePath = bridge.GetRenderPath();
                    if (bridgePath.Count < 2) continue;

                    for (int i = 0; i < bridgePath.Count - 1; i++)
                    {
                        Vector2 segStart = bridgePath[i];
                        Vector2 segEnd = bridgePath[i + 1];

                        if (SilkBridgeManager.SegmentIntersection(chunk.lastPos, chunk.pos, segStart, segEnd, out Vector2 intersection, out _))
                        {
                            Vector2 moveDir = (chunk.pos - chunk.lastPos).normalized;
                            float impactSpeed = chunk.vel.magnitude;
                            float damage = impactSpeed * chunk.mass * 0.6f;

                            bridge.TakeDamage(damage, intersection);
                            Vector2 forceOnBridge = moveDir * impactSpeed * chunk.mass * 1.5f;
                            bridge.ApplyForceAt(intersection, forceOnBridge, 30f);

                            self.room.PlaySound(SoundID.Big_Needle_Worm_Impale_Terrain, chunk.pos, 0.1f, 1.5f);
                            goto next_creature_loop;
                        }
                    }
                }
            }
        next_creature_loop:;
        }

        private static void Room_Update(On.Room.orig_Update orig, Room self)
        {
            orig(self);

            for (int i = animations.Count - 1; i >= 0; i--)
            {
                var anim = animations[i];
                if (anim.Room != self) continue;

                anim.Update();
                if (anim.IsFinished)
                {
                    anim.Destroy();
                    animations.RemoveAt(i);
                }
            }

            Room_WeaponSilkCheck(self);
        }

        private static void Room_WeaponSilkCheck(Room self)
        {
            var bridges = SilkBridgeManager.GetBridgesInRoom(self);
            if (bridges == null || bridges.Count == 0) return;

            foreach (var obj in self.physicalObjects)
            {
                foreach (var phys in obj)
                {
                    if (phys is Weapon weapon && !weapon.slatedForDeletetion)
                    {
                        if (weapon.thrownBy == null || weapon.mode != Weapon.Mode.Thrown) continue;

                        var chunk = weapon.firstChunk;
                        foreach (var bridge in bridges)
                        {
                            if (!bridge.IsActive) continue;
                            var path = bridge.GetRenderPath();
                            if (path.Count < 2) continue;

                            for (int i = 0; i < path.Count - 1; i++)
                            {
                                if (SilkBridgeManager.SegmentIntersection(chunk.lastPos, chunk.pos, path[i], path[i + 1], out Vector2 intersection, out _))
                                {
                                    bridge.TakeDamage(bridge.health + 1f, intersection);
                                    TriggerBreakAnimation(path, self, intersection);
                                    goto next_weapon;
                                }
                            }
                        }
                    }
                next_weapon:;
                }
            }
        }

        private static void Room_Unloaded(On.Room.orig_Unloaded orig, Room self)
        {
            orig(self);
            animations.RemoveAll(anim =>
            {
                if (anim.Room == self)
                {
                    anim.Destroy();
                    return true;
                }
                return false;
            });
        }

        private static void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            foreach (var anim in animations)
            {
                if (anim.Room == self.room)
                {
                    anim.Draw(self, timeStacker);
                }
            }
        }

        public static void TriggerBreakAnimation(List<Vector2> path, Room room, Vector2 breakPoint)
        {
            if (path == null || path.Count < 2 || room == null) return;

            float bestDist = float.MaxValue;
            int breakSegmentIndex = -1;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float dist = Custom.DistanceToLine(breakPoint, path[i], path[i + 1]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    breakSegmentIndex = i;
                }
            }

            if (breakSegmentIndex == -1) return;

            List<Vector2> path1 = new List<Vector2>();
            for (int i = 0; i <= breakSegmentIndex; i++) path1.Add(path[i]);
            path1.Add(breakPoint);

            List<Vector2> path2 = new List<Vector2>();
            path2.Add(breakPoint);
            for (int i = breakSegmentIndex + 1; i < path.Count; i++) path2.Add(path[i]);

            if (path1.Count >= 2) animations.Add(new BrokenSilkAnimation(path1, room));
            if (path2.Count >= 2) animations.Add(new BrokenSilkAnimation(path2, room));
        }
    }

    internal class BrokenSilkAnimation
    {
        public Room Room { get; private set; }
        public bool IsFinished => fadeAlpha <= 0f;

        private TriangleMesh lineMesh;
        private Vector2[] positions;
        private Vector2[] lastPositions;
        private float fadeAlpha = 1f;
        private float segmentLength;
        private Color silkColor;

        private const int RENDER_SEGMENTS = 20;
        private const float FADE_ALPHA_DECAY = 0.015f;
        private const float FADE_GRAVITY = 0.8f;
        private const float FADE_FRICTION = 0.92f;
        private const int PHYSICS_ITERATIONS = 4;

        public BrokenSilkAnimation(List<Vector2> path, Room room)
        {
            this.Room = room;
            this.silkColor = new Color(0.9f, 0.9f, 0.9f);
            positions = new Vector2[RENDER_SEGMENTS];
            lastPositions = new Vector2[RENDER_SEGMENTS];

            for (int i = 0; i < RENDER_SEGMENTS; i++)
            {
                float t = (float)i / (RENDER_SEGMENTS - 1);
                Vector2 pos = GetPathPoint(path, t);
                positions[i] = pos;
                lastPositions[i] = pos - new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(-0.5f, 1f));
            }

            segmentLength = Vector2.Distance(positions[0], positions[1]);
            lineMesh = TriangleMesh.MakeLongMesh(RENDER_SEGMENTS, false, true);
            lineMesh.shader = room.game.rainWorld.Shaders["Basic"];
        }

        private Vector2 GetPathPoint(List<Vector2> path, float t)
        {
            float sourceIndexF = t * (path.Count - 1);
            int idxA = Mathf.FloorToInt(sourceIndexF);
            int idxB = Mathf.Min(path.Count - 1, idxA + 1);
            float localT = sourceIndexF - idxA;
            return Vector2.Lerp(path[idxA], path[idxB], localT);
        }

        public void Update()
        {
            if (IsFinished || Room == null) return;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector2 vel = (positions[i] - lastPositions[i]) * FADE_FRICTION;
                lastPositions[i] = positions[i];
                positions[i] += vel;
                positions[i].y -= FADE_GRAVITY;
            }
            for (int iter = 0; iter < PHYSICS_ITERATIONS; iter++)
            {
                for (int i = 0; i < positions.Length - 1; i++)
                {
                    float d = Vector2.Distance(positions[i], positions[i + 1]);
                    if (d > 0.1f)
                    {
                        float diff = (segmentLength - d) / d;
                        Vector2 offset = (positions[i] - positions[i + 1]) * diff * 0.5f;
                        positions[i] += offset;
                        positions[i + 1] -= offset;
                    }
                }
            }
            fadeAlpha -= FADE_ALPHA_DECAY;
        }

        public void Draw(RoomCamera rCam, float timeStacker)
        {
            if (IsFinished || rCam.room != this.Room)
            {
                Destroy();
                return;
            }
            if (lineMesh.container == null) rCam.ReturnFContainer("Midground").AddChild(lineMesh);
            lineMesh.isVisible = true;
            Vector2 camPos = rCam.pos;
            float baseWidth = 1.2f * Mathf.InverseLerp(0f, 0.3f, fadeAlpha);
            Vector2 lastP = Vector2.Lerp(lastPositions[0], positions[0], timeStacker);
            float lastWidth = 0f;
            for (int i = 0; i < RENDER_SEGMENTS; i++)
            {
                float t = (float)i / (RENDER_SEGMENTS - 1);
                Vector2 currentP = Vector2.Lerp(lastPositions[i], positions[i], timeStacker);
                float segmentWidth = baseWidth * (1f - Mathf.Abs(t * 2f - 1f) * 0.2f);
                Vector2 dir = (currentP - lastP).normalized;
                if (dir.magnitude < 0.001f) dir = Vector2.up;
                Vector2 perp = Custom.PerpendicularVector(dir);
                int v = i * 4;
                Vector2 hA = perp * ((segmentWidth + lastWidth) * 0.5f);
                Vector2 hB = perp * segmentWidth;
                lineMesh.MoveVertice(v, (lastP + currentP) / 2f - hA - camPos);
                lineMesh.MoveVertice(v + 1, (lastP + currentP) / 2f + hA - camPos);
                lineMesh.MoveVertice(v + 2, currentP - hB - camPos);
                lineMesh.MoveVertice(v + 3, currentP + hB - camPos);
                for (int j = 0; j < 4; j++) lineMesh.verticeColors[v + j] = silkColor;
                lastP = currentP;
                lastWidth = segmentWidth;
            }
            lineMesh.alpha = fadeAlpha;
        }

        public void Destroy()
        {
            if (lineMesh != null)
            {
                lineMesh.RemoveFromContainer();
                lineMesh = null;
            }
        }
    }
}