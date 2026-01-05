using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using tinker.Silk;
using UnityEngine;

namespace Tinker.Player_GrabUpdate
{
    [HarmonyPatch(typeof(Player), nameof(Player.GrabUpdate))]
    public static class EnableEatWhileHanging
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);
            var distLessMethod = AccessTools.Method(typeof(RWCustom.Custom), nameof(RWCustom.Custom.DistLess), new[] { typeof(Vector2), typeof(Vector2), typeof(float) });

            int flagIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Stloc_0 || codes[i].opcode == OpCodes.Stloc_S)
                {
                    flagIndex = i;
                    break;
                }
            }

            if (flagIndex != -1)
            {
                codes.Insert(flagIndex, new CodeInstruction(OpCodes.Ldarg_0));
                codes.Insert(flagIndex + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(tinkerSilkData), nameof(tinkerSilkData.Get))));
                codes.Insert(flagIndex + 2, new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(SilkPhysics), nameof(SilkPhysics.Attached))));
                codes.Insert(flagIndex + 3, new CodeInstruction(OpCodes.Or));
            }

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(distLessMethod))
                {
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(tinkerSilkData), nameof(tinkerSilkData.Get))));
                    codes.Insert(i + 3, new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(SilkPhysics), nameof(SilkPhysics.Attached))));
                    codes.Insert(i + 4, new CodeInstruction(OpCodes.Or));
                    break;
                }
            }

            return codes;
        }
    }
}