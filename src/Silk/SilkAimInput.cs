using RWCustom;
using System.Collections.Generic;
using UnityEngine;
using Tinker.Silk.Bridge;

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
        }

        private static void PlayerGraphics_SuckedIntoShortCut(On.PlayerGraphics.orig_SuckedIntoShortCut orig, PlayerGraphics self, Vector2 shortCutPosition)
        {
            orig(self, shortCutPosition);
            if (self.owner is Player player)
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
            Vector2 aimVector;

            if (cam != null)
            {
                Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
                Vector2 headPos = player.bodyChunks[0].pos;
                aimVector = mouseWorldPos - headPos;
            }
            else
            {
                if (player.bodyChunks[0].vel.magnitude > 0.5f)
                    aimVector = player.bodyChunks[0].vel;
                else if (player.input[0].x != 0 || player.input[0].y != 0)
                    aimVector = new Vector2(player.input[0].x, player.input[0].y);
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
            Vector2 aimVector;

            if (cam != null)
            {
                Vector2 mouseWorldPos = new Vector2(
                    Futile.mousePosition.x + cam.pos.x,
                    Futile.mousePosition.y + cam.pos.y
                );
                aimVector = mouseWorldPos - referencePoint;
            }
            else
            {
                if (player.bodyChunks[0].vel.magnitude > 0.5f)
                    aimVector = player.bodyChunks[0].vel;
                else if (player.input[0].x != 0 || player.input[0].y != 0)
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
            orig(self, eu);
            if (self.room == null || self.dead) return;

            bool isTinker = self.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !self.isSlugpup;
            if (!isTinker) return;

            if (Plugin.SilkFeatureEnabled != null && Plugin.SilkFeatureEnabled.TryGet(self, out bool featureEnabled) && !featureEnabled) return;

            int playerNum = self.playerState?.playerNumber ?? -1;
            if (playerNum < 0) return;

            SilkPhysics silk = tinkerSilkData.Get(self);

            lastPlayerRoom.TryGetValue(playerNum, out Room lastRoom);
            if (lastRoom != self.room)
            {
                if (silk.Attached)
                {
                    silk.Release(true);
                }
                lastPlayerRoom[playerNum] = self.room;
            }

            var bridgeState = SilkBridgeManager.GetBridgeModeState(self);

            bool rightMouseDown = Input.GetMouseButton(1);
            bool leftMouseDown = Input.GetMouseButton(0);
            bool wasRightMouseDown = rightMouseDownLastFrame.GetValueOrDefault(playerNum);
            bool wasLeftMouseDown = leftMouseDownLastFrame.GetValueOrDefault(playerNum);

            rightMouseDownLastFrame[playerNum] = rightMouseDown;
            leftMouseDownLastFrame[playerNum] = leftMouseDown;

            bool rightMousePressed = rightMouseDown && !wasRightMouseDown;
            bool leftMousePressed = leftMouseDown && !wasLeftMouseDown;

            bool wasVerticalInput = verticalInputLastFrame.GetValueOrDefault(playerNum);
            bool currentVerticalInput = self.input[0].y != 0;
            verticalInputLastFrame[playerNum] = currentVerticalInput;

            bool inBridgeMode = bridgeState?.active == true;
            bool animationRunning = bridgeState?.animating == true;

            if (inBridgeMode && silk.Attached && bridgeState != null)
            {
                bridgeState.UpdateD2Position(self.room);
            }

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

            if (wasVerticalInput && !currentVerticalInput && silk.Attached)
            {
                silk.idealRopeLength = silk.requestedRopeLength;
                if (silk.idealRopeLength < MIN_ROPE_VISIBLE)
                    silk.idealRopeLength = MIN_ROPE_VISIBLE;
            }

            Options.ControlSetup controlSetup = RWCustom.Custom.rainWorld.options.controls[self.playerState.playerNumber];
            KeyCode jumpKey = controlSetup.KeyboardJump;

            bool jumpPressed = Input.GetKeyDown(jumpKey) || self.input[0].jmp;

            if (jumpPressed && silk.Attached)
            {
                silk.Release();
                return;
            }

            if (rightMousePressed && !inBridgeMode && !animationRunning)
            {
                if (silk.mode == SilkMode.Retracted)
                    silk.Shoot(GetMouseAimDirection(self));
                else if (silk.Attached)
                    silk.Release();
            }

            if (silk.Attached && !animationRunning)
            {
                bool attachedToTerrain = silk.mode == SilkMode.AttachedToTerrain;

                if (self.input[0].y != 0 && attachedToTerrain)
                {
                    if (self.input[0].y > 0)
                    {
                        MovePlayerVertically(self, silk, 1f);

                        float currentDist = Vector2.Distance(self.bodyChunks[0].pos, silk.pos);
                        const float REEL_STEP = 4f;
                        float upperBound = Mathf.Max(currentDist, MIN_ROPE_VISIBLE);
                        silk.idealRopeLength = Mathf.Clamp(silk.idealRopeLength - REEL_STEP, MIN_ROPE_VISIBLE, upperBound);

                        if (silk.requestedRopeLength < MIN_ROPE_VISIBLE)
                            silk.requestedRopeLength = MIN_ROPE_VISIBLE;
                    }
                    else if (self.input[0].y < 0)
                    {
                        MovePlayerVertically(self, silk, -1f);

                        const float UNREEL_STEP = 4f;
                        silk.idealRopeLength = Mathf.Clamp(silk.idealRopeLength + UNREEL_STEP, MIN_ROPE_VISIBLE, 800f);
                    }
                }

                if (self.input[0].x != 0)
                {
                    Vector2 toAnchor = (silk.pos - self.bodyChunks[0].pos).normalized;
                    Vector2 perpendicular = PerpendicularVector(toAnchor);
                    float swingForce = self.input[0].x * 0.5f;

                    for (int i = 0; i < self.bodyChunks.Length; i++)
                    {
                        self.bodyChunks[i].vel += perpendicular * swingForce;
                        if (Mathf.Abs(toAnchor.x) > 0.3f)
                            self.bodyChunks[i].vel.y -= 0.3f;
                    }
                }
            }
            if (!inBridgeMode && !SilkClimb.IsClimbing(self) && self.Consious && self.bodyMode != Player.BodyModeIndex.CorridorClimb)
            {
                if (self.input[0].y > 0)
                {
                    Vector2 checkPos = self.mainBodyChunk.pos + new Vector2(0f, 15f);
                    SilkBridge closestBridge = SilkBridgeManager.GetClosestBridge(self.room, checkPos, 40f);

                    if (closestBridge != null)
                    {
                        int segIndex;
                        float t;
                        Vector2 closestPoint = closestBridge.GetClosestPoint(checkPos, out segIndex, out t);

                        SilkClimb.AttachPlayerToSilk(self, closestBridge, segIndex, t);
                        self.Blink(5);
                        self.room.PlaySound(SoundID.Player_Grab_Pole_Mimic, self.mainBodyChunk.pos, 1f, 1f);
                        return;
                    }
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
    }
}