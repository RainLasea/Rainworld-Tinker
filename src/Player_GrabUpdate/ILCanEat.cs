using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using tinker.Silk;
using UnityEngine;

namespace Tinker.Player_GrabUpdate
{
    [HarmonyPatch(typeof(Player), nameof(Player.GrabUpdate), new[] { typeof(bool) })]
    public static class EnableEatWhileHanging
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var distLess = AccessTools.Method(typeof(RWCustom.Custom), nameof(RWCustom.Custom.DistLess),
                new[] { typeof(Vector2), typeof(Vector2), typeof(float) });
            int match = -1;
            int matches = 0;
            // GrabUpdate's eating movement check is DistLess(mainBodyChunk.pos,
            // mainBodyChunk.lastPos, 3.6f). Never guess a local-variable number.
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].Calls(distLess) && codes[i - 1].opcode == OpCodes.Ldc_R4 &&
                    codes[i - 1].operand is float distance && distance == 3.6f)
                {
                    match = i;
                    matches++;
                }
            }
            if (matches != 1)
            {
                Debug.LogWarning("[Tinker] GrabUpdate eating check changed; leaving vanilla eating intact.");
                return codes;
            }

            // Move branch/exception metadata so incoming control flow also loads the player.
            var loadPlayer = new CodeInstruction(OpCodes.Ldarg_0);
            loadPlayer.labels.AddRange(codes[match].labels);
            loadPlayer.blocks.AddRange(codes[match].blocks);
            codes[match].labels.Clear();
            codes[match].blocks.Clear();
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = AccessTools.Method(typeof(EnableEatWhileHanging), nameof(CanEatWhileMoving));
            codes.Insert(match, loadPlayer);
            return codes;
        }

        public static bool CanEatWhileMoving(Vector2 position, Vector2 lastPosition, float distance, Player player)
        {
            if (tinkerSilkData.IsTinkerPlayer(player) && tinkerSilkData.Get(player).Attached)
                return player.input[0].pckp;
            return RWCustom.Custom.DistLess(position, lastPosition, distance);
        }
    }
}
