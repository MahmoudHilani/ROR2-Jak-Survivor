using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    [DisallowMultipleComponent]
    public sealed class JakColorEffect : MonoBehaviour
    {
        private enum Effect { None, Dark, Shock }
        private sealed class SavedRenderer
        {
            public Renderer Renderer;
            public readonly MaterialPropertyBlock Original = new MaterialPropertyBlock();
        }
        private sealed class RenderPass
        {
            public Camera Camera;
            public bool Prepared, Applied;
            public int Count;
            public readonly List<SavedRenderer> Renderers = new List<SavedRenderer>();
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int EmTexId = Shader.PropertyToID("_EmTex");
        private static readonly int EmColorId = Shader.PropertyToID("_EmColor");
        private static readonly int EmPowerId = Shader.PropertyToID("_EmPower");
        private MaterialPropertyBlock working;
        private readonly List<RenderPass> passes = new List<RenderPass>();
        private readonly System.Random random = new System.Random();
        private CharacterModel model;
        private JakVisuals visuals;
        private Effect effect;
        private Color multiplier = Color.white, emissive = Color.clear;
        private float expires, nextShockFrame;
        private int depth;

        public static JakColorEffect For(CharacterModel value)
        {
            if (!value) return null;
            JakColorEffect result = value.GetComponent<JakColorEffect>();
            return result ? result : value.gameObject.AddComponent<JakColorEffect>();
        }

        public void Dark(float duration = 0.2f)
        {
            // enemy.gc:2028-2046 samples once per event, not per rendering camera.
            float t = 0.2f + (float)random.NextDouble() * 0.8f;
            effect = Effect.Dark;
            multiplier = new Color(1f, 1f - t, 1f, 1f);
            emissive = new Color(0.3f * t, 0f, 0.3f * t, 0f);
            expires = Time.time + duration;
        }

        public void Shock(float duration = 0.2f)
        {
            if (effect != Effect.Shock)
            {
                SampleShock();
                nextShockFrame = Time.time + 1f / 60f;
            }
            effect = Effect.Shock;
            expires = Time.time + duration;
        }

        public void Clear()
        {
            effect = Effect.None;
            for (int i = depth - 1; i >= 0; i--) Restore(passes[i]);
        }

        private void Awake()
        {
            working = new MaterialPropertyBlock();
            model = GetComponent<CharacterModel>();
            visuals = GetComponent<JakVisuals>();
        }

        private void OnEnable()
        {
            // CharacterModel must subscribe first: it owns hit/heal flash and camera fades.
            RuntimeHelpers.RunClassConstructor(typeof(CharacterModel).TypeHandle);
            Camera.onPreCull += BeginCamera;
            SceneCamera.onSceneCameraPreRender += PrepareCamera;
            Camera.onPostRender += EndCamera;
        }

        private void Update()
        {
            // A render interrupted before post-render must not leave overrides in the scene.
            if (depth != 0)
            {
                for (int i = depth - 1; i >= 0; i--) Restore(passes[i]);
                depth = 0;
            }
            if (effect == Effect.None) return;
            if (Time.time >= expires) { Clear(); return; }
            if (effect == Effect.Shock)
            {
                int frames = 0;
                while (Time.time >= nextShockFrame && frames++ < 12)
                {
                    SampleShock();
                    nextShockFrame += 1f / 60f;
                }
                // Missed historical colors are never drawn; bound catch-up after a stalled frame.
                if (Time.time >= nextShockFrame) nextShockFrame = Time.time + 1f / 60f;
            }
        }

        private void SampleShock()
        {
            // powerups.gc:687-697 updates once per native 60 Hz frame.
            float f = 0.5f + (float)random.NextDouble() * 1.5f;
            multiplier = new Color(f, f, f + 0.5f, 1f);
            emissive = Color.clear;
        }

        private void BeginCamera(Camera camera)
        {
            if (depth == 0 && (effect == Effect.None || !model)) return;
            if (depth > 0) Restore(passes[depth - 1]);
            if (depth == passes.Count) passes.Add(new RenderPass());
            RenderPass pass = passes[depth++];
            pass.Camera = camera;
            pass.Prepared = pass.Applied = false;
            pass.Count = 0;
        }

        private void PrepareCamera(SceneCamera camera)
        {
            if (depth == 0 || passes[depth - 1].Camera != camera.camera) return;
            RenderPass pass = passes[depth - 1];
            pass.Prepared = true;
            Apply(pass);
        }

        private void Apply(RenderPass pass)
        {
            if (!model || effect == Effect.None || Time.time >= expires) return;
            CharacterModel.RendererInfo[] infos = model.baseRendererInfos;
            pass.Applied = true;
            for (int i = 0; i < infos.Length; i++)
            {
                Renderer renderer = infos[i].renderer;
                Material material = infos[i].defaultMaterial;
                if (!renderer || !material || !material.HasProperty(ColorId)) continue;
                if (pass.Count == pass.Renderers.Count) pass.Renderers.Add(new SavedRenderer());
                SavedRenderer saved = pass.Renderers[pass.Count++];
                saved.Renderer = renderer;
                renderer.GetPropertyBlock(saved.Original);
                renderer.GetPropertyBlock(working);
                Color baseColor = working.HasProperty(ColorId) ? working.GetColor(ColorId) : material.GetColor(ColorId);
                Color flash = working.HasProperty(FlashId) ? working.GetColor(FlashId)
                    : material.HasProperty(FlashId) ? material.GetColor(FlashId) : Color.clear;
                bool ownEmission = effect == Effect.Shock && OwnMaterial(material)
                    && material.HasProperty(MainTexId) && material.HasProperty(EmTexId)
                    && material.HasProperty(EmColorId) && material.HasProperty(EmPowerId);
                Color albedoMultiplier = multiplier;
                if (ownEmission)
                {
                    // Deferred albedo clamps above one; Jak's unlit emission carries the excess.
                    Color excess = new Color(Mathf.Max(0f, multiplier.r - 1f), Mathf.Max(0f, multiplier.g - 1f), Mathf.Max(0f, multiplier.b - 1f), 0f);
                    albedoMultiplier = new Color(Mathf.Min(1f, multiplier.r), Mathf.Min(1f, multiplier.g), Mathf.Min(1f, multiplier.b), 1f);
                    Texture texture = working.HasProperty(MainTexId) ? working.GetTexture(MainTexId) : material.GetTexture(MainTexId);
                    working.SetTexture(EmTexId, texture);
                    working.SetColor(EmColorId, baseColor * excess);
                    working.SetFloat(EmPowerId, 1f);
                }
                working.SetColor(ColorId, baseColor * albedoMultiplier);
                working.SetColor(FlashId, flash + emissive);
                renderer.SetPropertyBlock(working);
            }
        }

        private bool OwnMaterial(Material material)
        {
            if (!visuals) return false;
            return Contains(visuals.armedMaterials, material) || Contains(visuals.darkMaterials, material);
        }

        private static bool Contains(Material[] materials, Material material)
        {
            if (materials != null)
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] == material) return true;
            return false;
        }

        private static void Restore(RenderPass pass)
        {
            if (!pass.Applied) return;
            // MPBs cannot remove one property. Restore the exact pre-render block, including absence.
            for (int i = pass.Count - 1; i >= 0; i--)
            {
                SavedRenderer saved = pass.Renderers[i];
                if (saved.Renderer) saved.Renderer.SetPropertyBlock(saved.Original.isEmpty ? null : saved.Original);
            }
            pass.Count = 0;
            pass.Applied = false;
        }

        private void EndCamera(Camera camera)
        {
            if (depth == 0 || passes[depth - 1].Camera != camera) return;
            Restore(passes[--depth]);
            if (depth > 0 && passes[depth - 1].Prepared) Apply(passes[depth - 1]);
        }

        private void OnDisable()
        {
            Camera.onPreCull -= BeginCamera;
            SceneCamera.onSceneCameraPreRender -= PrepareCamera;
            Camera.onPostRender -= EndCamera;
            Clear();
            depth = 0;
        }
    }
}
