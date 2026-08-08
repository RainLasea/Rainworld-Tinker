using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    /// <summary>
    /// Reads raw gamepad state via native XInput (xinput1_4.dll).
    /// Bypasses Rewired/Unity Input to reliably read right stick values
    /// that Rain World's Rewired config doesn't define.
    /// </summary>
    internal static class XInputReader
    {
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState_(int dwUserIndex, out XINPUT_STATE pState);

        /// <summary>
        /// Get right stick value normalized to [-1, 1] range.
        /// Returns Vector2.zero if no controller connected or error.
        /// </summary>
        public static Vector2 GetRightStick(int playerIndex = 0)
        {
            try
            {
                XINPUT_STATE state = default;
                int result = XInputGetState_(Mathf.Clamp(playerIndex, 0, 3), out state);

                if (result != ERROR_SUCCESS)
                    return Vector2.zero;

                short rawX = state.Gamepad.sThumbRX;
                short rawY = state.Gamepad.sThumbRY;

                float x = rawX < 0 ? rawX / 32768f : rawX / 32767f;
                float y = rawY < 0 ? rawY / 32768f : rawY / 32767f;

                return new Vector2(x, y);
            }
            catch (DllNotFoundException)
            {
                return Vector2.zero;
            }
            catch (EntryPointNotFoundException)
            {
                return Vector2.zero;
            }
        }

        /// <summary>Check if an Xbox controller is connected.</summary>
        public static bool IsControllerConnected(int playerIndex = 0)
        {
            try
            {
                int result = XInputGetState_(Mathf.Clamp(playerIndex, 0, 3), out _);
                return result == ERROR_SUCCESS;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
