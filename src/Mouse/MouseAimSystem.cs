using RWCustom;
using System.Reflection;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Mouse
{
    public static class MouseAimSystem
    {
        private static FieldInfo weaponThrowDirField;
        private static bool reflectionInitialized = false;
        private static RoomCamera currentCamera;
        private static bool mouseAimEnabled = false;
        private static Player currentPlayer;
        private static int currentPlayerNumber = 0;

        public static RoomCamera GetCurrentCamera() => currentCamera;

        public static bool IsGamepadActive(Player player)
        {
            try { return player != null && player.input[0].gamePad; }
            catch { return false; }
        }

        public static Vector2 GetAimDirection(Player player)
        {
            var cam = GetCurrentCamera();
            Vector2 aimVector;

            if (cam != null)
            {
                // Check for gamepad aim first
                bool gamepadActive = IsGamepadActive(player);
                if (gamepadActive)
                {
                    var gpState = GamepadBridgeState.GetOrCreate(player);
                    if (gpState.aiming)
                    {
                        aimVector = gpState.aimWorldPos - player.mainBodyChunk.pos;
                    }
                    else
                    {
                        aimVector = Vector2.right * player.flipDirection;
                    }
                }
                else
                {
                    Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
                    aimVector = mouseWorldPos - player.mainBodyChunk.pos;
                }
            }
            else
            {
                aimVector = player.bodyChunks[0].vel.magnitude > 0.5f
                    ? player.bodyChunks[0].vel
                    : new Vector2(player.input[0].x, player.input[0].y);
            }

            if (aimVector.magnitude < 0.1f)
                aimVector = Vector2.right * player.flipDirection;

            return aimVector.normalized;
        }

        public static void Initialize()
        {
            InitializeReflection();
            On.Weapon.Thrown += Weapon_Thrown;
            On.RWInput.PlayerInputLogic_int_int += PlayerInputLogic;
            On.RoomCamera.ctor += RoomCamera_ctor;
        }

        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;
            weaponThrowDirField = typeof(Weapon).GetField("throwDir", BindingFlags.NonPublic | BindingFlags.Instance);
            reflectionInitialized = true;
        }

        private static void RoomCamera_ctor(On.RoomCamera.orig_ctor orig, RoomCamera self, RainWorldGame game, int cameraNumber)
        {
            orig(self, game, cameraNumber);
            currentCamera = self;
        }

        public static void SetMouseAimEnabled(bool enabled, Player player)
        {
            mouseAimEnabled = enabled;
            currentPlayer = player;
            if (player?.playerState != null)
                currentPlayerNumber = player.playerState.playerNumber;
        }

        public static bool IsMouseAimEnabled() => mouseAimEnabled && currentPlayer != null;

        private static void Weapon_Thrown(On.Weapon.orig_Thrown orig, Weapon weapon, Creature thrownBy, Vector2 thrownPos, Vector2? firstFrameTraceFromPos, IntVector2 throwDir, float frc, bool eu)
        {
            orig(weapon, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);

            if (mouseAimEnabled && thrownBy is Player player && player == currentPlayer)
            {
                bool isTinker = player.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !player.isSlugpup;
                if (isTinker && currentCamera != null)
                {
                    Vector2 aimDir;

                    // Gamepad branch: aim at gamepad cursor position
                    if (IsGamepadActive(player))
                    {
                        var gpState = GamepadBridgeState.GetOrCreate(player);
                        if (gpState.aiming)
                            aimDir = (gpState.aimWorldPos - thrownPos).normalized;
                        else
                            aimDir = new Vector2(player.flipDirection, 0f).normalized;
                    }
                    else
                    {
                        // Mouse branch: aim at mouse cursor position
                        Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + currentCamera.pos.x, Futile.mousePosition.y + currentCamera.pos.y);
                        aimDir = (mouseWorldPos - thrownPos).normalized;
                    }

                    float originalSpeed = weapon.firstChunk.vel.magnitude;

                    foreach (BodyChunk bodyChunk in weapon.bodyChunks)
                    {
                        bodyChunk.vel = aimDir * originalSpeed;
                    }
                    weapon.setRotation = aimDir;
                }
            }
        }

        private static Player.InputPackage PlayerInputLogic(On.RWInput.orig_PlayerInputLogic_int_int orig, int categoryID, int playerNumber)
        {
            Player.InputPackage inputPackage = orig(categoryID, playerNumber);

            if (!mouseAimEnabled || playerNumber != currentPlayerNumber || currentPlayer == null)
            {
                return inputPackage;
            }

            // ── Gamepad detected: skip keyboard/mouse input injection ──
            // Trigger input is sampled separately by GamepadInputReader.
            if (IsGamepadActive(currentPlayer))
                return inputPackage;

            bool isTinker = currentPlayer.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !currentPlayer.isSlugpup;
            if (!isTinker) return inputPackage;

            bool inGame = RWCustom.Custom.rainWorld.processManager.currentMainLoop is RainWorldGame;

            bool isInBuildMode = false;
            var bridgeState = SilkBridgeManager.GetBridgeModeState(currentPlayer);
            if (bridgeState != null)
            {
                isInBuildMode = bridgeState.active;
            }

            if (inGame)
            {
                if (Input.GetKey(KeyCode.E))
                    inputPackage.pckp = true;

                if (Input.GetMouseButton(0) && !isInBuildMode)
                {
                    inputPackage.thrw = true;
                }
            }

            return inputPackage;
        }

        public static void Cleanup()
        {
            On.Weapon.Thrown -= Weapon_Thrown;
            On.RWInput.PlayerInputLogic_int_int -= PlayerInputLogic;
            On.RoomCamera.ctor -= RoomCamera_ctor;
            reflectionInitialized = false;
            mouseAimEnabled = false;
            currentPlayer = null;
            currentPlayerNumber = 0;
        }
    }
}