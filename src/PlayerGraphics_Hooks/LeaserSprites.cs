using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Tinker.PlayerGraphics_Hooks
{
    // Physics belongs to PlayerGraphics; sprites belong to one camera's SpriteLeaser.
    internal sealed class LeaserSprites
    {
        private ConditionalWeakTable<RoomCamera.SpriteLeaser, FSprite[]> spritesByLeaser = new();
        private readonly List<WeakReference<FSprite[]>> allocations = new();

        public void Add(RoomCamera.SpriteLeaser leaser, params FSprite[] sprites)
        {
            if (spritesByLeaser.TryGetValue(leaser, out var previous))
                foreach (var sprite in previous) sprite.RemoveFromContainer();
            spritesByLeaser.Remove(leaser);
            spritesByLeaser.Add(leaser, sprites);
            allocations.RemoveAll(reference => !reference.TryGetTarget(out _));
            allocations.Add(new WeakReference<FSprite[]>(sprites));

            int start = leaser.sprites.Length;
            Array.Resize(ref leaser.sprites, start + sprites.Length);
            Array.Copy(sprites, 0, leaser.sprites, start, sprites.Length);
        }

        public bool TryGet(RoomCamera.SpriteLeaser leaser, out FSprite[] sprites)
        {
            // orig.InitiateSprites can replace the array before orig.AddToContainer runs.
            return spritesByLeaser.TryGetValue(leaser, out sprites) &&
                   leaser.sprites != null && Array.IndexOf(leaser.sprites, sprites[0]) >= 0;
        }

        public void Cleanup()
        {
            foreach (var reference in allocations)
                if (reference.TryGetTarget(out var sprites))
                    foreach (var sprite in sprites) sprite.RemoveFromContainer();
            allocations.Clear();
            spritesByLeaser = new();
        }
    }
}
