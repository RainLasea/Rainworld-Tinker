using System;
using System.Linq;
using System.Reflection;
using tinker;
using UnityEngine;

namespace Tinker.Silk.Bridge
{
    internal static class OptionalImprovedInput
    {
        private static MethodInfo isPressedMethod;
        private static object silkAimKeybind;
        private static object silkShootKeybind;

        public static bool Available => isPressedMethod != null && silkAimKeybind != null && silkShootKeybind != null;

        public static void Initialize()
        {
            try
            {
                Type keybindType = FindType("ImprovedInput.PlayerKeybind");
                Type inputExtType = FindType("ImprovedInput.CustomInputExt");
                if (keybindType == null || inputExtType == null) return;

                MethodInfo register = keybindType.GetMethod("Register", new[]
                {
                    typeof(string), typeof(string), typeof(string), typeof(KeyCode), typeof(KeyCode)
                });
                isPressedMethod = inputExtType.GetMethod("IsPressed", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Player), keybindType }, null);
                if (register == null || isPressedMethod == null) return;

                silkAimKeybind = register.Invoke(null, new object[]
                {
                    "tinker.silkAim", "Tinker", "Silk Aim (LT)", KeyCode.None, KeyCode.JoystickButton14
                });
                silkShootKeybind = register.Invoke(null, new object[]
                {
                    "tinker.silkShoot", "Tinker", "Silk Shoot (RT)", KeyCode.None, KeyCode.JoystickButton15
                });
            }
            catch (Exception)
            {
                isPressedMethod = null;
                silkAimKeybind = null;
                silkShootKeybind = null;
            }
        }

        public static bool TryReadTriggers(Player player, out bool ltHeld, out bool rtHeld)
        {
            ltHeld = false;
            rtHeld = false;
            if (!Available || player == null) return false;

            try
            {
                ltHeld = (bool)isPressedMethod.Invoke(null, new[] { player, silkAimKeybind });
                rtHeld = (bool)isPressedMethod.Invoke(null, new[] { player, silkShootKeybind });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName + ", ImprovedInput");
            if (type != null) return type;
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
        }
    }
}