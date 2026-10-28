using System;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace JakSurvivor
{
    // One body owns all weapon presentation. The only input is the replicated combat
    // snapshot; remote ropes acquire cosmetic targets without executing attacks.
    [DisallowMultipleComponent, DefaultExecutionOrder(10005)]
    public sealed class JakWeaponPresentation : MonoBehaviour
    {
        private const int WaveLimit = 32;
        private sealed class WaveVisual
        {
            internal uint Id;
            internal float Elapsed, Radius, Strength, StartedAt;
            internal JakEffectPlayer Burst, Ring;
            internal uint BurstLease, RingLease;
            internal JakAudio.Voice Shot;
            internal bool Seen;
            internal bool Grounded;
            internal Vector3 Ground, Normal;
        }
        private readonly WaveVisual[] waves = new WaveVisual[WaveLimit];
        private JakWeaponCombat combat;
        private CharacterBody body;
        private Transform model, muzzle;
        private uint actionId;
        private bool charging;
        private JakEffectPlayer action;
        private uint actionLease;
        private JakArcTracker arc;
        private JakAudio.Voice loop, whine;
        private readonly JakGunSpin gunSpin = new JakGunSpin();
        private JakGunBarrel barrel;
        internal float GunSpinSpeed => gunSpin.Speed;

        private void Awake()
        {
            combat = GetComponent<JakWeaponCombat>();
            body = GetComponent<CharacterBody>();
            for (int i = 0; i < waves.Length; i++) waves[i] = new WaveVisual();
        }
        private bool CanRender => !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && JakEffects.Assets && JakEffects.Data != null;
        // target-gun.gc L3338-3352, L3424-3436; gun-util.gc L313-322. Before LateUpdate so the barrel
        // twist and tracker sounds use this frame's spin.
        private void Update()
        {
            if (!CanRender || !body || !combat) return;
            bool blue = muzzle && muzzle.gameObject.activeInHierarchy && body.skillLocator && body.skillLocator.primary &&
                body.skillLocator.primary.skillDef == JakContent.ArcWielder;
            gunSpin.Step(blue && combat.ActionKind == JakWeaponCombat.Kind.Arc, Time.deltaTime);
            if (model && (!barrel || barrel.transform != model)) barrel = model.GetComponent<JakGunBarrel>() ?? model.gameObject.AddComponent<JakGunBarrel>();
            if (barrel) barrel.SetSpin(gunSpin.Spin);
            if (!blue) { JakAudio.Stop(whine); whine = null; return; }
            float v = gunSpin.Normalized;
            whine = JakAudio.Play("blue-gun-whine", muzzle.gameObject,
                volume1024: 1024f * Mathf.Min(1f, 2f * v), pitchMod: 1524f * Mathf.Lerp(-0.9f, 0.5f, v));
        }
        private void LateUpdate()
        {
            if (!CanRender) return;
            if (body && (!body.isActiveAndEnabled || !body.healthComponent || !body.healthComponent.alive)) { StopPresentation(); return; }
            if (combat)
            {
                Transform current = body && body.modelLocator ? body.modelLocator.modelTransform : null;
                if (model != current || !muzzle)
                {
                    if (model != current) ClearActionImmediate();
                    model = current;
                    var locator = model ? model.GetComponent<ChildLocator>() : null;
                    muzzle = locator ? locator.FindChild("MuzzleRight") : null;
                }
                if (!muzzle || !muzzle.gameObject.activeInHierarchy) ClearActionImmediate();
                switch (muzzle && muzzle.gameObject.activeInHierarchy ? combat.ActionKind : JakWeaponCombat.Kind.None)
                {
                    case JakWeaponCombat.Kind.Arc:
                        SetArc(combat.ActionId, muzzle);
                        break;
                    case JakWeaponCombat.Kind.Charge: SetCharge(combat.ActionId, muzzle, combat.WaveCharge); break;
                    default: ClearAction(); break;
                }
                foreach (WaveVisual visual in waves) visual.Seen = false;
                for (int i = 0; i < combat.WaveCount; i++)
                {
                    var snapshot = combat.GetWave(i);
                    SetWave(snapshot.Id, snapshot.Origin, snapshot.Strength, snapshot.Radius, snapshot.Elapsed);
                }
            }
            foreach (WaveVisual visual in waves)
            {
                if (visual.Id == 0) continue;
                if (!combat) visual.Seen = false;
                if (!visual.Seen)
                {
                    visual.Elapsed = Mathf.Max(visual.Elapsed, Time.time - visual.StartedAt);
                    // Combat can finish before the recovered visual tail. Keep the
                    // frozen release expanding cosmetically, without acquiring hits.
                    visual.Radius = JakWeaponRules.ExpandingRadius(visual.Strength, visual.Elapsed);
                }
                // A parent-hidden or pool-reclaimed effect ends its wave visual.
                if (visual.Elapsed >= JakEffectPlayer.WaveVisualDuration || !visual.Burst || !visual.Burst.IsLease(visual.BurstLease) ||
                    !visual.Ring || !visual.Ring.IsLease(visual.RingLease)) { Retire(visual); continue; }
                visual.Burst.SampleWave(visual.Radius, visual.Elapsed, visual.Grounded, visual.Ground, visual.Normal);
                visual.Ring.SampleWave(visual.Radius, visual.Elapsed, visual.Grounded, visual.Ground, visual.Normal);
            }
        }

        public void SetArc(uint id, Transform followMuzzle)
        {
            if (!CanRender || id == 0 || !followMuzzle) return;
            if (charging) ClearAction();
            if (!arc) arc = GetComponent<JakArcTracker>() ?? gameObject.AddComponent<JakArcTracker>();
            arc.Drive(id, followMuzzle);
            actionId = id;
        }
        public void SetCharge(uint id, Transform followMuzzle, float normalizedCharge)
        {
            if (!CanRender || id == 0 || !followMuzzle) return;
            if (actionId != id || !charging || !action || !action.IsLease(actionLease))
            {
                ClearAction();
                actionId = id; charging = true;
                action = Spawn("wave-charge", followMuzzle.position, followMuzzle);
                actionLease = action.LeaseId;
                action.Driven = true;
                // The authority auto-releases just before one pass ends; the loop covers a
                // release held back by wave capacity.
                loop = JakAudio.Play("wave-charge", followMuzzle.gameObject);
            }
            action.Charge = Mathf.Clamp01(normalizedCharge);
        }
        public void ClearAction()
        {
            JakAudio.Stop(loop); loop = null;
            if (action) action.Release(actionLease);
            if (arc) arc.Release();
            action = null; actionId = 0; charging = false;
        }
        private void ClearActionImmediate()
        {
            ClearAction();
            if (arc) arc.Kill();
        }

        // Attack origin is immutable and 3D; grounding is a cosmetic world-only probe.
        // The supplied radius/elapsed, not a local target/range query, controls the wave.
        public void SetWave(uint id, Vector3 origin, float normalizedStrength, float radius, float elapsed)
        {
            if (!CanRender || id == 0 || elapsed >= JakEffectPlayer.WaveVisualDuration) return;
            WaveVisual visual = null, spare = null;
            foreach (WaveVisual candidate in waves)
            {
                if (candidate.Id == id) { visual = candidate; break; }
                if (candidate.Id == 0) spare = candidate;
            }
            if (visual == null)
            {
                visual = spare;
                if (visual == null) return; // Cosmetic bound never evicts or changes combat.
                visual.Id = id;
                visual.Strength = Mathf.Clamp01(normalizedStrength);
                visual.StartedAt = Time.time - Mathf.Max(0f, elapsed);
                visual.Burst = Spawn("wave-burst", origin, null);
                visual.Ring = Spawn("wave-ring", origin, null);
                visual.BurstLease = visual.Burst.LeaseId; visual.RingLease = visual.Ring.LeaseId;
                visual.Burst.Driven = visual.Ring.Driven = true;
                visual.Grounded = Physics.Raycast(origin + Vector3.up * 0.1f, Vector3.down, out RaycastHit ground,
                    18.1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                visual.Ground = visual.Grounded ? ground.point : origin;
                visual.Normal = visual.Grounded ? ground.normal : Vector3.up;
                visual.Burst.Charge = visual.Ring.Charge = Mathf.Clamp01(normalizedStrength);
                // Joining a wave in progress samples its elapsed presentation; it does
                // not replay the release sound or a second visual commitment.
                if (elapsed <= 0.05f) visual.Shot = JakAudio.Play("wave-shot", gameObject);
            }
            visual.Seen = true;
            visual.Elapsed = Mathf.Max(visual.Elapsed, Mathf.Max(0f, elapsed));
            visual.Radius = Mathf.Clamp(radius, 0f, 18f);
        }
        private JakEffectPlayer Spawn(string name, Vector3 position, Transform parent)
        {
            var player = JakEffectPlayer.Spawn(name, position, Quaternion.identity, parent);
            var root = player.gameObject;
            // Body collision layers are excluded from the normal scene camera.
            root.layer = name.StartsWith("wave-", System.StringComparison.Ordinal) ? LayerIndex.defaultLayer.intVal : gameObject.layer;
            return player;
        }
        private static void Retire(WaveVisual visual)
        {
            // Source shot tails outlast the finite wave; the audio mixer owns their
            // natural completion on the body emitter, independently of VFX cleanup.
            if (visual.Burst) visual.Burst.Release(visual.BurstLease);
            if (visual.Ring) visual.Ring.Release(visual.RingLease);
            visual.Id = visual.BurstLease = visual.RingLease = 0; visual.Burst = visual.Ring = null; visual.Shot = null;
            visual.Elapsed = visual.Radius = visual.Strength = visual.StartedAt = 0f; visual.Seen = false;
        }
        public void StopPresentation()
        {
            JakAudio.Stop(whine); whine = null;
            ClearActionImmediate();
            foreach (WaveVisual visual in waves)
                if (visual != null) { JakAudio.Stop(visual.Shot); Retire(visual); }
        }
        private void OnDisable() => StopPresentation();
        private void OnDestroy() => StopPresentation();
    }
}
