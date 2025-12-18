using System;
using tinker;

namespace Tinker
{
    public class TheTinker
    {
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
    }
}