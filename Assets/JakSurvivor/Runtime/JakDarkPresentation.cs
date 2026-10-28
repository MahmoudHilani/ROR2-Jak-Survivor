using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace JakSurvivor
{
    [DisallowMultipleComponent, DefaultExecutionOrder(201)]
    public sealed class JakDarkPresentation : MonoBehaviour
    {
        // Serialized by the importer in native node-list order, including the root at zero.
        public Transform[] Nodes;
        // target-part.gc:3139 uses merc-ctrl.length (62 on jakb-lod0), not the 65-node cspace table.
        internal const int Drawable2JointCount = 62;
        private sealed class Victim
        {
            internal NetworkInstanceId Id;
            internal float StartedAt;
            internal CharacterBody Body;
            internal CharacterModel Model;
            internal Transform[] Bones;
            internal GameObject Owner;
            internal JakColorEffect Tint;
            internal JakDarkVictimEffect State;
            internal bool Seen;
        }
        private readonly List<Victim> victims = new List<Victim>();
        private readonly System.Random random = new System.Random();
        private readonly List<Transform> boneScratch = new List<Transform>();
        private JakDarkAura aura;
        private JakFace face;
        private CharacterModel model;
        private JakDarkCombat combat;
        private JakEffectPlayer particles;
        private JakColorEffect tint;
        private JakShockSpec transformSpec, attackSpec;
        private Action<JakDarkEvent> auraEvent;
        private Action<long> frame;
        private Action<JakDarkEvent> victimEvent;
        private Victim currentVictim;
        internal long Sparkles { get; private set; }
        internal long VictimSparkles { get; private set; }
        internal long VictimProbeRequests { get; private set; }
        internal long BetweenRequests { get; private set; }
        internal int ActiveVictimCount => victims.Count;
        internal bool AuraDrawn { get; private set; }

        private void Awake()
        {
            face = GetComponent<JakFace>();
            model = GetComponent<CharacterModel>();
            auraEvent = AuraEvent;
            victimEvent = VictimEvent;
            frame = Frame;
        }
        private void OnEnable() => aura = new JakDarkAura(random);
        private bool CanRender => !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
            JakEffects.Assets && JakEffects.Data != null;

        private void LateUpdate()
        {
            if (!CanRender || !face || !model) return;
            CharacterBody body = model.body;
            if (body && (!body.healthComponent || !body.healthComponent.alive)) { Stop(); return; }
            if (!tint) tint = JakColorEffect.For(model);
            if (!particles) particles = JakEffectPlayer.CreateLauncher(gameObject.layer);
            if (transformSpec == null) transformSpec = JakEffects.ShockSpec("lightning-darkjak-transform");
            if (attackSpec == null) attackSpec = JakEffects.ShockSpec("lightning-darkjak-attack");
            combat = body ? body.GetComponent<JakDarkCombat>() : null;
            Reconcile();
            AuraDrawn = model.visibility == VisibilityLevel.Visible && model.invisibilityCount == 0;
            aura.Advance(Time.deltaTime, face.DarkInterp, AuraDrawn, Nodes.Length, auraEvent, frame);
        }

        private void Reconcile()
        {
            foreach (Victim victim in victims) victim.Seen = false;
            if (combat)
            {
                for (int entry = 0; entry < combat.Victims.Count; entry++)
                {
                    JakDarkCombat.VictimSnapshot snapshot = combat.Victims[entry];
                    Victim victim = null;
                    for (int index = 0; index < victims.Count; index++)
                        if (victims[index].Id == snapshot.NetId && victims[index].StartedAt == snapshot.StartedAt)
                        { victim = victims[index]; break; }
                    if (victim != null && (!victim.Body || !victim.Model)) continue;
                    if (victim == null)
                    {
                        CharacterBody body = snapshot.Body;
                        Transform root = body && body.modelLocator ? body.modelLocator.modelTransform : null;
                        CharacterModel characterModel = root ? root.GetComponent<CharacterModel>() : null;
                        // A snapshot can precede the victim's spawn/model on a joining client.
                        if (!characterModel) continue;
                        boneScratch.Clear();
                        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            foreach (Transform bone in renderer.bones)
                                if (bone && !boneScratch.Contains(bone)) boneScratch.Add(bone);
                        if (boneScratch.Count == 0) boneScratch.Add(root);
                        var owner = new GameObject("JakDarkVictimLightning");
                        owner.transform.SetParent(root, false);
                        victim = new Victim { Id = snapshot.NetId, StartedAt = snapshot.StartedAt, Body = body,
                            Model = characterModel, Bones = boneScratch.ToArray(), Owner = owner,
                            Tint = JakColorEffect.For(characterModel),
                            State = new JakDarkVictimEffect(random, Mathf.Max(0f, snapshot.Age - Time.deltaTime), aura.AgeTicks) };
                        victims.Add(victim);
                    }
                    victim.Seen = true;
                }
            }
            for (int i = victims.Count - 1; i >= 0; i--)
                if (!victims[i].Seen) { Retire(victims[i], false); victims.RemoveAt(i); }
        }

        private void Frame(long tick)
        {
            foreach (Victim victim in victims)
            {
                if (!victim.Model || !victim.Owner) continue;
                currentVictim = victim;
                // The replicated presence owns validity. RoR2 skeletons use random bones instead of native node indices.
                victim.State.AdvanceFrame(aura, true, true, Drawable2JointCount, 0, victimEvent);
            }
            currentVictim = null;
        }
        private Transform Joint(int node) => Nodes[node];
        private Transform VictimJoint(Victim victim) => victim.Bones[random.Next(victim.Bones.Length)];

        private void AuraEvent(JakDarkEvent effect)
        {
            switch (effect.Kind)
            {
                case JakDarkEventKind.Sparkle:
                    particles.Launch(173, Joint(effect.SourceJoint).position, Quaternion.identity); Sparkles++;
                    break;
                case JakDarkEventKind.Probe:
                    JakShock.Probe(gameObject, transformSpec, 187, Joint(effect.SourceJoint), model.body ? model.body.gameObject : gameObject, gameObject.layer);
                    break;
            }
        }
        private void VictimEvent(JakDarkEvent effect)
        {
            Victim victim = currentVictim;
            bool visible = victim.Model.visibility != VisibilityLevel.Invisible && victim.Model.invisibilityCount == 0;
            if (effect.Kind == JakDarkEventKind.End) { if (victim.Tint) victim.Tint.Clear(); return; }
            if (!visible) return;
            switch (effect.Kind)
            {
                case JakDarkEventKind.Sparkle:
                    particles.Launch(173, VictimJoint(victim).position, Quaternion.identity); Sparkles++; VictimSparkles++;
                    break;
                case JakDarkEventKind.DarkTint: victim.Tint.Dark(); break;
                case JakDarkEventKind.ShockTint: tint.Shock(); break;
                case JakDarkEventKind.Between:
                    JakShock.Between(victim.Owner, attackSpec, 187, Joint(effect.SourceJoint), VictimJoint(victim), victim.Model.gameObject.layer);
                    BetweenRequests++;
                    break;
                case JakDarkEventKind.Probe:
                    JakShock.Probe(victim.Owner, attackSpec, 187, VictimJoint(victim), victim.Body.gameObject, victim.Model.gameObject.layer);
                    VictimProbeRequests++;
                    break;
            }
        }
        private static void Retire(Victim victim, bool immediate)
        {
            if (victim.Tint) victim.Tint.Clear();
            if (immediate) { if (victim.Owner) Destroy(victim.Owner); }
            else JakShock.Retire(victim.Owner);
        }
        private void Stop()
        {
            AuraDrawn = false;
            foreach (Victim victim in victims) Retire(victim, true);
            victims.Clear();
            if (tint) tint.Clear();
            JakShock.Clear(gameObject);
            if (particles) particles.Release();
            particles = null;
        }
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
