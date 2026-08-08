using System.Collections.Generic;
using System.Runtime.CompilerServices;
using tinker;
using tinker.Mouse;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    /// <summary>
    /// Per-player gamepad state for SilkBridge aiming and shooting.
    /// LT → aim mode (right stick controls cursor on screen)
    /// RT → shoot silk / hold for bridge mode
    /// </summary>
    public class GamepadBridgeState
    {
        // ── Constants ──────────────────────────────────────────
        public const float INITIAL_CURSOR_DIST = 300f;

        // ── State ──────────────────────────────────────────────
        public bool aiming;
        public bool selectingBridge;
        public bool gamepadConnected;
        public Vector2 aimWorldPos;
        public Vector2 firstTargetWorldPos;
        public Vector2 bridgeAnchorWorldPos;
        public bool rtHeld;
        public float rtHoldSeconds;
        public int playerNumber;

        // ── Pool / lifecycle ───────────────────────────────────
        private static readonly ConditionalWeakTable<Player, GamepadBridgeState> _states =
            new ConditionalWeakTable<Player, GamepadBridgeState>();
        private static readonly Dictionary<int, Vector2> savedCursorScreenPositions = new();

        public static GamepadBridgeState GetOrCreate(Player player)
        {
            if (_states.TryGetValue(player, out var state))
                return state;

            state = new GamepadBridgeState();
            _states.Add(player, state);
            return state;
        }

        public static void Cleanup(Player player)
        {
            _states.Remove(player);
        }

        public void Initialize(Player player)
        {
            playerNumber = player.playerState?.playerNumber ?? 0;
            aiming = false;
            selectingBridge = false;
            gamepadConnected = false;
            rtHeld = false;
            rtHoldSeconds = 0f;

            // Place cursor at INITIAL_CURSOR_DIST in front of player
            Vector2 dir = new Vector2(player.flipDirection, 0f);
            aimWorldPos = player.mainBodyChunk.pos + dir * INITIAL_CURSOR_DIST;
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>Restore this player's last screen-space cursor position on aim enter.</summary>
        public void EnterAimMode(Player player)
        {
            aiming = true;
            selectingBridge = false;
            playerNumber = player.playerState?.playerNumber ?? 0;

            var cam = MouseAimSystem.GetCurrentCamera();
            if (cam != null)
            {
                if (!savedCursorScreenPositions.TryGetValue(playerNumber, out Vector2 screenPos))
                    screenPos = cam.sSize * 0.5f;

                aimWorldPos = cam.pos + screenPos;
            }
            else
            {
                Vector2 dir = new Vector2(player.flipDirection, 0f);
                aimWorldPos = player.mainBodyChunk.pos + dir * INITIAL_CURSOR_DIST;
            }
            ClampToCamera();
            SaveCursorScreenPosition();
        }

        public void ExitAimMode()
        {
            aiming = false;
            selectingBridge = false;
        }

        public void UpdateAim(Vector2 stick)
        {
            if (!aiming) return;
            aimWorldPos += stick * Options_Hook.GamepadCursorSpeed * Time.unscaledDeltaTime;
            ClampToCamera();
            SaveCursorScreenPosition();
        }

        /// <summary>Clamp cursor world pos to current camera bounds.</summary>
        public void ClampToCamera()
        {
            var cam = MouseAimSystem.GetCurrentCamera();
            if (cam == null) return;
            aimWorldPos.x = Mathf.Clamp(aimWorldPos.x, cam.pos.x, cam.pos.x + cam.sSize.x);
            aimWorldPos.y = Mathf.Clamp(aimWorldPos.y, cam.pos.y, cam.pos.y + cam.sSize.y);
        }

        private void SaveCursorScreenPosition()
        {
            var cam = MouseAimSystem.GetCurrentCamera();
            if (cam != null)
                savedCursorScreenPositions[playerNumber] = aimWorldPos - cam.pos;
        }

        public void BeginRtPress()
        {
            rtHeld = true;
            rtHoldSeconds = 0f;
            firstTargetWorldPos = aimWorldPos;
        }

        public void UpdateRtHold()
        {
            if (rtHeld)
                rtHoldSeconds += Time.unscaledDeltaTime;
        }

        public void BeginBridgeSelection(Vector2 anchorWorldPos)
        {
            selectingBridge = true;
            bridgeAnchorWorldPos = anchorWorldPos;
        }

        public void OnRTRelease()
        {
            rtHeld = false;
            rtHoldSeconds = 0f;
        }

        public void Cancel(bool keepGamepadConnected = false)
        {
            aiming = false;
            selectingBridge = false;
            if (!keepGamepadConnected)
                gamepadConnected = false;
            rtHeld = false;
            rtHoldSeconds = 0f;
        }
    }
}
