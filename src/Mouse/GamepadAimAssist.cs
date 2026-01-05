//using Rewired;
//using UnityEngine;

//namespace tinker.Mouse
//{
//    public static class GamepadAimAssist
//    {
//        private const float MAX_AIM_DIST = 500f;
//        private const float CURSOR_SPEED = 20f;
//        private const float MAGNET_RADIUS = 70f;
//        private const float SNAP_STRENGTH = 0.25f;
//        private const float DEADZONE = 0.15f;
//        private const float FADE_OUT_TIME = 2.5f;

//        private const int L2_AXIS = 4;
//        private const int R2_AXIS = 5;
//        private const float TRIGGER_THRESHOLD = 0.5f;

//        public static Vector2 virtualCursorPos;
//        private static float cursorIdleTimer = 0f;
//        public static float cursorOpacity = 0f;

//        public static void Update(global::Player player, RoomCamera cam)
//        {
//            if (player == null || player.room == null || cam == null) return;

//            var rewiredPlayer = ReInput.players.GetPlayer(player.playerState.playerNumber);
//            if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return;

//            Joystick joystick = rewiredPlayer.controllers.Joysticks[0];
//            float rawX = joystick.GetAxisRaw(2);
//            float rawY = -joystick.GetAxisRaw(3);

//            Vector2 stickInput = new Vector2(rawX, rawY);

//            if (stickInput.magnitude < DEADZONE)
//            {
//                cursorIdleTimer += Time.deltaTime;
//                if (cursorIdleTimer > FADE_OUT_TIME)
//                {
//                    cursorOpacity = Mathf.Lerp(cursorOpacity, 0f, 0.05f);
//                }
//                ApplyConstraints(player);
//                return;
//            }

//            cursorIdleTimer = 0f;
//            cursorOpacity = Mathf.Lerp(cursorOpacity, 1f, 0.15f);

//            if (virtualCursorPos == Vector2.zero)
//            {
//                virtualCursorPos = player.mainBodyChunk.pos;
//            }

//            virtualCursorPos += stickInput * CURSOR_SPEED;

//            Vector2? target = FindMagnetTarget(player, virtualCursorPos);
//            if (target.HasValue)
//            {
//                virtualCursorPos = Vector2.Lerp(virtualCursorPos, target.Value, SNAP_STRENGTH);
//            }

//            ApplyConstraints(player);
//        }

//        public static bool IsL2Pressed(global::Player player)
//        {
//            var rewiredPlayer = ReInput.players.GetPlayer(player.playerState.playerNumber);
//            if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return false;
//            return rewiredPlayer.controllers.Joysticks[0].GetAxisRaw(L2_AXIS) > TRIGGER_THRESHOLD;
//        }

//        public static bool IsR2Pressed(global::Player player)
//        {
//            var rewiredPlayer = ReInput.players.GetPlayer(player.playerState.playerNumber);
//            if (rewiredPlayer == null || rewiredPlayer.controllers.joystickCount == 0) return false;
//            return rewiredPlayer.controllers.Joysticks[0].GetAxisRaw(R2_AXIS) > TRIGGER_THRESHOLD;
//        }

//        private static void ApplyConstraints(global::Player player)
//        {
//            Vector2 playerPos = player.mainBodyChunk.pos;
//            float dist = Vector2.Distance(playerPos, virtualCursorPos);
//            if (dist > MAX_AIM_DIST)
//            {
//                virtualCursorPos = playerPos + (virtualCursorPos - playerPos).normalized * MAX_AIM_DIST;
//            }
//            else if (dist < 20f)
//            {
//                virtualCursorPos = playerPos + (Vector2.right * player.flipDirection * 20f);
//            }
//        }

//        private static Vector2? FindMagnetTarget(global::Player player, Vector2 searchPos)
//        {
//            float bestDist = MAGNET_RADIUS;
//            Vector2? bestTarget = null;

//            for (int i = 0; i < player.room.physicalObjects.Length; i++)
//            {
//                for (int j = 0; j < player.room.physicalObjects[i].Count; j++)
//                {
//                    var obj = player.room.physicalObjects[i][j];
//                    if (obj is Creature crit && crit != player && !crit.dead && !crit.Template.smallCreature)
//                    {
//                        float d = Vector2.Distance(searchPos, crit.mainBodyChunk.pos);
//                        if (d < bestDist)
//                        {
//                            bestDist = d;
//                            bestTarget = crit.mainBodyChunk.pos;
//                        }
//                    }
//                }
//            }
//            return bestTarget;
//        }
//    }
//}