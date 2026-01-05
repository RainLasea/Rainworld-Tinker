using tinker;
using UnityEngine;

namespace Tinker
{
    public static class TinkerLanguageHint
    {
        private static readonly Menu.MenuScene.SceneID TinkerSceneID =
            new Menu.MenuScene.SceneID("slugcat_tinker");

        public static void Init()
        {
            On.Menu.MenuScene.ctor += MenuScene_ctor;
        }

        private static void MenuScene_ctor(
            On.Menu.MenuScene.orig_ctor orig,
            Menu.MenuScene self,
            Menu.Menu menu,
            Menu.MenuObject owner,
            Menu.MenuScene.SceneID sceneID)
        {
            orig(self, menu, owner, sceneID);

            if (sceneID != TinkerSceneID || !Options_Hook.LanguageHintEnabled)
                return;

            var rw = menu.manager.rainWorld;

            string imgName =
                rw.options.language == InGameTranslator.LanguageID.Chinese
                    ? "hint_zh"
                    : "hint_en";

            self.AddIllustration(
                new Menu.MenuDepthIllustration(
                    menu,
                    self,
                    self.sceneFolder,
                    imgName,
                    new Vector2(385f, 600f),
                    2.79f,
                    Menu.MenuDepthIllustration.MenuShader.Normal
                )
            );
        }
    }
}