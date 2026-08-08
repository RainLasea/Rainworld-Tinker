using System;
using System.Collections.Generic;
using Rewired;
using Rewired.ControllerExtensions;
using SlugBase.DataTypes;
using tinker;
using UnityEngine;

namespace Tinker
{
    public class TheTinker
    {
        private const float LightTransitionFrames = 30f;
        private static readonly Dictionary<Joystick, LightTransition> lightTransitions = new();

        private sealed class LightTransition
        {
            public Color current;
            public Color target;
            public float progress;
        }

        public bool IsTinker
        {
            get
            {
                Player player;
                bool flag = playerRef.TryGetTarget(out player);
                return flag && player.SlugCatClass == Plugin.SlugName;
            }
        }

        public TheTinker(Player player)
        {
            playerRef = new WeakReference<Player>(player);
        }
        public WeakReference<Player> playerRef;

        public static void UpdateDualSenseLight(Player player)
        {
            if (!Options_Hook.DualSenseLightEnabled || player?.graphicsModule is not PlayerGraphics graphics)
                return;

            int playerNumber = player.playerState?.playerNumber ?? -1;
            if (playerNumber < 0) return;

            try
            {
                Rewired.Player rewiredPlayer = RWCustom.Custom.rainWorld?.options?.controls?[playerNumber]?.player;
                if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return;

                Joystick joystick = rewiredPlayer.controllers.Joysticks[0];
                DualSenseExtension dualSense = joystick.GetExtension<DualSenseExtension>();
                if (dualSense == null) return;

                Color color = PlayerColor.GetCustomColor(graphics, GetDualSenseLightColorSource());
                color.a = 1f;
                if (!lightTransitions.TryGetValue(joystick, out LightTransition transition))
                {
                    dualSense.SetLightColor(color);
                    lightTransitions[joystick] = new LightTransition { current = color, target = color, progress = 1f };
                    return;
                }

                if (transition.target != color)
                {
                    transition.target = color;
                    transition.progress = 0f;
                }

                if (transition.progress >= 1f) return;

                transition.progress = Mathf.Min(1f, transition.progress + 1f / LightTransitionFrames);
                transition.current = Color.Lerp(transition.current, transition.target, Mathf.SmoothStep(0f, 1f, transition.progress));
                dualSense.SetLightColor(transition.current);
            }
            catch (Exception)
            {
            }
        }

        private static string GetDualSenseLightColorSource()
        {
            return Options_Hook.DualSenseLightColorSource switch
            {
                "Body" => "Body",
                "Eyes" => "Eyes",
                "AntennaTip" => "AntennaTip",
                _ => "AntennaBase"
            };
        }
    }
}