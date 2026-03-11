using HUD;
using RWCustom;
using System.Reflection;
using tinker.Silk;
using UnityEngine;

namespace tinker.HUD_Hook
{
    public class SilkFuelMeter : HudPart
    {
        public Vector2 pos, lastPos;
        public FContainer hudGroup;
        public FSprite container;
        public FSprite liquid;
        public float fade, lastFade;
        private Player player;
        private SilkPhysics silk;

        private static FieldInfo _renderLayerField;
        private static FieldInfo _meshRendererField;
        private static MaterialPropertyBlock _propBlock;

        private float displayEnergy;
        private float lastDisplayEnergy;

        private int showTimer;

        private float lastRecordedEnergy = 0f;
        private int foodDisplayTimer = 0;
        private const int FOOD_DISPLAY_DURATION = 300;
        private bool isWeakened = false;
        private int weakenFlashTimer = 0;
        private const int WEAKEN_FLASH_DURATION = 60;

        public SilkFuelMeter(HUD.HUD hud, FContainer fContainer, Player player) : base(hud)
        {
            this.player = player;
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            if (_renderLayerField == null)
                _renderLayerField = typeof(FFacetNode).GetField("_renderLayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            try { this.silk = tinkerSilkData.Get(player); } catch { this.silk = null; }

            this.hudGroup = new FContainer();
            this.container = new FSprite("Silkhud", true);
            this.liquid = new FSprite("Silkhud_0", true);

            if (hud.rainWorld.Shaders.TryGetValue("SilkJarWave", out FShader silkShader))
            {
                this.liquid.shader = silkShader;
            }

            this.liquid.anchorY = 0f;
            this.liquid.y = -23f;

            this.hudGroup.AddChild(this.liquid);
            this.hudGroup.AddChild(this.container);

            this.hudGroup.scaleX = 2.8f;
            this.hudGroup.scaleY = 2.8f;

            fContainer.AddChild(this.hudGroup);
            this.pos = new Vector2(58f, 160f);
            this.lastPos = this.pos;

            this.displayEnergy = tinkerSilkData.GetEnergy(player);
            this.lastDisplayEnergy = this.displayEnergy;
            this.lastRecordedEnergy = this.displayEnergy;
        }

        public override void Update()
        {
            lastPos = pos;
            lastFade = fade;
            lastDisplayEnergy = displayEnergy;

            float targetEnergy = tinkerSilkData.GetEnergy(player);

            isWeakened = targetEnergy < 30f;

            if (silk != null && silk.mode == SilkMode.ShootingOut && isWeakened)
            {
                weakenFlashTimer = WEAKEN_FLASH_DURATION;
            }

            if (weakenFlashTimer > 0)
            {
                weakenFlashTimer--;
            }

            if (Mathf.Abs(displayEnergy - targetEnergy) > 0.01f)
                displayEnergy = Mathf.Lerp(displayEnergy, targetEnergy, 0.15f);
            else
                displayEnergy = targetEnergy;

            if (targetEnergy > lastRecordedEnergy + 0.1f)
            {
                foodDisplayTimer = FOOD_DISPLAY_DURATION;
            }

            lastRecordedEnergy = targetEnergy;

            if (foodDisplayTimer > 0)
            {
                foodDisplayTimer--;
            }

            if (silk != null && silk.mode != SilkMode.Retracted)
            {
                showTimer = 200;
            }
            else if (showTimer > 0)
            {
                showTimer--;
            }

            bool shouldShow = hud.showKarmaFoodRain || showTimer > 0 || foodDisplayTimer > 0;

            fade = Custom.LerpAndTick(fade, shouldShow ? 1f : 0f, 0.05f, 0.025f);

            Vector2 targetPos = new Vector2(58f, 160f);
            if (silk != null && silk.mode == SilkMode.ShootingOut) targetPos += Custom.RNV() * 1.5f;
            pos = Vector2.Lerp(pos, targetPos, 0.1f);
        }

        public override void Draw(float timeStacker)
        {
            Vector2 drawPos = Vector2.Lerp(lastPos, pos, timeStacker);
            float drawFade = Mathf.Lerp(lastFade, fade, timeStacker);

            this.hudGroup.SetPosition(drawPos);
            this.hudGroup.alpha = drawFade;

            this.liquid.scaleY = 1f;
            this.liquid.y = -23f;

            float smoothedEnergy = Mathf.Lerp(lastDisplayEnergy, displayEnergy, timeStacker);

            UpdateLiquidShader(smoothedEnergy / 100f);

            if (weakenFlashTimer > 0)
            {
                float flash = Mathf.Sin(Time.time * 20f) * 0.3f + 0.7f;
                this.container.color = new Color(1f, 0.7f, 0.7f) * flash;
                this.liquid.color = new Color(1f, 0.8f, 0.8f) * flash;
            }
            else
            {
                this.container.color = Color.white;
                this.liquid.color = Color.white;
            }
        }

        private void UpdateLiquidShader(float level)
        {
            try
            {
                object renderLayer = _renderLayerField?.GetValue(this.liquid);
                if (renderLayer != null)
                {
                    if (_meshRendererField == null)
                        _meshRendererField = renderLayer.GetType().GetField("_meshRenderer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    MeshRenderer renderer = _meshRendererField?.GetValue(renderLayer) as MeshRenderer;
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_propBlock);

                        Rect uv = this.liquid.element.uvRect;
                        _propBlock.SetVector("_SpriteRect", new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax));
                        _propBlock.SetFloat("_Level", level);
                        _propBlock.SetFloat("_CustomTime", Time.time);

                        renderer.SetPropertyBlock(_propBlock);
                    }
                }
            }
            catch { }
        }

        public override void ClearSprites() => this.hudGroup.RemoveFromContainer();
    }
}