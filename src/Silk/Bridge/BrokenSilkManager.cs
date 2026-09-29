using System.Collections.Generic;
using tinker.Silk;
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
            On.RainWorldGame.ShutDownProcess += Game_ShutDownProcess;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.Room.Update -= Room_Update;
            On.RoomCamera.DrawUpdate -= RoomCamera_DrawUpdate;
            On.Room.Unloaded -= Room_Unloaded;
            On.Creature.Update -= Creature_Update;
            On.RainWorldGame.ShutDownProcess -= Game_ShutDownProcess;

            ClearAnimations();
            initialized = false;
        }

        private static void Game_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
        {
            ClearAnimations();
            orig(self);
        }

        private static void ClearAnimations()
        {
            foreach (var anim in animations)
            {
                anim.Destroy();
            }
            animations.Clear();
        }

        private static void Creature_Update(On.Creature.orig_Update orig, Creature self, bool eu)
        {
            orig(self, eu);

            if (self == null || self is Player || !tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(self) || self.room == null || self.slatedForDeletetion)
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
                    if (!bridge.IsActive || bridge.RenderPoints == null || bridge.RenderPoints.Length < 2) continue;

                    var renderPts = bridge.GetRenderPath();
                    for (int i = 0; i < renderPts.Count - 1; i++)
                    {
                        Vector2 segStart = renderPts[i];
                        Vector2 segEnd = renderPts[i + 1];

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
            // Preserve the game's exception handling and other mods' diagnostics.
            orig(self);

            try
            {
                if (self == null) return;
                for (int i = animations.Count - 1; i >= 0; i--)
                {
                    var anim = animations[i];
                    if (anim?.Room != self) continue;

                    anim.Update();
                    if (anim.IsFinished)
                    {
                        anim.Destroy();
                        animations.RemoveAt(i);
                    }
                }

                if (self.physicalObjects != null)
                {
                    Room_WeaponSilkCheck(self);
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[BrokenSilkManager] Room_Update error: {ex}");
            }
        }

        private static void Room_WeaponSilkCheck(Room self)
        {
            if (self.physicalObjects == null) return;
            var bridges = SilkBridgeManager.GetBridgesInRoom(self);
            if (bridges == null || bridges.Count == 0) return;

            foreach (var obj in self.physicalObjects)
            {
                foreach (var phys in obj)
                {
                    if (phys is Weapon weapon && !weapon.slatedForDeletetion &&
                        tinker.Silk.RainMeadow.RainMeadowBridge.CanSimulate(weapon))
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
            if (self == null) return;
            foreach (var anim in animations)
            {
                // Also detach this camera's old-room meshes after a room switch.
                anim.Draw(self, timeStacker);
            }
        }

        public static void TriggerFadeAnimation(List<Vector2> path, Room room, bool recoil = false, Color? color = null, float width = 1.2f)
        {
            AddAnimation(path, room, recoil, false, color ?? new Color(0.9f, 0.9f, 0.9f), width);
        }

        public static void TriggerBreakAnimation(List<Vector2> path, Room room, Vector2 breakPoint,
            BridgeAnchor startAnchor = null, BridgeAnchor endAnchor = null)
        {
            if (room == null || !SilkFade.Split(path, breakPoint, out var left, out var right)) return;
            float width = path.Count < 2 ? 1f : Mathf.Lerp(1f, 0.65f,
                Mathf.Clamp01(Vector2.Distance(path[0], path[path.Count - 1]) / 600f));
            AddAnimation(left, room, true, startAnchor == null || startAnchor.IsValid(room), SilkBridgeGraphics.MainSilkColor, width, startAnchor);
            AddAnimation(right, room, true, endAnchor == null || endAnchor.IsValid(room), SilkBridgeGraphics.MainSilkColor, width, endAnchor);
        }

        private static void AddAnimation(List<Vector2> path, Room room, bool recoil, bool pinStart, Color color, float width,
            BridgeAnchor anchor = null)
        {
            if (room == null) return;
            var strand = new SilkFade.Strand(path, recoil, pinStart);
            if (!strand.Finished) animations.Add(new BrokenSilkAnimation(strand, room, color, width, anchor));
        }
    }

    internal class BrokenSilkAnimation
    {
        public Room Room { get; }
        public bool IsFinished => destroyed || strand.Finished;
        private readonly SilkFade.Strand strand;
        private readonly Color silkColor;
        private readonly float width;
        private readonly BridgeAnchor anchor;
        private readonly Dictionary<RoomCamera, TriangleMesh> meshes = new();
        private bool destroyed;

        public BrokenSilkAnimation(SilkFade.Strand strand, Room room, Color color, float width, BridgeAnchor anchor)
        {
            this.strand = strand;
            Room = room;
            silkColor = color;
            this.width = width;
            this.anchor = anchor;
        }

        public void Update()
        {
            if (IsFinished || Room == null) return;
            bool validAnchor = anchor != null && anchor.IsValid(Room);
            if (anchor != null && !validAnchor) strand.ReleaseAnchor();
            bool pinned = strand.Pinned;
            strand.Update(Room.gravity, validAnchor ? anchor.GetWorldPosition() : (Vector2?)null);
            for (int i = pinned ? 1 : 0; i < strand.Positions.Length; i++)
            {
                var point = strand.Positions[i];
                if (Room.GetTile(point).Solid)
                {
                    var rect = Room.TileRect(Room.GetTilePosition(point));
                    float nearest = Mathf.Min(point.x - rect.left, rect.right - point.x, point.y - rect.bottom, rect.top - point.y);
                    if (nearest == point.x - rect.left) point.x = rect.left - 0.1f;
                    else if (nearest == rect.right - point.x) point.x = rect.right + 0.1f;
                    else if (nearest == point.y - rect.bottom) point.y = rect.bottom - 0.1f;
                    else point.y = rect.top + 0.1f;
                    strand.ResolveContact(i, point);
                }
            }
        }

        public void Draw(RoomCamera rCam, float timeStacker)
        {
            if (IsFinished || rCam.room != this.Room)
            {
                if (meshes.TryGetValue(rCam, out var oldMesh))
                {
                    oldMesh.RemoveFromContainer();
                    meshes.Remove(rCam);
                }
                return;
            }
            if (!meshes.TryGetValue(rCam, out var lineMesh))
            {
                lineMesh = SilkFadeMesh.Create(strand.Positions.Length);
                lineMesh.shader = rCam.game.rainWorld.Shaders["Basic"];
                lineMesh.color = silkColor;
                meshes.Add(rCam, lineMesh);
            }
            if (lineMesh.container == null) rCam.ReturnFContainer("Midground").AddChild(lineMesh);
            SilkFadeMesh.Draw(lineMesh, strand.Positions, strand.Previous, timeStacker, rCam.pos,
                width * Mathf.InverseLerp(0f, 0.3f, strand.Alpha));
            lineMesh.alpha = strand.Alpha;
        }

        public void Destroy()
        {
            destroyed = true;
            foreach (var mesh in meshes.Values) mesh.RemoveFromContainer();
            meshes.Clear();
        }
    }
}
