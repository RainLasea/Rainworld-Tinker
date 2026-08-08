using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Silk
{
    public static class SilkAimInput
    {
        private const float MIN_ROPE_VISIBLE = 0.1f;

        private static readonly HashSet<int> silkRequestPlayers = new();
        private static readonly Dictionary<int, bool> verticalInputLastFrame = new();
        private static readonly Dictionary<int, bool> rightMouseDownLastFrame = new();
        private static readonly Dictionary<int, bool> leftMouseDownLastFrame = new();
        private static readonly Dictionary<int, Room> lastPlayerRoom = new();
        private static readonly Dictionary<int, bool> jumpLastFrame = new();

        public static bool IsShooting(Player player)
        {
            int playerNum = player.playerState?.playerNumber ?? -1;
            return playerNum >= 0 && silkRequestPlayers.Contains(playerNum);
        }

        public static bool IsReleasing(Player player) => false;

        public static void Initialize()
        {
            On.Player.Update += Player_Update_Input;
            On.PlayerGraphics.SuckedIntoShortCut += PlayerGraphics_SuckedIntoShortCut;
        }

        public static void Cleanup()
        {
            On.Player.Update -= Player_Update_Input;
            On.PlayerGraphics.SuckedIntoShortCut -= PlayerGraphics_SuckedIntoShortCut;
            silkRequestPlayers.Clear();
            verticalInputLastFrame.Clear();
            rightMouseDownLastFrame.Clear();
            leftMouseDownLastFrame.Clear();
            lastPlayerRoom.Clear();
            jumpLastFrame.Clear();
            GamepadInputReader.Cleanup();
        }

        private static void PlayerGraphics_SuckedIntoShortCut(On.PlayerGraphics.orig_SuckedIntoShortCut self, PlayerGraphics selfGraphics, Vector2 shortCutPosition)
        {
            self(selfGraphics, shortCutPosition);
            if (selfGraphics.owner is Player player)
            {
                SilkPhysics silk = tinkerSilkData.Get(player);
                if (silk.Attached)
                {
                    silk.Release(true);
                }
            }
        }

        private static Vector2 GetMouseAimDirection(Player player)
        {
            var cam = tinker.Mouse.MouseAimSystem.GetCurrentCamera();
            bool useMouse = cam != null;

            Vector2 aimVector;

            if (useMouse)
            {
                Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
                Vector2 headPos = player.bodyChunks[0].pos;
                aimVector = mouseWorldPos - headPos;
            }
            else
            {
                if (player.input[0].x != 0 || player.input[0].y != 0)
                    aimVector = new Vector2(player.input[0].x, player.input[0].y);
                else if (player.bodyChunks[0].vel.magnitude > 0.5f)
                    aimVector = player.bodyChunks[0].vel;
                else
                    aimVector = Vector2.right * player.flipDirection;
            }

            if (aimVector.magnitude < 0.1f)
                aimVector = Vector2.right * player.flipDirection;

            return aimVector.normalized;
        }

        private static Vector2 GetMouseAimDirectionFromPoint(Vector2 referencePoint, Player player)
        {
            var cam = tinker.Mouse.MouseAimSystem.GetCurrentCamera();
            bool useMouse = cam != null;

            Vector2 aimVector;

            if (useMouse)
            {
                Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
                aimVector = mouseWorldPos - referencePoint;
            }
            else
            {
                if (player.input[0].x != 0 || player.input[0].y != 0)
                    aimVector = new Vector2(player.input[0].x, player.input[0].y);
                else
                    aimVector = Vector2.right * player.flipDirection;
            }

            if (aimVector.magnitude < 0.1f)
                aimVector = Vector2.right * player.flipDirection;

            return aimVector.normalized;
        }

        private static Vector2 PerpendicularVector(Vector2 v) => new Vector2(v.y, -v.x);

        private static void MovePlayerVertically(Player player, SilkPhysics silk, float direction)
        {
            Vector2 toAnchor = (silk.pos - player.bodyChunks[0].pos).normalized;
            float climbForce = direction * 0.8f;

            for (int i = 0; i < player.bodyChunks.Length; i++)
                player.bodyChunks[i].vel += toAnchor * climbForce;
        }

        private static void Player_Update_Input(On.Player.orig_Update orig, Player self, bool eu)
        {
            // ═══════════════════════════════════════════════════
            // PHASE 1: BEFORE orig() — Apply continuous physics forces
            // Forces applied here WILL be processed by the game's collision/physics
            // ═══════════════════════════════════════════════════
            bool isTinker = IsTinkerPlayer(self);
            if (isTinker && self.room != null && !self.dead)
            {
                SilkPhysics silk = tinkerSilkData.Get(self);
                if (silk.Attached && !(SilkBridgeManager.GetBridgeModeState(self)?.animating == true))
                {
                    // Use self.input[1] (previous frame input) for continuous forces
                    // self.input[0] is not set yet — game sets it inside orig()
                    ApplyContinuousPhysicsForces(self, silk);
                }
            }

            orig(self, eu);

            // ═══════════════════════════════════════════════════
            // PHASE 2: AFTER orig() — Input detection & state changes
            // self.input[0] is now populated with current frame's input
            // ═══════════════════════════════════════════════════
            if (self.room == null || self.dead) return;
            if (!isTinker) return;

            // ── Remote player guard ─────────────────────────────
            // In Rain Meadow multiplayer, remote players use OnlineController.
            // Their input[0] is synced from the network, but mouse/keyboard/gamepad
            // reads are LOCAL only. Skip silk input handling for remote players
            // to prevent local input from accidentally triggering their silk.
            if (IsRemotePlayer(self)) return;

            int playerNum = self.playerState?.playerNumber ?? -1;
            if (playerNum < 0) return;

            SilkPhysics silk2 = tinkerSilkData.Get(self);
            var bridgeState = SilkBridgeManager.GetBridgeModeState(self);

            // Track room changes — release silk on room transition
            TrackRoomChange(self, playerNum, silk2);

            GamepadSnapshot gamepad = GamepadInputReader.Sample(self, playerNum);
            var gamepadState = GamepadBridgeState.GetOrCreate(self);
            gamepadState.gamepadConnected = gamepad.connected;
            bool usingGamepad = gamepad.connected || gamepad.ltHeld || gamepad.rtHeld || gamepadState.aiming;

            // Read raw mouse/keyboard input for trigger events.
            ReadMouseInputState(playerNum, out bool rightMousePressed, out bool leftMousePressed, out bool rightMouseDown, out bool leftMouseDown);

            // Track game input state changes
            ReadGameInputState(self, playerNum, out bool wasVerticalInput, out bool currentVerticalInput, out bool wasJumping, out bool isJumping);

            bool inBridgeMode = bridgeState?.active == true;
            bool animationRunning = bridgeState?.animating == true;

            // Update bridge D2 position
            if (inBridgeMode && silk2.Attached && bridgeState != null)
                bridgeState.UpdateD2Position(self.room);

            if (usingGamepad)
            {
                HandleGamepadInput(self, silk2, bridgeState, gamepad);
                inBridgeMode = bridgeState?.active == true;
                animationRunning = bridgeState?.animating == true;
            }
            else
            {
                // Bridge mode: activate/deactivate + shoot virtual silk
                HandleBridgeMode(self, silk2, bridgeState, rightMouseDown, leftMousePressed, inBridgeMode, animationRunning);
            }

            // Vertical input released → lock rope length
            if (wasVerticalInput && !currentVerticalInput && silk2.Attached)
            {
                silk2.idealRopeLength = Mathf.Max(silk2.requestedRopeLength, MIN_ROPE_VISIBLE);
            }

            // Super jump trigger detection + burst vel applied next frame
            bool jumpTriggered = isJumping && !wasJumping;
            HandleSuperJump(self, silk2, jumpTriggered);

            // Shoot or release silk on right mouse press
            if (!usingGamepad && rightMousePressed && !inBridgeMode && !animationRunning)
            {
                if (silk2.mode == SilkMode.Retracted)
                    silk2.Shoot(GetMouseAimDirection(self));
                else if (silk2.Attached)
                    silk2.Release();
            }

            // Reel/unreel rope length on vertical input (after orig — only changes rope state)
            if (silk2.Attached && !animationRunning && silk2.mode == SilkMode.AttachedToTerrain)
                UpdateRopeLengthOnVerticalInput(self, silk2);

            // Pickup key → detach objects from bridges
            if (self.input[0].pckp && !self.input[1].pckp)
            {
                foreach (var bridge in SilkBridgeManager.GetBridgesInRoom(self.room))
                {
                    if (bridge.TryDetachObject(self, out PhysicalObject _))
                        break;
                }
            }

            // Bridge climb attach on y-up near bridge
            TryAttachToBridge(self, inBridgeMode);
        }

        private static bool IsTinkerPlayer(Player self)
        {
            return self.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !self.isSlugpup;
        }

        /// <summary>
        /// Detects if this player is a remote multiplayer player (e.g. from Rain Meadow).
        /// Remote players use OnlineController — their input[0] is synced over network,
        /// but local mouse/keyboard input reads are invalid for them.
        /// </summary>
        private static bool IsRemotePlayer(Player self)
        {
            if (RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
            {
                return RainMeadow.RainMeadowBridge.IsOnlineAndRemote(self);
            }
            if (self.controller == null) return false;
            string controllerType = self.controller.GetType().Name;
            // Standard vanilla controllers: KeyboardController, JoystickController
            // Rain Meadow remote controller: OnlineController
            return controllerType != "KeyboardController" && controllerType != "JoystickController";
        }

        private static void ApplyContinuousPhysicsForces(Player self, SilkPhysics silk)
        {
            // Swing force — based on last frame's input (processed by orig())
            if (self.input[1].x != 0)
            {
                Vector2 toAnchor = (silk.pos - self.bodyChunks[0].pos).normalized;
                Vector2 perpendicular = PerpendicularVector(toAnchor);
                float swingForce = self.input[1].x * 0.5f;

                for (int i = 0; i < self.bodyChunks.Length; i++)
                {
                    self.bodyChunks[i].vel += perpendicular * swingForce;
                    if (Mathf.Abs(toAnchor.x) > 0.3f)
                        self.bodyChunks[i].vel.y -= 0.3f;
                }
            }

            // Climb force — based on last frame's input
            if (self.input[1].y != 0 && silk.mode == SilkMode.AttachedToTerrain)
            {
                MovePlayerVertically(self, silk, Mathf.Sign(self.input[1].y));
            }

            // Super jump burst — reserved for future: apply burst from previous frame's trigger
            if (pendingSuperJumpBurst.TryGetValue(self, out object _))
            {
                // Currently the super jump burst is applied immediately in HandleSuperJump (post-orig).
                // This pre-orig slot is reserved for burst migration when the one-frame lag
                // from using input[1] is acceptable.
                pendingSuperJumpBurst.Remove(self);
            }
        }

        private static readonly ConditionalWeakTable<Player, object> pendingSuperJumpBurst = new ConditionalWeakTable<Player, object>();

        private static void TrackRoomChange(Player self, int playerNum, SilkPhysics silk)
        {
            lastPlayerRoom.TryGetValue(playerNum, out Room lastRoom);
            if (lastRoom != self.room)
            {
                if (silk.Attached)
                    silk.Release(true);
                GamepadBridgeState.GetOrCreate(self).Cancel();
                SilkBridgeManager.GetBridgeModeState(self)?.Deactivate();
                lastPlayerRoom[playerNum] = self.room;
            }
        }

        private static void ReadMouseInputState(int playerNum, out bool rightMousePressed, out bool leftMousePressed, out bool rightMouseDown, out bool leftMouseDown)
        {
            rightMouseDown = Input.GetKey(Options_Hook.SilkShootKey);
            leftMouseDown = Input.GetMouseButton(0);
            bool wasRightMouseDown = rightMouseDownLastFrame.GetValueOrDefault(playerNum);
            bool wasLeftMouseDown = leftMouseDownLastFrame.GetValueOrDefault(playerNum);

            rightMouseDownLastFrame[playerNum] = rightMouseDown;
            leftMouseDownLastFrame[playerNum] = leftMouseDown;

            rightMousePressed = rightMouseDown && !wasRightMouseDown;
            leftMousePressed = leftMouseDown && !wasLeftMouseDown;
        }

        private static void ReadGameInputState(Player self, int playerNum, out bool wasVerticalInput, out bool currentVerticalInput, out bool wasJumping, out bool isJumping)
        {
            wasVerticalInput = verticalInputLastFrame.GetValueOrDefault(playerNum);
            currentVerticalInput = self.input[0].y != 0;
            verticalInputLastFrame[playerNum] = currentVerticalInput;

            wasJumping = jumpLastFrame.GetValueOrDefault(playerNum);
            isJumping = self.input[0].jmp;
            jumpLastFrame[playerNum] = isJumping;
        }

        private static void HandleBridgeMode(Player self, SilkPhysics silk, BridgeModeState bridgeState, bool rightMouseDown, bool leftMousePressed, bool inBridgeMode, bool animationRunning)
        {
            if (silk.Attached && rightMouseDown && bridgeState != null)
            {
                if (!bridgeState.active)
                {
                    bridgeState.Activate(silk.pos);

                    if (silk.mode == SilkMode.AttachedToTerrain && silk.attachedBridge != null)
                    {
                        int segIndex;
                        float t;
                        silk.attachedBridge.GetClosestPoint(silk.pos, out segIndex, out t);
                        bridgeState.AttachD2ToBridge(silk.attachedBridge, segIndex, t);
                    }
                    else if (silk.mode == SilkMode.AttachedToObject && silk.attachedObject != null)
                    {
                        bridgeState.AttachD2ToObject(silk.attachedObject);
                    }
                }

                if (leftMousePressed && !bridgeState.animating)
                {
                    Vector2 D2 = bridgeState.point2;
                    Vector2 shootDir = GetMouseAimDirectionFromPoint(D2, self);
                    Vector2 mouseWorld = GetMouseWorldPosition();
                    bridgeState.ShootVirtualSilk(shootDir, D2, self.room, mouseWorld);
                    silk.Release();
                }
            }
            else if (bridgeState?.active == true && !bridgeState.animating)
            {
                bridgeState.Deactivate();
            }
        }

        private static void HandleSuperJump(Player self, SilkPhysics silk, bool jumpTriggered)
        {
            if (!jumpTriggered || !silk.Attached) return;

            const int TRIGGER_WINDOW = 6;

            if (silk.attachedTime <= TRIGGER_WINDOW)
            {
                silk.superJumpTimer = 5;
                silk.superJumpBaseLength = Vector2.Distance(self.mainBodyChunk.pos, silk.pos);
                Vector2 toAnchor = (silk.pos - self.mainBodyChunk.pos).normalized;
                Vector2 burstVel = Vector2.up * 10f + toAnchor * 6f;

                // Note: vel modification after orig() is a design tradeoff.
                // The burst is a one-frame impulse that primarily goes upward,
                // so the collision risk is minimal (upward through air).
                // The game's gravity + collision next frame will correct any issues.
                for (int i = 0; i < self.bodyChunks.Length; i++)
                    self.bodyChunks[i].vel += burstVel;
                self.jumpBoost += 2f;
            }
            else
            {
                silk.Release();
            }
        }

        private static void UpdateRopeLengthOnVerticalInput(Player self, SilkPhysics silk)
        {
            if (self.input[0].y > 0)
            {
                float currentDist = Vector2.Distance(self.bodyChunks[0].pos, silk.pos);
                const float REEL_STEP = 4f;
                silk.idealRopeLength = Mathf.Clamp(silk.idealRopeLength - REEL_STEP, MIN_ROPE_VISIBLE, Mathf.Max(currentDist, MIN_ROPE_VISIBLE));
                if (silk.requestedRopeLength < MIN_ROPE_VISIBLE)
                    silk.requestedRopeLength = MIN_ROPE_VISIBLE;
            }
            else if (self.input[0].y < 0)
            {
                const float UNREEL_STEP = 4f;
                silk.idealRopeLength = Mathf.Clamp(silk.idealRopeLength + UNREEL_STEP, MIN_ROPE_VISIBLE, 800f);
            }
        }

        private static void TryAttachToBridge(Player self, bool inBridgeMode)
        {
            if (inBridgeMode || SilkClimb.IsClimbing(self) || !self.Consious || self.bodyMode == Player.BodyModeIndex.CorridorClimb)
                return;

            if (self.input[0].y > 0)
            {
                Vector2 checkPos = self.mainBodyChunk.pos + new Vector2(0f, 15f);
                SilkBridge closestBridge = SilkBridgeManager.GetClosestBridge(self.room, checkPos, 40f);

                if (closestBridge != null)
                {
                    int segIndex;
                    float t;
                    closestBridge.GetClosestPoint(checkPos, out segIndex, out t);

                    SilkClimb.AttachPlayerToSilk(self, closestBridge, segIndex, t);
                    self.Blink(5);
                    self.room.PlaySound(SoundID.Player_Grab_Pole_Mimic, self.mainBodyChunk.pos, 1f, 1f);
                }
            }
        }

        private static Vector2 GetMouseWorldPosition()
        {
            var cam = tinker.Mouse.MouseAimSystem.GetCurrentCamera();
            if (cam != null)
                return new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
            return Vector2.zero;
        }

        // ── Gamepad input methods ─────────────────────────────────────

        private static void HandleGamepadInput(Player self, SilkPhysics silk, BridgeModeState bridgeState, GamepadSnapshot input)
        {
            var gpState = GamepadBridgeState.GetOrCreate(self);

            if (input.ltPressed)
                gpState.EnterAimMode(self);

            if (gpState.aiming && input.ltHeld)
                gpState.UpdateAim(input.rightStick);

            if (input.ltReleased && gpState.aiming)
            {
                if (gpState.rtHeld || gpState.selectingBridge)
                {
                    silk.Release(true);
                    bridgeState?.Deactivate();
                }
                gpState.Cancel(keepGamepadConnected: input.connected);
                return;
            }

            if (input.rtPressed && gpState.aiming && silk.mode == SilkMode.Retracted && bridgeState?.animating != true)
            {
                gpState.BeginRtPress();
                silk.ShootAtPosition(gpState.firstTargetWorldPos);
                return;
            }

            if (input.rtPressed && silk.Attached && !gpState.selectingBridge)
            {
                silk.Release();
                gpState.ExitAimMode();
                return;
            }

            if (input.rtHeld && gpState.rtHeld)
            {
                gpState.UpdateRtHold();
                if (!gpState.selectingBridge && silk.Attached && gpState.rtHoldSeconds >= Options_Hook.GamepadBridgeHoldSeconds)
                {
                    if (bridgeState != null)
                    {
                        bridgeState.Activate(silk.pos);

                        if (silk.mode == SilkMode.AttachedToTerrain && silk.attachedBridge != null)
                        {
                            int segIdx; float t;
                            silk.attachedBridge.GetClosestPoint(silk.pos, out segIdx, out t);
                            bridgeState.AttachD2ToBridge(silk.attachedBridge, segIdx, t);
                        }
                        else if (silk.mode == SilkMode.AttachedToObject && silk.attachedObject != null)
                        {
                            bridgeState.AttachD2ToObject(silk.attachedObject);
                        }
                        gpState.BeginBridgeSelection(bridgeState.point2);
                    }
                }
            }

            if (input.rtReleased && gpState.rtHeld)
            {
                bool shouldBuild = gpState.selectingBridge && bridgeState?.active == true && !bridgeState.animating;
                gpState.OnRTRelease();

                if (shouldBuild)
                {
                    bridgeState.ShootVirtualSilk(
                        (gpState.aimWorldPos - bridgeState.point2).normalized,
                        bridgeState.point2, self.room, gpState.aimWorldPos);
                    silk.Release();
                    gpState.selectingBridge = false;
                }
            }
        }
    }
}