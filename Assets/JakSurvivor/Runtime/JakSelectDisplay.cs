using System.Collections;
using RoR2;
using RoR2.SurvivorMannequins;
using UnityEngine;

namespace JakSurvivor
{
    internal sealed class JakSelectDisplay : MonoBehaviour
    {
        // Starting values for the lobby's lighting; tune after seeing them in game.
        private const float DarkLightIntensity = 2.5f, DarkLightRange = 5f;
        private JakVisuals visuals;
        private bool preview;
        private System.Action<string, float> previewSound;
        private JakAudio.Voice voice;
        private Transform chest;
        private Light darkLight;
        private bool darkForm;
        private float level;

        private void OnEnable() => StartCoroutine(PlayWhenReady());

        private IEnumerator PlayWhenReady()
        {
            // Native mannequin ownership and skin application follow Instantiate.
            yield return null;
            var slot = GetComponentInParent<SurvivorMannequinSlotController>();
            visuals = GetComponentInChildren<JakVisuals>();
            // Catalog prefabs, portrait renders and other previews stay in stance.
            if (!slot || !visuals) yield break;
            var locator = visuals.GetComponent<ChildLocator>();
            chest = locator ? locator.FindChild("Chest") : null;
            BeginEntrance();
        }

        public void EndEntrance()
        {
            darkForm = false;
            preview = false;
            previewSound = null;
            JakAudio.Stop(voice);
            voice = null;
        }

        public void BeginEntrance(System.Action<string, float> sound = null)
        {
            EndEntrance();
            visuals = GetComponentInChildren<JakVisuals>();
            preview = sound != null;
            previewSound = sound;
            visuals.Animator.runtimeAnimatorController = JakAssets.Get<RuntimeAnimatorController>(JakAnimator.DisplayController);
            visuals.Animator.speed = 1f;
            visuals.Animator.Play("Select0", 0, 0f);
        }

        public void EnterStep(string sound, float gain, bool gun, float darkFrom, float darkUntil)
        {
            if (!visuals) visuals = GetComponentInChildren<JakVisuals>();
            if (preview) previewSound?.Invoke(sound, gain);
            else PlaySound(sound, gain);
            UpdateStep(0f, gun, darkFrom, darkUntil);
        }

        public void UpdateStep(float elapsed, bool gun, float darkFrom, float darkUntil)
        {
            bool dark = elapsed >= darkFrom && elapsed < darkUntil;
            if (visuals) visuals.SetSelectionAppearance(dark, gun);
            if (darkForm != dark) OnFormChanged(dark);
        }

        private void PlaySound(string key, float gain)
        {
            // Silent animation phases let the preceding effect finish its tail.
            if (string.IsNullOrEmpty(key)) return;
            JakAudio.Stop(voice);
            voice = null;
            // Lobby slot swaps can reparent a display while its entrance plays.
            var slot = GetComponentInParent<SurvivorMannequinSlotController>();
            if (slot && slot.networkUser && slot.networkUser.isLocalPlayer)
                voice = JakAudio.PlayMenu(key, gameObject, gain);
        }

        // The light is local and unnetworked, so every Jak mannequin in the
        // lobby shows it; only the local player's display plays sound. Jak 3
        // spawns no particle burst for this transformation or its reversion.
        private void OnFormChanged(bool dark)
        {
            darkForm = dark;
            if (darkLight) return;
            darkLight = new GameObject("JakDarkLight").AddComponent<Light>();
            darkLight.transform.SetParent(transform, false);
            darkLight.type = LightType.Point;
            darkLight.color = JakContent.DarkBuff ? JakContent.DarkBuff.buffColor : new Color(0.64f, 0.24f, 0.94f);
            darkLight.range = DarkLightRange;
            darkLight.shadows = LightShadows.None;
            darkLight.renderMode = LightRenderMode.ForcePixel;
            darkLight.intensity = 0f;
        }

        private Vector3 Center() => chest ? chest.position : transform.position + Vector3.up * 1.2f;

        private void LateUpdate()
        {
            if (!darkLight) return;
            float delta = Time.unscaledDeltaTime;
            // Rise with the transformation and ease out with the reversion.
            level = Mathf.MoveTowards(level, darkForm ? 1f : 0f, delta / (darkForm ? 0.25f : 0.5f));
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f);
            darkLight.intensity = level * DarkLightIntensity * pulse;
            darkLight.enabled = darkLight.intensity > 0.01f;
            // Keep the light in front of the chest; from inside the body it
            // would only reach back faces.
            darkLight.transform.position = Center() + transform.forward * 0.7f;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            JakAudio.Stop(voice);
            voice = null;
            if (visuals) visuals.SetSelectionAppearance(false, true);
            if (darkLight) Destroy(darkLight.gameObject);
            darkLight = null;
            darkForm = false;
            level = 0f;
        }
    }
}
