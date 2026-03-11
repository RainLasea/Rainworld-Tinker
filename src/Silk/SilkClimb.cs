using RWCustom;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk
{
    public static class SilkClimb
    {
        private enum ClimbMode
        {
            VerticalClimb,
            HorizontalClimb
        }

        private class SwitchingState
        {
            public IClimbableSilk toSilk;
            public int toSeg;
            public float toT;
            public Vector2 fromPos;
            public int counter;
            public int duration = 8;
            public float Progress => Mathf.Clamp01((float)counter / duration);
        }

        private class ClimbState
        {
            public IClimbableSilk ClimbTarget;
            public int SegmentIndex;
            public float T;
            public ClimbMode CurrentClimbMode;
            public bool IsHanging;
            public Vector2 smoothedAttachPoint;
            public bool Active => ClimbTarget != null && ClimbTarget.IsActive;
            public SwitchingState switching;
            public int switchCooldown;
        }

        private static readonly ConditionalWeakTable<Player, ClimbState> climbStates = new ConditionalWeakTable<Player, ClimbState>();

        private static FieldInfo noGrabCounterField;

        private static int GetNoGrabCounter(Player player)
        {
            if (noGrabCounterField == null)
            {
                noGrabCounterField = typeof(Player).GetField("noGrabCounter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (int)noGrabCounterField.GetValue(player);
        }

        private static void SetNoGrabCounter(Player player, int value)
        {
            if (noGrabCounterField == null)
            {
                noGrabCounterField = typeof(Player).GetField("noGrabCounter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            noGrabCounterField.SetValue(player, value);
        }

        public static bool IsClimbing(Player player)
        {
            return climbStates.TryGetValue(player, out var state) && state.Active;
        }

        public static void AttachPlayerToSilk(Player player, IClimbableSilk silk, int seg, float t)
        {
            if (GetNoGrabCounter(player) > 0) return;
            var state = climbStates.GetOrCreateValue(player);
            if (state.switching != null) return;

            state.ClimbTarget = silk;
            state.SegmentIndex = seg;
            state.T = t;
            player.mainBodyChunk.vel *= 0.3f;

            state.switching = new SwitchingState
            {
                toSilk = silk,
                toSeg = seg,
                toT = t,
                duration = 8,
                counter = 0,
                fromPos = player.mainBodyChunk.pos
            };

            player.room.PlaySound(SoundID.Player_Grab_Pole_Mimic, player.mainBodyChunk);
        }

        public static void DetachPlayerFromSilk(Player player)
        {
            if (player == null) return;
            if (climbStates.TryGetValue(player, out var state))
            {
                state.ClimbTarget = null;
                state.switching = null;
                SetNoGrabCounter(player, 15);
            }

            if (player.bodyMode == Player.BodyModeIndex.ClimbingOnBeam)
            {
                player.animation = Player.AnimationIndex.None;
                player.bodyMode = Player.BodyModeIndex.Default;
            }
        }

        public static void Init()
        {
            On.Player.UpdateBodyMode += Player_UpdateBodyMode;
            On.Player.Die += Player_Die;
        }

        private static void Player_Die(On.Player.orig_Die orig, Player self)
        {
            DetachPlayerFromSilk(self);
            orig(self);
        }

        private static void Player_UpdateBodyMode(On.Player.orig_UpdateBodyMode orig, Player self)
        {
            if (climbStates.TryGetValue(self, out var state) && (state.Active || state.switching != null))
            {
                UpdateSilkClimbPhysics(self, state);
            }
            else
            {
                orig(self);
            }
        }

        private static void UpdateSilkClimbPhysics(Player self, ClimbState state)
        {
            if (state.switchCooldown > 0) state.switchCooldown--;

            if (state.switching != null)
            {
                UpdateSwitching(self, state);
                return;
            }

            var silk = state.ClimbTarget;
            if (silk == null || !silk.IsActive)
            {
                DetachPlayerFromSilk(self);
                return;
            }

            Vector2 tangent = ComputeSegmentTangent(silk, state.SegmentIndex, state.T);
            float angle = Vector2.Angle(tangent, Vector2.up);

            ClimbMode newMode = (angle < 55f || angle > 125f) ? ClimbMode.VerticalClimb : ClimbMode.HorizontalClimb;
            if (newMode != state.CurrentClimbMode)
            {
                state.CurrentClimbMode = newMode;
                self.animation = (newMode == ClimbMode.VerticalClimb) ? Player.AnimationIndex.ClimbOnBeam : Player.AnimationIndex.StandOnBeam;
                state.IsHanging = false;
            }

            self.bodyMode = Player.BodyModeIndex.ClimbingOnBeam;

            if (state.CurrentClimbMode == ClimbMode.VerticalClimb)
            {
                VerticalClimbUpdate(self, state, tangent);
            }
            else
            {
                HorizontalClimbUpdate(self, state, tangent);
            }

            Vector2 vector = self.bodyChunks[0].pos - self.bodyChunks[1].pos;
            float magnitude = vector.magnitude;
            float num = self.bodyChunkConnections[0].distance - magnitude;
            if (magnitude > 0.001f)
            {
                vector.Normalize();
                self.bodyChunks[0].pos += vector * num * 0.7f;
                self.bodyChunks[0].vel += vector * num * 0.7f;
                self.bodyChunks[1].pos -= vector * num * 0.3f;
                self.bodyChunks[1].vel -= vector * num * 0.3f;
            }

            Vector2 targetPointOnSilk = silk.GetPointOnSegment(state.SegmentIndex, state.T);
            state.smoothedAttachPoint = Vector2.Lerp(state.smoothedAttachPoint, targetPointOnSilk, 0.4f);

            Vector2 moveDir = (self.mainBodyChunk.pos - state.smoothedAttachPoint);
            if (moveDir.sqrMagnitude > 1f)
            {
                Vector2 force = Vector2.down * self.gravity * self.TotalMass * 1.2f;
                try { silk.ApplyClimbForce(state.smoothedAttachPoint, force); } catch { }
            }
        }

        private static void UpdateSwitching(Player player, ClimbState state)
        {
            if (state.switching == null || state.switching.toSilk == null)
            {
                state.switching = null;
                return;
            }

            state.switching.counter++;
            float smoothProgress = Mathf.SmoothStep(0f, 1f, state.switching.Progress);

            Vector2 targetPos = state.switching.toSilk.GetPointOnSegment(state.switching.toSeg, state.switching.toT);
            Vector2 newPos = Vector2.Lerp(state.switching.fromPos, targetPos, smoothProgress);

            player.mainBodyChunk.vel *= 0.85f;
            AlignPlayerToPoint(player, newPos, state);

            if (state.switching.counter >= state.switching.duration)
            {
                var finalSilk = state.switching.toSilk;
                var finalSeg = state.switching.toSeg;
                var finalT = state.switching.toT;

                state.switching = null;
                state.ClimbTarget = finalSilk;
                state.SegmentIndex = finalSeg;
                state.T = finalT;
                state.smoothedAttachPoint = targetPos;

                player.bodyMode = Player.BodyModeIndex.ClimbingOnBeam;
                player.animation = Player.AnimationIndex.ClimbOnBeam;
                player.room.PlaySound(SoundID.Player_Grab_Pole_Mimic, player.mainBodyChunk, false, 1f, 1.1f);
            }
        }

        private const float SilkClimbGrabRange = 24f;

        private static bool TrySwitchBridge(Player self, ClimbState state, int inputX, int inputY)
        {
            if (state.switchCooldown > 0 || IsNearVanillaPole(self)) return false;

            Vector2 checkDir = new Vector2(inputX, inputY);
            Vector2 checkPos = self.mainBodyChunk.pos + checkDir * 10f;
            SilkBridge targetBridge = SilkBridgeManager.GetClosestBridge(self.room, checkPos, SilkClimbGrabRange, b => b != state.ClimbTarget);

            if (targetBridge != null)
            {
                int segIndex;
                float t;
                Vector2 closestPoint = targetBridge.GetClosestPoint(checkPos, out segIndex, out t);

                if (Vector2.Distance(checkPos, closestPoint) < SilkClimbGrabRange)
                {
                    Vector2 targetTangent = ComputeSegmentTangent(targetBridge, segIndex, t);
                    float angle = Vector2.Angle(targetTangent, Vector2.up);
                    bool isTargetHorizontal = (angle >= 55f && angle <= 125f);

                    bool shouldSwitch = (state.CurrentClimbMode == ClimbMode.VerticalClimb && isTargetHorizontal && inputX != 0) ||
                                        (state.CurrentClimbMode == ClimbMode.HorizontalClimb && !isTargetHorizontal && inputY != 0);

                    if (shouldSwitch)
                    {
                        state.switchCooldown = 15;
                        state.switching = new SwitchingState
                        {
                            toSilk = targetBridge,
                            toSeg = segIndex,
                            toT = t,
                            fromPos = self.mainBodyChunk.pos,
                            duration = 7
                        };
                        self.room.PlaySound(SoundID.Player_Grab_Pole_Mimic, self.mainBodyChunk.pos, 0.6f, 1.2f);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void VerticalClimbUpdate(Player self, ClimbState state, Vector2 tangent)
        {
            var silk = state.ClimbTarget;
            self.animation = Player.AnimationIndex.ClimbOnBeam;

            self.bodyChunks[0].vel *= 0.75f;
            self.bodyChunks[1].vel *= 0.75f;

            if (self.input[0].x != 0 && self.input[1].x == 0)
            {
                if (TrySwitchBridge(self, state, self.input[0].x, 0)) return;
            }

            if (self.input[0].jmp && !self.input[1].jmp)
            {
                Vector2 jumpDir = (tangent * self.input[0].x * 0.8f + Vector2.up * 0.7f).normalized;
                self.jumpBoost = 7f;
                self.bodyChunks[0].vel = jumpDir * 10f;
                self.bodyChunks[1].vel = jumpDir * 8f;
                self.canJump = 0;
                DetachPlayerFromSilk(self);
                return;
            }

            if (self.input[0].y < 0 && self.input[0].jmp && !self.input[1].jmp)
            {
                DetachPlayerFromSilk(self);
                return;
            }

            int dy = self.input[0].y;
            if (dy != 0)
            {
                float climbSpeed = self.slugcatStats.poleClimbSpeedFac * 1.8f;
                Vector2 segStart = silk.GetPointOnSegment(state.SegmentIndex, 0f);
                Vector2 segEnd = silk.GetPointOnSegment(state.SegmentIndex, 1f);
                int upSign = (segEnd.y - segStart.y) >= 0 ? 1 : -1;
                float segmentLength = Vector2.Distance(segStart, segEnd);
                float deltaT = (segmentLength > 0.1f) ? (climbSpeed * dy * upSign) / segmentLength : 0f;
                state.T += deltaT;
                UpdateSegment(state, silk);
                Vector2 move = (segEnd - segStart).normalized * climbSpeed * dy * upSign;
                self.bodyChunks[0].pos += move;
                self.bodyChunks[1].pos += move;
            }
            AlignPlayerToPoint(self, state.smoothedAttachPoint, state);
        }

        private static void HorizontalClimbUpdate(Player self, ClimbState state, Vector2 tangent)
        {
            var silk = state.ClimbTarget;

            if (self.input[0].y != 0 && self.input[1].y == 0)
            {
                if (state.IsHanging && self.input[0].y < 0)
                {
                    DetachPlayerFromSilk(self);
                    return;
                }
                if (TrySwitchBridge(self, state, 0, self.input[0].y)) return;
                state.IsHanging = self.input[0].y < 0;
            }
            self.animation = state.IsHanging ? Player.AnimationIndex.HangFromBeam : Player.AnimationIndex.StandOnBeam;

            if (self.input[0].jmp && !self.input[1].jmp)
            {
                Vector2 jumpDir = (Vector2.up * (state.IsHanging ? -0.5f : 1.1f) + tangent * self.input[0].x).normalized;
                self.jumpBoost = 6f;
                self.bodyChunks[0].vel = jumpDir * 9.5f;
                self.bodyChunks[1].vel = jumpDir * 8f;
                self.canJump = 0;
                DetachPlayerFromSilk(self);
                return;
            }

            int dx = self.input[0].x;
            if (dx != 0)
            {
                float moveDirection = (Vector2.Dot(tangent, Vector2.right) < 0) ? -dx : dx;
                float climbSpeed = self.slugcatStats.poleClimbSpeedFac * 1.6f * moveDirection;
                Vector2 segStart = silk.GetPointOnSegment(state.SegmentIndex, 0f);
                Vector2 segEnd = silk.GetPointOnSegment(state.SegmentIndex, 1f);
                float segmentLength = Vector2.Distance(segStart, segEnd);
                float deltaT = (segmentLength > 0.1f) ? climbSpeed / segmentLength : 0f;
                state.T += deltaT;
                UpdateSegment(state, silk);
            }

            self.bodyChunks[0].vel.y -= self.gravity * 0.5f;
            self.bodyChunks[1].vel.y -= self.gravity * 0.5f;
            self.bodyChunks[0].vel.x *= 0.85f;
            self.bodyChunks[1].vel.x *= 0.85f;

            AlignPlayerToPoint(self, state.smoothedAttachPoint, state);
        }

        private static void AlignPlayerToPoint(Player player, Vector2 point, ClimbState state)
        {
            float bodyDist = player.bodyChunkConnections[0].distance;
            if (state.CurrentClimbMode == ClimbMode.HorizontalClimb)
            {
                if (state.IsHanging)
                {
                    player.bodyChunks[0].pos = Vector2.Lerp(player.bodyChunks[0].pos, point + Vector2.down * 2f, 0.4f);
                    player.bodyChunks[1].pos = Vector2.Lerp(player.bodyChunks[1].pos, point + Vector2.down * (bodyDist + 2f), 0.3f);
                }
                else
                {
                    player.bodyChunks[1].pos = Vector2.Lerp(player.bodyChunks[1].pos, point, 0.7f);
                    player.bodyChunks[0].vel.y += player.gravity * 0.8f;
                }
            }
            else
            {
                float xBias = 0.5f;
                player.mainBodyChunk.pos.x = Mathf.Lerp(player.mainBodyChunk.pos.x, point.x, xBias);
                player.bodyChunks[1].pos.x = Mathf.Lerp(player.bodyChunks[1].pos.x, point.x, xBias * 0.8f);
                player.bodyChunks[0].pos.y = Mathf.Lerp(player.bodyChunks[0].pos.y, point.y, 0.8f);
                player.bodyChunks[1].pos.y = Mathf.Lerp(player.bodyChunks[1].pos.y, point.y - bodyDist, 0.7f);
            }
        }

        private static void UpdateSegment(ClimbState state, IClimbableSilk silk)
        {
            if (state.T < 0f)
            {
                if (state.SegmentIndex > 0) { state.SegmentIndex--; state.T += 1f; }
                else { state.T = 0f; }
            }
            else if (state.T > 1f)
            {
                if (state.SegmentIndex < silk.SegmentCount - 1) { state.SegmentIndex++; state.T -= 1f; }
                else { state.T = 1f; }
            }
        }

        private static Vector2 ComputeSegmentTangent(IClimbableSilk silk, int segIndex, float t)
        {
            float dt = 0.05f;
            Vector2 p0, p1;
            if (t < dt && segIndex > 0)
            {
                p0 = silk.GetPointOnSegment(segIndex - 1, 1f - (dt - t));
                p1 = silk.GetPointOnSegment(segIndex, t + dt);
            }
            else if (t > 1f - dt && segIndex < silk.SegmentCount - 1)
            {
                p0 = silk.GetPointOnSegment(segIndex, t - dt);
                p1 = silk.GetPointOnSegment(segIndex + 1, dt - (1f - t));
            }
            else
            {
                p0 = silk.GetPointOnSegment(segIndex, Mathf.Clamp01(t - dt));
                p1 = silk.GetPointOnSegment(segIndex, Mathf.Clamp01(t + dt));
            }
            Vector2 tangent = p1 - p0;
            if (tangent.sqrMagnitude < 1e-4f)
            {
                if (silk.SegmentCount > 1)
                {
                    int nextSeg = (segIndex < silk.SegmentCount - 1) ? segIndex + 1 : segIndex - 1;
                    return (silk.GetPointOnSegment(nextSeg, 0.5f) - silk.GetPointOnSegment(segIndex, 0.5f)).normalized;
                }
                return Vector2.up;
            }
            return tangent.normalized;
        }

        private static bool IsNearVanillaPole(Player player, float range = 22f)
        {
            if (player?.room == null) return false;
            IntVector2 tile = player.room.GetTilePosition(player.mainBodyChunk.pos);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var t = player.room.GetTile(tile.x + dx, tile.y + dy);
                    if (t.verticalBeam || t.horizontalBeam)
                    {
                        Vector2 polePos = player.room.MiddleOfTile(tile.x + dx, tile.y + dy);
                        if (Vector2.Distance(player.mainBodyChunk.pos, polePos) < range) return true;
                    }
                }
            }
            return false;
        }
    }
}