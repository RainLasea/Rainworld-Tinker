using RWCustom;
using System.Collections.Generic;
using UnityEngine;

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
            On.Room.Update += Room_WeaponSilkCheck;
            On.Creature.Update += Creature_Update;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.Room.Update -= Room_Update;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.Room.Unloaded -= Room_Unloaded;
            On.Room.Update -= Room_WeaponSilkCheck;
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
                            Vector2 segDir = (segEnd - segStart).normalized;
                            Vector2 normal = Custom.PerpendicularVector(segDir);

                            if (Vector2.Dot(normal, moveDir) < 0)
                            {
                                normal = -normal;
                            }

                            chunk.pos = intersection - moveDir * (chunk.rad * 0.5f + 1f);

                            float impactSpeed = chunk.vel.magnitude;
                            chunk.vel = Vector2.Reflect(chunk.vel, normal) * 0.3f;

                            float damage = impactSpeed * chunk.mass * 0.6f;
                            bridge.TakeDamage(damage, intersection);

                            Vector2 forceOnBridge = moveDir * impactSpeed * chunk.mass * 1.5f;
                            bridge.ApplyForceAt(intersection, forceOnBridge);

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
            for (int i = 0; i <= breakSegmentIndex; i++)
            {
                path1.Add(path[i]);
            }
            path1.Add(breakPoint);

            List<Vector2> path2 = new List<Vector2>();
            path2.Add(breakPoint);
            for (int i = breakSegmentIndex + 1; i < path.Count; i++)
            {
                path2.Add(path[i]);
            }

            if (path1.Count >= 2)
                animations.Add(new BrokenSilkAnimation(path1, room));
            if (path2.Count >= 2)
                animations.Add(new BrokenSilkAnimation(path2, room));
        }

        private static void Room_WeaponSilkCheck(On.Room.orig_Update orig, Room self)
        {
            orig(self);

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
                                Vector2 segStart = path[i];
                                Vector2 segEnd = path[i + 1];

                                if (SilkBridgeManager.SegmentIntersection(chunk.lastPos, chunk.pos, segStart, segEnd, out Vector2 intersection, out _))
                                {

                                    bridge.TakeDamage(bridge.health + 1f, intersection);
                                    BrokenSilkManager.TriggerBreakAnimation(path, self, intersection);

                                    goto next_weapon;
                                }
                            }
                        }
                    }
                next_weapon:;
                }
            }
        }
    }

    internal class BrokenSilkAnimation
    {
        public Room Room { get; private set; }
        public bool IsFinished => fadeAlpha <= 0f;

        private TriangleMesh mesh;
        private Vector2[] positions;
        private Vector2[] lastPositions;
        private Vector2[] velocities;
        private float fadeAlpha;
        private float fadeThickness;
        private float fadeSwayPhase;

        private const int RENDER_SEGMENTS = 20;
        private const float FADE_ALPHA_DECAY = 0.025f;
        private const float FADE_THICKNESS_DECAY = 0.02f;
        private const float FADE_GRAVITY = 0.3f;

        public BrokenSilkAnimation(List<Vector2> path, Room room)
        {
            this.Room = room;

            positions = new Vector2[path.Count];
            lastPositions = new Vector2[path.Count];
            velocities = new Vector2[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                positions[i] = path[i];
                lastPositions[i] = path[i];
                velocities[i] = new Vector2((Random.value - 0.5f) * 2.5f, -Random.value * 2f);
            }

            fadeAlpha = 1f;
            fadeThickness = 1.2f;
            fadeSwayPhase = Random.value * Mathf.PI * 2f;

            TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[(RENDER_SEGMENTS - 1) * 2];
            for (int i = 0; i < RENDER_SEGMENTS - 1; i++)
            {
                int v = i * 4;
                tris[i * 2] = new TriangleMesh.Triangle(v, v + 1, v + 2);
                tris[i * 2 + 1] = new TriangleMesh.Triangle(v + 1, v + 2, v + 3);
            }
            mesh = new TriangleMesh("Futile_White", tris, false, false);
        }

        public void Update()
        {
            if (IsFinished) return;

            fadeSwayPhase += 0.1f;
            for (int i = 0; i < positions.Length; i++)
            {
                lastPositions[i] = positions[i];
                velocities[i].y -= FADE_GRAVITY;
                velocities[i].x *= 0.98f;
                velocities[i].x += Mathf.Sin(fadeSwayPhase + i * 0.5f) * 0.2f * fadeAlpha;
                positions[i] += velocities[i];
            }

            fadeAlpha -= FADE_ALPHA_DECAY;
            fadeThickness -= FADE_THICKNESS_DECAY;
        }

        public void Draw(RoomCamera rCam, float timeStacker)
        {
            if (IsFinished || rCam.room != this.Room)
            {
                if (mesh.container != null) mesh.RemoveFromContainer();
                return;
            }

            if (mesh.container == null)
                rCam.ReturnFContainer("Midground").AddChild(mesh);

            Vector2[] renderPoints = new Vector2[RENDER_SEGMENTS];
            for (int i = 0; i < RENDER_SEGMENTS; i++)
            {
                float t = (float)i / (RENDER_SEGMENTS - 1);
                float pathT = t * (positions.Length - 1);
                int idxA = Mathf.FloorToInt(pathT);
                int idxB = Mathf.Min(positions.Length - 1, idxA + 1);

                Vector2 posA = Vector2.Lerp(lastPositions[idxA], positions[idxA], timeStacker);
                Vector2 posB = Vector2.Lerp(lastPositions[idxB], positions[idxB], timeStacker);

                renderPoints[i] = Vector2.Lerp(posA, posB, pathT - idxA);
            }

            float width = 2.0f * fadeThickness;
            Vector2 camPos = rCam.pos;
            for (int i = 0; i < RENDER_SEGMENTS - 1; i++)
            {
                Vector2 start = renderPoints[i];
                Vector2 end = renderPoints[i + 1];
                if ((start - end).sqrMagnitude < 0.1f) continue;

                Vector2 dir = (end - start).normalized;
                Vector2 perp = Custom.PerpendicularVector(dir);

                int v = i * 4;
                mesh.MoveVertice(v, start - perp * width * 0.5f - camPos);
                mesh.MoveVertice(v + 1, start + perp * width * 0.5f - camPos);
                mesh.MoveVertice(v + 2, end - perp * width * 0.5f - camPos);
                mesh.MoveVertice(v + 3, end + perp * width * 0.5f - camPos);
            }

            mesh.alpha = fadeAlpha > 0 ? fadeAlpha : 0;
            mesh.color = Color.white;
        }

        public void Destroy()
        {
            if (mesh != null)
            {
                mesh.RemoveFromContainer();
                mesh = null;
            }
        }
    }
}