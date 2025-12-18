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
            if (mouseAimEnabled && thrownBy is Player && thrownBy == currentPlayer)
            {
                weapon.thrownBy = thrownBy;
                weapon.thrownPos = thrownPos;
                weapon.firstFrameTraceFromPos = firstFrameTraceFromPos;
                weapon.changeDirCounter = 3;
                weapon.ChangeOverlap(true);
                weapon.firstChunk.MoveFromOutsideMyUpdate(eu, thrownPos);

                Vector2 mouseWorldPos = new Vector2(Futile.mousePosition.x + currentCamera.pos.x, Futile.mousePosition.y + currentCamera.pos.y);
                Vector2 aimVector = Vector2.ClampMagnitude(mouseWorldPos - thrownPos, 0.03f) * 10f;

                Vector2 normalizedAim = aimVector.normalized;
                IntVector2 computedThrowDir = Mathf.Abs(normalizedAim.x) > Mathf.Abs(normalizedAim.y)
                    ? new IntVector2(normalizedAim.x > 0 ? 1 : -1, 0)
                    : new IntVector2(0, normalizedAim.y > 0 ? 1 : -1);

                weaponThrowDirField.SetValue(weapon, computedThrowDir);

                foreach (BodyChunk bodyChunk in weapon.bodyChunks)
                {
                    bodyChunk.pos = thrownBy.mainBodyChunk.pos + aimVector;
                    bodyChunk.vel = aimVector * 160f;
                }

                weapon.setRotation = aimVector;
                weapon.overrideExitThrownSpeed = frc >= 1f ? 0f : Mathf.Min(weapon.exitThrownModeSpeed, frc * 20f);
                weapon.ChangeMode(Weapon.Mode.Thrown);
                weapon.rotationSpeed = 0f;
                weapon.meleeHitChunk = null;
            }
            else
            {
                orig(weapon, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);
            }
        }

        private static Player.InputPackage PlayerInputLogic(On.RWInput.orig_PlayerInputLogic_int_int orig, int categoryID, int playerNumber)
        {
            Player.InputPackage inputPackage = orig(categoryID, playerNumber);

            if (mouseAimEnabled && playerNumber == currentPlayerNumber && currentPlayer != null)
            {
                bool inGame = RWCustom.Custom.rainWorld.processManager.currentMainLoop is RainWorldGame;


                bool isInBuildMode = false;
                var bridgeState = SilkBridgeManager.GetBridgeModeState(currentPlayer);
                if (bridgeState != null)
                {
                    isInBuildMode = bridgeState.active;
                }


                if (inGame && Input.GetKey(KeyCode.E) && playerNumber == currentPlayerNumber)
                    inputPackage.pckp = true;


                if (inGame && Input.GetKey(KeyCode.Mouse0) && playerNumber == currentPlayerNumber && !isInBuildMode)
                    inputPackage.thrw = true;
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