using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using RWCustom;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk
{
    // The player owns locomotion. Silk supplies a continuous beam surface to
    // vanilla's animation/body-mode queries; it never writes a second jump.
    public static class SilkClimb
    {
        private sealed class ClimbState
        {
            public IClimbableSilk Target;
            public Room Room;
            public int Segment;
            public float T;
            public bool Vertical;
            public IClimbableSilk PreviousTarget;
            public Vector2 SwitchPoint;
            public Vector2 SwitchInput;
            public bool RouteInputActive;
            public int RouteSign;
            public Vector2[] RoutingPoints;
            public Vector2[] CandidatePoints;
            public float StepDistance;
            public bool WasMoving;
            public Vector2[] Points;
            public Vector2 LastSupport;
            public SilkClimbTransfer Transfer;
        }

        private static ConditionalWeakTable<Player, ClimbState> states = new();
        private static readonly FieldInfo noGrabCounter = typeof(Player).GetField("noGrabCounter",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool initialized;
        private const int SurfaceSubdivisions = 4;

        public static bool IsClimbing(Player player) => player != null && !RainMeadow.RainMeadowBridge.IsOnlineAndRemote(player) &&
            states.TryGetValue(player, out var state) && Valid(player, state);

        private static bool Valid(Player player, ClimbState state) => state.Target != null &&
            state.Target.IsActive && state.Target.SegmentCount > 0 && player.room != null &&
            player.room == state.Room && (!(state.Target is SilkBridge bridge) || bridge.room == player.room);

        internal static bool BeamAnimation(Player.AnimationIndex animation) =>
            animation == Player.AnimationIndex.ClimbOnBeam || animation == Player.AnimationIndex.HangFromBeam ||
            animation == Player.AnimationIndex.StandOnBeam || animation == Player.AnimationIndex.GetUpOnBeam ||
            animation == Player.AnimationIndex.GetUpToBeamTip || animation == Player.AnimationIndex.BeamTip ||
            animation == Player.AnimationIndex.HangUnderVerticalBeam;

        public static void Init()
        {
            if (initialized) return;
            SilkBeamAdapter.Init();
            On.Player.Update += Player_Update;
            On.Player.UpdateAnimation += Player_UpdateAnimation;
            On.Player.UpdateBodyMode += Player_UpdateBodyMode;
            On.Player.Jump += Player_Jump;
            On.Player.Die += Player_Die;
            initialized = true;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.Player.Update -= Player_Update;
            On.Player.UpdateAnimation -= Player_UpdateAnimation;
            On.Player.UpdateBodyMode -= Player_UpdateBodyMode;
            On.Player.Jump -= Player_Jump;
            On.Player.Die -= Player_Die;
            SilkBeamAdapter.Cleanup();
            states = new ConditionalWeakTable<Player, ClimbState>();
            initialized = false;
        }

        public static void AttachPlayerToSilk(Player player, IClimbableSilk silk, int seg, float t)
        {
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(player) || player?.room == null || !player.Consious || silk == null || !silk.IsActive ||
                silk.SegmentCount < 1 || (int)noGrabCounter.GetValue(player) > 0 || IsClimbing(player)) return;
            if (silk is SilkBridge otherRoom && otherRoom.room != player.room) return;
            seg = Mathf.Clamp(seg, 0, silk.SegmentCount - 1);
            t = Mathf.Clamp01(t);
            Vector2 point = silk.GetPointOnSegment(seg, t);
            if (!player.room.VisualContact(player.mainBodyChunk.pos, point)) return;
            var state = states.GetOrCreateValue(player);
            state.Target = silk;
            state.Room = player.room;
            state.Segment = seg;
            state.T = t;
            state.Vertical = VerticalAt(silk, seg, false);
            state.PreviousTarget = null;
            state.RouteInputActive = false;
            state.Transfer = default;
            state.StepDistance = 0f;
            state.WasMoving = false;
            state.LastSupport = point;
            UpdateSurface(state);
            if (silk is SilkBridge bridge)
                bridge.ExciteMotion(seg, t, player.mainBodyChunk.vel * player.TotalMass * 1.1f);
            player.bodyMode = Player.BodyModeIndex.ClimbingOnBeam;
            // Vanilla's beam grab paths keep animationFrame. Its setter is
            // protected in the running game, despite the public reference facade.
            player.standing = true;
            if (state.Vertical)
            {
                player.animation = Player.AnimationIndex.ClimbOnBeam;
                player.flipDirection = player.mainBodyChunk.pos.x < point.x ? -1 : 1;
                player.mainBodyChunk.vel = Vector2.zero;
                player.mainBodyChunk.pos.x = point.x;
            }
            else
            {
                // Grabbing from below starts hanging, exactly as a horizontal pole.
                player.animation = Player.AnimationIndex.HangFromBeam;
                player.mainBodyChunk.vel.y = 0f;
                player.bodyChunks[1].vel.y *= 0.25f;
                player.mainBodyChunk.pos.y = point.y;
            }
            player.room.PlaySound(SoundID.Slugcat_Grab_Beam, player.mainBodyChunk);
        }

        public static void DetachPlayerFromSilk(Player player) => Release(player, true, 10);

        private static void Release(Player player, bool clearAnimation, int grabDelay)
        {
            if (player == null || !states.TryGetValue(player, out var state) || state.Target == null) return;
            state.Target = null;
            state.PreviousTarget = null;
            state.Room = null;
            state.Transfer = default;
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(player)) return;
            noGrabCounter.SetValue(player, Math.Max((int)noGrabCounter.GetValue(player), grabDelay));
            if (clearAnimation && BeamAnimation(player.animation)) player.animation = Player.AnimationIndex.None;
            if (player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam && !BeamAnimation(player.animation))
                player.bodyMode = Player.BodyModeIndex.Default;
        }

        private static void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self))
            {
                states.Remove(self);
                orig(self, eu);
                return;
            }
            if (states.TryGetValue(self, out var state) && state.Target != null &&
                (!Valid(self, state) || !self.Consious || self.enteringShortCut.HasValue || self.inShortcut || self.grabbedBy.Count > 0))
                DetachPlayerFromSilk(self);
            orig(self, eu);
            if (states.TryGetValue(self, out state) && state.Target != null &&
                (!Valid(self, state) || !self.Consious || self.enteringShortCut.HasValue || self.inShortcut || !BeamAnimation(self.animation)))
                DetachPlayerFromSilk(self);
        }

        private static void Player_Die(On.Player.orig_Die orig, Player self)
        {
            DetachPlayerFromSilk(self);
            orig(self);
        }

        private static void Player_UpdateAnimation(On.Player.orig_UpdateAnimation orig, Player self)
        {
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self))
            {
                states.Remove(self);
                orig(self);
                return;
            }
            if (!states.TryGetValue(self, out var state) || !Valid(self, state) || !self.Consious || !BeamAnimation(self.animation))
            {
                Release(self, false, 10);
                orig(self);
                return;
            }
            UpdateSurface(state);
            if (!FollowSupport(self, state))
            {
                DetachPlayerFromSilk(self);
                orig(self);
                return;
            }
            RefreshContact(self, state);
            TrySwitch(self, state);
            bool vertical = VerticalAt(state.Target, state.Segment, state.Vertical);
            // Hysteresis uses the load-bearing curve, not rapidly changing ripple slopes.
            if (!state.Transfer.Active && vertical != state.Vertical && (self.animation == Player.AnimationIndex.ClimbOnBeam ||
                self.animation == Player.AnimationIndex.StandOnBeam || self.animation == Player.AnimationIndex.HangFromBeam))
            {
                state.Vertical = vertical;
                self.animation = vertical ? Player.AnimationIndex.ClimbOnBeam : Player.AnimationIndex.HangFromBeam;
            }
            var before = self.animation;
            var raw = self.input[0];
            try
            {
                MapRouteInput(self, state);
                orig(self);
            }
            finally { self.input[0] = raw; }
            if (OnNativeBeam(self))
            {
                Release(self, false, 0);
                return;
            }
            if (!BeamAnimation(self.animation))
            {
                if (before == Player.AnimationIndex.HangUnderVerticalBeam && self.input[0].jmp && !self.input[1].jmp)
                    ExciteJump(self, state);
                Release(self, false, 10);
            }
        }

        private static bool OnNativeBeam(Player player)
        {
            var tile = player.room.GetTile(Contact(player));
            return (player.animation == Player.AnimationIndex.ClimbOnBeam && tile.verticalBeam) ||
                ((player.animation == Player.AnimationIndex.StandOnBeam || player.animation == Player.AnimationIndex.HangFromBeam) && tile.horizontalBeam);
        }

        private static void Player_UpdateBodyMode(On.Player.orig_UpdateBodyMode orig, Player self)
        {
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self))
            {
                states.Remove(self);
                orig(self);
                return;
            }
            if (!BeamAnimation(self.animation)) Release(self, false, 10);
            var raw = self.input[0];
            try
            {
                if (states.TryGetValue(self, out var attached) && Valid(self, attached)) MapRouteInput(self, attached);
                orig(self);
            }
            finally { self.input[0] = raw; }
            if (!states.TryGetValue(self, out var state) || !Valid(self, state)) return;
            if (!BeamAnimation(self.animation) || self.bodyMode != Player.BodyModeIndex.ClimbingOnBeam)
            {
                Release(self, false, 10);
                return;
            }
            RefreshContact(self, state);
            Vector2 point = state.Target.GetPointOnSegment(state.Segment, state.T);
            state.LastSupport = point;
            state.Target.ApplyClimbForce(point, Vector2.down * self.gravity * self.TotalMass * 1.2f);
            if (state.Target is SilkBridge bridge)
            {
                // Count travel on the control axis, not the normal ripple displacement.
                int chunk = self.animation == Player.AnimationIndex.StandOnBeam ? 1 : 0;
                Vector2 travel = self.bodyChunks[chunk].pos - self.bodyChunks[chunk].lastPos;
                float distance = Mathf.Abs(state.Vertical ? travel.y : travel.x);
                bool moving = distance > 0.1f && (state.RouteInputActive ||
                    (state.Vertical ? self.input[0].y != 0 : self.input[0].x != 0));
                state.StepDistance += moving ? distance : 0f;
                if (moving && (!state.WasMoving || state.StepDistance >= 6f))
                {
                    state.StepDistance = state.WasMoving ? state.StepDistance % 6f : 0f;
                    Vector2 press = state.Vertical ? Custom.PerpendicularVector(Tangent(state.Target, state.Segment)) : Vector2.down;
                    bridge.ExciteMotion(state.Segment, state.T, press * self.TotalMass *
                        Mathf.Clamp(1.4f + distance * 0.9f, 1.4f, 3.2f), gentle: true);
                }
                state.WasMoving = moving;
            }
        }

        private static void Player_Jump(On.Player.orig_Jump orig, Player self)
        {
            if (RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self))
            {
                states.Remove(self);
                orig(self);
                return;
            }
            bool attached = states.TryGetValue(self, out var state) && Valid(self, state) && BeamAnimation(self.animation);
            orig(self);
            if (!attached) return;
            ExciteJump(self, state);
            // Up+jump on a vertical pole keeps ClimbOnBeam and sets slideUpPole=17.
            // All other departures keep the exact velocities/boost assigned by Jump().
            if (!BeamAnimation(self.animation)) Release(self, false, 10);
        }

        private static void ExciteJump(Player player, ClimbState state)
        {
            if (state.Target is SilkBridge bridge)
            {
                Vector2 support = bridge.GetVelocityOnSegment(state.Segment, state.T);
                bridge.ExciteMotion(state.Segment, state.T, (support - player.mainBodyChunk.vel) * player.TotalMass * 1.35f);
            }
        }

        private static Vector2 Tangent(IClimbableSilk silk, int segment)
        {
            // Average a few material segments so small kinks cannot change posture.
            int lo = Math.Max(0, segment - 1), hi = Math.Min(silk.SegmentCount - 1, segment + 1);
            return silk is SilkBridge bridge
                ? bridge.GetBasePoint(hi, 1f) - bridge.GetBasePoint(lo, 0f)
                : silk.GetPointOnSegment(hi, 1f) - silk.GetPointOnSegment(lo, 0f);
        }

        private static bool VerticalAt(IClimbableSilk silk, int segment, bool wasVertical)
        {
            Vector2 tangent = Tangent(silk, segment);
            return Mathf.Abs(tangent.y) > Mathf.Abs(tangent.x) * (wasVertical ? 0.65f : 0.85f);
        }

        private static Vector2 Contact(Player player) => player.bodyChunks[
            player.animation == Player.AnimationIndex.StandOnBeam || player.animation == Player.AnimationIndex.BeamTip ? 1 : 0].pos;

        private static void RefreshContact(Player player, ClimbState state)
        {
            if (TrySample(player, Contact(player), state.Vertical, out var sample))
            {
                state.Segment = sample.Segment;
                state.T = sample.T;
            }
        }

        private static void TrySwitch(Player player, ClimbState state)
        {
            if (player.input[0].jmp || state.Transfer.Active) return;
            Vector2 input = new Vector2(player.input[0].x, player.input[0].y);
            if (input.sqrMagnitude < 0.01f) return;
            // Leave vanilla's get-up transitions alone until a stable grip exists.
            if (player.animation != Player.AnimationIndex.ClimbOnBeam &&
                player.animation != Player.AnimationIndex.StandOnBeam &&
                player.animation != Player.AnimationIndex.HangFromBeam &&
                player.animation != Player.AnimationIndex.BeamTip &&
                player.animation != Player.AnimationIndex.HangUnderVerticalBeam) return;
            Vector2 contact = BasePoint(state.Target, state.Segment, state.T);
            if ((contact - state.SwitchPoint).sqrMagnitude > 16f * 16f ||
                Vector2.Dot(input.normalized, state.SwitchInput) < 0.5f)
                state.PreviousTarget = null;
            UpdateRoutingSurface(state.Target, ref state.RoutingPoints);
            SilkBridge selected = null;
            var best = new SilkClimbRouting.Route { Score = float.NegativeInfinity };
            Vector2 movement = Vector2.zero;
            bool selectedVertical = false;
            int selectedSegment = 0;
            float selectedT = 0f;
            Player.AnimationIndex animation = player.animation;
            foreach (var bridge in SilkBridgeManager.GetBridgesInRoom(player.room))
            {
                if (bridge == state.Target || !bridge.IsActive ||
                    bridge.room != player.room || bridge.SegmentCount < 1) continue;
                UpdateRoutingSurface(bridge, ref state.CandidatePoints);
                bool found = SilkClimbRouting.TryRoute(state.RoutingPoints, state.CandidatePoints, contact, input, out var route);
                // Explicit attachments stay connected even if a parent kink lies
                // between the regular surface samples.
                CheckAnchorRoute(bridge.startAnchor, state.Target, 0f, true, state, contact, input, ref found, ref route);
                CheckAnchorRoute(bridge.endAnchor, state.Target, bridge.SegmentCount, true, state, contact, input, ref found, ref route);
                if (state.Target is SilkBridge current)
                {
                    CheckAnchorRoute(current.startAnchor, bridge, 0f, false, state, contact, input, ref found, ref route);
                    CheckAnchorRoute(current.endAnchor, bridge, current.SegmentCount, false, state, contact, input, ref found, ref route);
                }
                if (!found || !SilkClimbRouting.Better(route, best)) continue;
                if (bridge == state.PreviousTarget && (route.Point - state.SwitchPoint).sqrMagnitude < 4f * 4f) continue;
                MaterialCoordinate(bridge, route.Coordinate, out int segment, out float t);
                bool vertical = VerticalAt(bridge, segment, state.Vertical);
                var nextAnimation = vertical ? Player.AnimationIndex.ClimbOnBeam :
                    player.animation == Player.AnimationIndex.StandOnBeam || player.animation == Player.AnimationIndex.BeamTip
                        ? Player.AnimationIndex.StandOnBeam : Player.AnimationIndex.HangFromBeam;
                int chunk = nextAnimation == Player.AnimationIndex.StandOnBeam ? 1 : 0;
                Vector2 offset = vertical ? Vector2.right * (player.flipDirection * 5f) :
                    nextAnimation == Player.AnimationIndex.StandOnBeam ? Vector2.up * 5f : Vector2.zero;
                // Keep the occupied movement coordinate if the destination extends
                // that far. In particular, feet-to-hands need not lower the whole
                // body by its length when an upright branch already passes the head.
                Vector2 grip = player.bodyChunks[chunk].pos - offset;
                if (SilkBeamGeometry.TrySample(state.CandidatePoints, grip, vertical, out var nearby) &&
                    nearby.AxisDistance < 0.01f && (nearby.Point - route.Point).sqrMagnitude <= 28f * 28f)
                    MaterialCoordinate(bridge, nearby.Segment + nearby.T, out segment, out t);
                Vector2 point = bridge.GetPointOnSegment(segment, t);
                Vector2 shift = point + offset - player.bodyChunks[chunk].pos;
                if (shift.sqrMagnitude > 32f * 32f || !CanMoveBody(player, shift)) continue;
                selected = bridge;
                selectedVertical = vertical;
                selectedSegment = segment;
                selectedT = t;
                animation = nextAnimation;
                movement = shift;
                best = route;
            }
            if (selected == null) return;
            state.PreviousTarget = state.Target;
            state.SwitchPoint = best.Point;
            state.SwitchInput = input.normalized;
            state.RouteInputActive = true;
            state.Target = selected;
            state.Segment = selectedSegment;
            state.T = selectedT;
            state.Vertical = selectedVertical;
            state.RouteSign = Vector2.Dot(Tangent(selected, state.Segment), best.Outgoing) >= 0f ? 1 : -1;
            state.StepDistance = 0f;
            state.WasMoving = false;
            state.LastSupport = selected.GetPointOnSegment(state.Segment, state.T);
            // Start the new queried support at the existing grip. Do not teleport
            // either chunk or rewrite lastPos: rendering must interpolate the move.
            state.Transfer.Begin(movement);
            UpdateSurface(state);
            player.animation = animation;
            player.bodyMode = Player.BodyModeIndex.ClimbingOnBeam;
            player.room.PlaySound(SoundID.Slugcat_Grab_Beam, player.mainBodyChunk);
        }

        private static void CheckAnchorRoute(BridgeAnchor anchor, IClimbableSilk parent, float endpoint,
            bool candidateIsChild, ClimbState state, Vector2 contact, Vector2 input,
            ref bool found, ref SilkClimbRouting.Route best)
        {
            if (anchor.type != BridgeAnchor.AnchorType.BridgeSegment || anchor.attachedBridge != parent) return;
            float parentCoordinate = (anchor.segmentIndex + anchor.segmentT) * SurfaceSubdivisions;
            endpoint *= SurfaceSubdivisions;
            if (!SilkClimbRouting.TryJunction(state.RoutingPoints, candidateIsChild ? parentCoordinate : endpoint,
                state.CandidatePoints, candidateIsChild ? endpoint : parentCoordinate, contact, input, out var route)) return;
            if (!found || SilkClimbRouting.Better(route, best)) best = route;
            found = true;
        }

        private static void MapRouteInput(Player player, ClimbState state)
        {
            if (!state.RouteInputActive) return;
            Vector2 input = new Vector2(player.input[0].x, player.input[0].y);
            if (player.input[0].jmp || (input.normalized - state.SwitchInput).sqrMagnitude > 0.001f)
            {
                state.RouteInputActive = false;
                return;
            }
            // An up/down-selected sloping branch may still use vanilla's horizontal
            // beam posture. Feed its travel axis, otherwise down would drop the grip
            // immediately and up would pull up instead of following that branch.
            Vector2 travel = Tangent(state.Target, state.Segment).normalized * state.RouteSign;
            if (Vector2.Dot(travel, input.normalized) < 0.15f)
            {
                state.RouteInputActive = false;
                return;
            }
            player.input[0].x = state.Vertical ? 0 : travel.x >= 0f ? 1 : -1;
            player.input[0].y = state.Vertical ? (travel.y >= 0f ? 1 : -1) : 0;
        }

        private static Vector2 BasePoint(IClimbableSilk silk, int segment, float t) => silk is SilkBridge bridge
            ? bridge.GetBasePoint(segment, t) : silk.GetPointOnSegment(segment, t);

        private static void UpdateRoutingSurface(IClimbableSilk silk, ref Vector2[] points)
        {
            int samples = silk.SegmentCount * SurfaceSubdivisions;
            if (points == null || points.Length != samples + 1) points = new Vector2[samples + 1];
            for (int i = 0; i < samples; i++)
                points[i] = BasePoint(silk, i / SurfaceSubdivisions, (i % SurfaceSubdivisions) / (float)SurfaceSubdivisions);
            points[samples] = BasePoint(silk, silk.SegmentCount - 1, 1f);
        }

        private static void MaterialCoordinate(IClimbableSilk silk, float coordinate, out int segment, out float t)
        {
            coordinate /= SurfaceSubdivisions;
            segment = Math.Min((int)coordinate, silk.SegmentCount - 1);
            t = coordinate - segment;
        }

        private static bool CanMoveBody(Player player, Vector2 movement)
        {
            foreach (var chunk in player.bodyChunks)
                if (player.room.GetTile(chunk.pos + movement).Solid ||
                    (movement.sqrMagnitude > 0.01f && !player.room.VisualContact(chunk.pos, chunk.pos + movement))) return false;
            return true;
        }

        internal static bool TrySample(Player player, Vector2 query, bool vertical, out SilkBeamGeometry.Sample sample)
        {
            sample = default;
            if (!states.TryGetValue(player, out var state) || !Valid(player, state)) return false;
            if (state.Points == null || !SilkBeamGeometry.TrySample(state.Points, query, vertical, state.Transfer.Offset, out sample)) return false;
            float coordinate = (sample.Segment + sample.T) / SurfaceSubdivisions;
            sample.Segment = Math.Min((int)coordinate, state.Target.SegmentCount - 1);
            sample.T = coordinate - sample.Segment;
            return true;
        }

        private static void UpdateSurface(ClimbState state)
        {
            int count = state.Target.SegmentCount;
            int samples = count * SurfaceSubdivisions;
            if (state.Points == null || state.Points.Length != samples + 1) state.Points = new Vector2[samples + 1];
            for (int i = 0; i < samples; i++)
                state.Points[i] = state.Target.GetPointOnSegment(i / SurfaceSubdivisions, (i % SurfaceSubdivisions) / (float)SurfaceSubdivisions);
            state.Points[samples] = state.Target.GetPointOnSegment(count - 1, 1f);
        }

        private static bool FollowSupport(Player player, ClimbState state)
        {
            Vector2 point = state.Target.GetPointOnSegment(state.Segment, state.T);
            Vector2 movement = point - state.LastSupport;
            // Carry the body with sag/ripples before vanilla checks its grip. A
            // moving support must not appear to disappear underneath the feet.
            // Along-axis movement and jump velocities still belong to vanilla.
            if (state.Vertical) movement.y = 0f;
            else movement.x = 0f;
            var transfer = state.Transfer;
            // A pole jump retains its grip; do not add handoff correction while
            // jumping. Departures clear the transfer without applying its remainder.
            if (!player.input[0].jmp) transfer.Advance();
            movement += transfer.Offset - state.Transfer.Offset;
            if (movement.sqrMagnitude > 35f * 35f) return false;
            for (int i = 0; i < player.bodyChunks.Length; i++)
            {
                Vector2 from = player.bodyChunks[i].pos;
                if (player.room.GetTile(from + movement).Solid ||
                    (movement.sqrMagnitude > 0.01f && !player.room.VisualContact(from, from + movement))) return false;
            }
            for (int i = 0; i < player.bodyChunks.Length; i++) player.bodyChunks[i].pos += movement;
            state.Transfer = transfer;
            state.LastSupport = point;
            return true;
        }

        internal static bool TryGetSurface(Player player, out bool vertical)
        {
            vertical = false;
            if (!IsClimbing(player)) return false;
            vertical = states.GetOrCreateValue(player).Vertical;
            return true;
        }

        // IntVector2 beam queries express offsets from the occupied tile. Anchor
        // that grid to the contact chunk, so a strand at y=19 behaves like y=10.
        internal static Vector2 TileQuery(Player player, IntVector2 tile)
        {
            Vector2 contact = Contact(player);
            IntVector2 origin = player.room.GetTilePosition(contact);
            return contact + new Vector2((tile.x - origin.x) * 20f, (tile.y - origin.y) * 20f);
        }
    }
}
