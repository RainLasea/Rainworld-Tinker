using RWCustom;
using System.Runtime.CompilerServices;
using Tinker.Silk.Bridge;
using UnityEngine;
using static Tinker.Silk.Bridge.BridgeModeState;

namespace tinker.Mouse
{
    public static class MouseAimSystem
    {
        private sealed class CameraView
        {
            public Room Room;
            public Vector2 Position;
        }

        private static ConditionalWeakTable<RoomCamera, CameraView> cameraViews = new();
        private static bool initialized;

        public static RoomCamera GetCurrentCamera(Player player)
        {
            if (player?.room?.game?.cameras == null) return null;
            RoomCamera roomCamera = null;
            foreach (var camera in player.room.game.cameras)
            {
                if (camera?.room != player.room) continue;
                if (camera.followAbstractCreature == player.abstractCreature) return camera;
                if (roomCamera == null) roomCamera = camera;
            }
            return roomCamera;
        }

        internal static Vector2 CameraPosition(RoomCamera camera) =>
            cameraViews.TryGetValue(camera, out var view) && view.Room == camera.room ? view.Position : camera.pos;

        // Futile handles window scaling; the world container handles camera zoom
        // and viewport transforms. Use the same origin that drew the player.
        public static bool TryGetMouseWorldPosition(Player player, out Vector2 position)
        {
            var camera = GetCurrentCamera(player);
            position = Vector2.zero;
            if (camera == null) return false;
            position = camera.ReturnFContainer("Midground").ScreenToLocal(Futile.mousePosition) + CameraPosition(camera);
            return true;
        }

        internal static Vector2 WorldToHud(RoomCamera camera, FContainer hud, Vector2 position) =>
            hud.OtherToLocal(camera.ReturnFContainer("Midground"), position - CameraPosition(camera));

        public static bool IsGamepadActive(Player player) => player != null &&
            (player.input[0].gamePad || GamepadBridgeState.GetOrCreate(player).gamepadConnected);

        internal static bool IsLocalPlayer(Player player)
        {
            if (player?.room == null || player.slatedForDeletetion || player.isSlugpup ||
                player.slugcatStats.name != Plugin.SlugName) return false;
            if (Silk.RainMeadow.RainMeadowBridge.IsRainMeadowLoaded)
                return !Silk.RainMeadow.RainMeadowBridge.IsOnlineAndRemote(player);
            string controller = player.controller?.GetType().Name;
            return controller == null || controller == "KeyboardController" || controller == "JoystickController";
        }

        public static bool IsMouseAimEnabled(Player player) => Options_Hook.MouseAimEnabled && IsLocalPlayer(player);

        public static Vector2 GetAimDirection(Player player) => GetAimDirection(player, player.mainBodyChunk.pos);

        public static Vector2 GetAimDirection(Player player, Vector2 origin)
        {
            Vector2 direction;
            if (IsGamepadActive(player))
            {
                var state = GamepadBridgeState.GetOrCreate(player);
                direction = state.aiming ? state.aimWorldPos - origin : Vector2.right * player.flipDirection;
            }
            else if (TryGetMouseWorldPosition(player, out var target)) direction = target - origin;
            else direction = new Vector2(player.input[0].x, player.input[0].y);
            return direction.sqrMagnitude < 0.01f ? Vector2.right * (player.flipDirection < 0 ? -1f : 1f) : direction.normalized;
        }

        public static void Initialize()
        {
            if (initialized) return;
            On.Weapon.Thrown += Weapon_Thrown;
            On.RWInput.PlayerInputLogic_int_int += PlayerInputLogic;
            On.PlayerGraphics.DrawSprites += PlayerGraphics_DrawSprites;
            initialized = true;
        }

        private static void PlayerGraphics_DrawSprites(On.PlayerGraphics.orig_DrawSprites orig, PlayerGraphics self,
            RoomCamera.SpriteLeaser leaser, RoomCamera camera, float timeStacker, Vector2 camPos)
        {
            var view = cameraViews.GetValue(camera, _ => new CameraView());
            view.Room = camera.room;
            view.Position = camPos;
            orig(self, leaser, camera, timeStacker, camPos);
        }

        private static void Weapon_Thrown(On.Weapon.orig_Thrown orig, Weapon weapon, Creature thrownBy,
            Vector2 thrownPos, Vector2? firstFrameTraceFromPos, IntVector2 throwDir, float frc, bool eu)
        {
            if (thrownBy is not Player player || !IsMouseAimEnabled(player) ||
                (IsGamepadActive(player) && !GamepadBridgeState.GetOrCreate(player).aiming) ||
                (!IsGamepadActive(player) && GetCurrentCamera(player) == null))
            {
                orig(weapon, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);
                return;
            }

            Vector2 aim = GetAimDirection(player, thrownPos);
            // Vanilla still needs a cardinal direction for collision bookkeeping.
            // Pass it through Thrown instead of reflecting into a protected field.
            throwDir = Mathf.Abs(aim.x) >= Mathf.Abs(aim.y)
                ? new IntVector2(aim.x < 0f ? -1 : 1, 0)
                : new IntVector2(0, aim.y < 0f ? -1 : 1);
            orig(weapon, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);
            float speed = weapon.firstChunk.vel.magnitude;
            foreach (var chunk in weapon.bodyChunks) chunk.vel = aim * speed;
            weapon.setRotation = aim;
            // The vanilla three-tick correction otherwise turns aimed throws
            // back towards the player's movement/facing direction.
            weapon.changeDirCounter = 0;
        }

        private static Player.InputPackage PlayerInputLogic(On.RWInput.orig_PlayerInputLogic_int_int orig,
            int categoryID, int playerNumber)
        {
            var input = orig(categoryID, playerNumber);
            if (categoryID != 0 || input.gamePad ||
                Custom.rainWorld?.processManager?.currentMainLoop is not RainWorldGame game || game.paused) return input;
            foreach (var creature in game.Players)
            {
                if (creature.realizedCreature is not Player player || player.playerState.playerNumber != playerNumber ||
                    !IsMouseAimEnabled(player) || IsGamepadActive(player)) continue;
                if (Input.GetKey(KeyCode.E)) input.pckp = true;
                var bridge = SilkBridgeManager.GetBridgeModeState(player);
                // Reserve the click immediately while the silk button is held,
                // including the tick before build mode has been activated.
                bool building = bridge?.active == true || bridge?.animating == true ||
                    (Input.GetKey(Options_Hook.SilkShootKey) && Silk.tinkerSilkData.Get(player).Attached);
                if (Input.GetMouseButton(0)) input.thrw = !building;
                break;
            }
            return input;
        }

        public static void Cleanup()
        {
            if (!initialized) return;
            On.Weapon.Thrown -= Weapon_Thrown;
            On.RWInput.PlayerInputLogic_int_int -= PlayerInputLogic;
            On.PlayerGraphics.DrawSprites -= PlayerGraphics_DrawSprites;
            cameraViews = new();
            initialized = false;
        }
    }
}
