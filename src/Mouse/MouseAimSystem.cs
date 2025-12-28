using RWCustom;
using System.Reflection;
using UnityEngine;
using Tinker.Silk.Bridge;

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

        public static Vector2 GetAimDirection(Player player)
        {
            var cam = GetCurrentCamera();
            Vector2 aimVector;

            if (cam != null)
            {
                Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + cam.pos.x, Futile.mousePosition.y + cam.pos.y);
                aimVector = mouseWorldPos - player.mainBodyChunk.pos;
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
                if (isTinker)
                {
                    Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + currentCamera.pos.x, Futile.mousePosition.y + currentCamera.pos.y);
                    Vector2 aimVector = (mouseWorldPos - thrownPos).normalized;

                    float originalSpeed = weapon.firstChunk.vel.magnitude;

                    foreach (BodyChunk bodyChunk in weapon.bodyChunks)
                    {
                        bodyChunk.vel = aimVector * originalSpeed;
                    }
                    weapon.setRotation = aimVector;
                }
            }
        }

        private static Player.InputPackage PlayerInputLogic(On.RWInput.orig_PlayerInputLogic_int_int orig, int categoryID, int playerNumber)
        {
            Player.InputPackage inputPackage = orig(categoryID, playerNumber);

            if (mouseAimEnabled && playerNumber == currentPlayerNumber && currentPlayer != null)
            {
                bool isTinker = currentPlayer.slugcatStats.name.ToString() == Plugin.SlugName.ToString() && !currentPlayer.isSlugpup;
                if (!isTinker) return inputPackage;

                bool inGame = RWCustom.Custom.rainWorld.processManager.currentMainLoop is RainWorldGame;

                bool isInBuildMode = false;
                var bridgeState = SilkBridgeManager.GetBridgeModeState(currentPlayer);
                if (bridgeState != null)
                {
                    isInBuildMode = bridgeState.active;
                }

                if (inGame && Input.GetKey(KeyCode.E) && playerNumber == currentPlayerNumber)
                    inputPackage.pckp = true;

                if (inGame && Input.GetMouseButton(0) && playerNumber == currentPlayerNumber && !isInBuildMode)
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