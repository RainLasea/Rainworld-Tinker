using System;
using System.Collections.Generic;
using Rewired;
using RWCustom;
using tinker;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    internal enum GamepadInputBackend
    {
        None,
        RewiredRaw,
        XInput
    }

    internal struct GamepadSnapshot
    {
        public bool connected;
        public GamepadInputBackend backend;
        public string deviceName;
        public Vector2 rightStickRaw;
        public Vector2 rightStick;
        public bool ltHeld;
        public bool ltPressed;
        public bool ltReleased;
        public bool rtHeld;
        public bool rtPressed;
        public bool rtReleased;
    }

    internal static class GamepadInputReader
    {
        private const int RIGHT_STICK_X_AXIS = 2;
        private const int RIGHT_STICK_Y_AXIS = 3;
        private const int LEFT_TRIGGER_AXIS = 4;
        private const int RIGHT_TRIGGER_AXIS = 5;
        private const float TRIGGER_THRESHOLD = 0.5f;

        private static readonly Dictionary<int, bool> previousLtHeld = new();
        private static readonly Dictionary<int, bool> previousRtHeld = new();

        public static GamepadSnapshot Sample(Player player, int playerNumber)
        {
            GamepadSnapshot snapshot = default;
            if (player == null || playerNumber < 0 || !player.input[0].gamePad)
            {
                previousLtHeld.Remove(playerNumber);
                previousRtHeld.Remove(playerNumber);
                return snapshot;
            }

            // Vanilla RWInput has already selected this player's active controller.
            snapshot.connected = true;

            if (!OptionalImprovedInput.TryReadTriggers(player, out snapshot.ltHeld, out snapshot.rtHeld))
                TryReadDefaultTriggers(playerNumber, out snapshot.ltHeld, out snapshot.rtHeld);

            bool previousLt = previousLtHeld.GetValueOrDefault(playerNumber);
            bool previousRt = previousRtHeld.GetValueOrDefault(playerNumber);
            previousLtHeld[playerNumber] = snapshot.ltHeld;
            previousRtHeld[playerNumber] = snapshot.rtHeld;
            snapshot.ltPressed = snapshot.ltHeld && !previousLt;
            snapshot.ltReleased = !snapshot.ltHeld && previousLt;
            snapshot.rtPressed = snapshot.rtHeld && !previousRt;
            snapshot.rtReleased = !snapshot.rtHeld && previousRt;

            string requestedBackend = Options_Hook.GamepadBackend;
            bool allowRewired = requestedBackend != "XInput";
            bool allowXInput = requestedBackend != "RewiredRaw";

            if (allowRewired && TryReadRewiredRaw(playerNumber, out Vector2 rawStick, out string deviceName))
            {
                snapshot.backend = GamepadInputBackend.RewiredRaw;
                snapshot.deviceName = deviceName;
                snapshot.rightStickRaw = rawStick;
            }
            else if (allowXInput && XInputReader.IsControllerConnected(playerNumber))
            {
                snapshot.backend = GamepadInputBackend.XInput;
                snapshot.deviceName = "XInput slot " + playerNumber;
                snapshot.rightStickRaw = XInputReader.GetRightStick(playerNumber);
            }

            snapshot.rightStick = ApplyRadialDeadzone(snapshot.rightStickRaw, Options_Hook.GamepadDeadzone);
            return snapshot;
        }

        public static void Cleanup()
        {
            previousLtHeld.Clear();
            previousRtHeld.Clear();
        }

        private static bool TryReadRewiredRaw(int playerNumber, out Vector2 stick, out string deviceName)
        {
            stick = Vector2.zero;
            deviceName = null;

            try
            {
                Rewired.Player rewiredPlayer = Custom.rainWorld?.options?.controls?[playerNumber]?.player;
                if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return false;

                Joystick joystick = rewiredPlayer.controllers.Joysticks[0];
                stick = new Vector2(joystick.GetAxisRaw(RIGHT_STICK_X_AXIS), -joystick.GetAxisRaw(RIGHT_STICK_Y_AXIS));
                deviceName = joystick.name ?? "Rewired joystick";
                return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        private static void TryReadDefaultTriggers(int playerNumber, out bool ltHeld, out bool rtHeld)
        {
            ltHeld = false;
            rtHeld = false;

            try
            {
                Rewired.Player rewiredPlayer = Custom.rainWorld?.options?.controls?[playerNumber]?.player;
                if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return;

                Joystick joystick = rewiredPlayer.controllers.Joysticks[0];
                if (joystick.axisCount > LEFT_TRIGGER_AXIS)
                    ltHeld = joystick.GetAxisRaw(LEFT_TRIGGER_AXIS) > TRIGGER_THRESHOLD;
                if (joystick.axisCount > RIGHT_TRIGGER_AXIS)
                    rtHeld = joystick.GetAxisRaw(RIGHT_TRIGGER_AXIS) > TRIGGER_THRESHOLD;
            }
            catch (Exception)
            {
            }
        }

        private static Vector2 ApplyRadialDeadzone(Vector2 stick, float deadzone)
        {
            float magnitude = stick.magnitude;
            if (magnitude <= deadzone || magnitude < 0.0001f)
                return Vector2.zero;

            float scaledMagnitude = Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
            return stick / magnitude * scaledMagnitude;
        }

    }
}