namespace Tinker.AncientBot
{
    internal class GenerateKeyDrone
    {
        public static AbstractPhysicalObject.AbstractObjectType UVsKeyDroneType;

        public static void RegisterValues()
        {
            UVsKeyDroneType = new AbstractPhysicalObject.AbstractObjectType("UVsKeyDrone", true);
        }

        public static void ApplyHooks()
        {
            On.Player.ctor += Player_ctor;
            On.AbstractPhysicalObject.Realize += AbstractPhysicalObject_Realize;
        }

        private static void AbstractPhysicalObject_Realize(On.AbstractPhysicalObject.orig_Realize orig, AbstractPhysicalObject self)
        {
            orig(self);
            if (self.type == UVsKeyDroneType)
            {
                self.realizedObject = new UVsKeyDrone_Item(self);
            }
        }

        private static void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
        {
            orig(self, abstractCreature, world);

            if (self.slugcatStats.name == tinker.Plugin.SlugName)
            {
                AbstractPhysicalObject drone = new AbstractPhysicalObject(
                    world,
                    UVsKeyDroneType,
                    null,
                    self.abstractCreature.pos,
                    world.game.GetNewID());

                self.objectInStomach = drone;
            }
        }
    }
}