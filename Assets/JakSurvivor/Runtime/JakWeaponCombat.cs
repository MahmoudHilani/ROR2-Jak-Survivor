using System;
using System.Collections.Generic;
using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor
{
    // Input/stock remain native skill/state work. This body component is the sole damage
    // executor and supplies action and wave snapshots, including to late joiners.
    // Manual serialization intentionally needs no generated SyncVar or RPC methods.
    [DisallowMultipleComponent]
    public sealed class JakWeaponCombat : NetworkBehaviour
    {
        public enum Kind : byte { None, Arc, Charge }
        public const int MaxWaves = 32;
        public readonly struct WaveSnapshot
        {
            public readonly uint Id;
            public readonly Vector3 Origin;
            public readonly float Strength, Elapsed;
            public readonly bool Crit;
            public WaveSnapshot(uint id, Vector3 origin, float strength, float elapsed, bool crit)
            { Id = id; Origin = origin; Strength = strength; Elapsed = elapsed; Crit = crit; }
            public float Radius => JakWeaponRules.ExpandingRadius(Strength, Elapsed);
        }
        private sealed class Wave
        {
            internal uint Id;
            internal Vector3 Origin;
            internal float Strength, Elapsed, Damage;
            internal bool Crit;
            private readonly JakWaveVictims victims = new JakWaveVictims();
            internal bool AddVictim(int value) => victims.Add(value);
            internal bool HasVictim(int value) => victims.Contains(value);
            internal bool VictimsFull => victims.Full;
            internal void Clear()
            {
                Id = 0; Origin = default; Strength = Elapsed = Damage = 0f; Crit = false;
                victims.Clear();
            }
            internal WaveSnapshot Snapshot => new WaveSnapshot(Id, Origin, Strength, Elapsed, Crit);
        }
        private HurtBox arcFocus;
        private CharacterBody body;
        private EntityStateMachine bodyMachine;
        private SkillLocator skills;
        private ChildLocator children;
        private readonly JakWeaponTargets targets = new JakWeaponTargets();
        private JakArcRope serverRope;
        private JakArcWorld arcWorld;
        private readonly HashSet<int> arcVictims = new HashSet<int>();
        private bool arcCrit, arcWindow;
        internal JakArcRope ServerRope => serverRope;
        // Server-selected first target, replicated to every peer; the rope root aims at it.
        internal HurtBox ArcFocus => arcFocus;
        internal float ActionAge => actionAge;
        private readonly Wave[] waves = new Wave[MaxWaves];
        private JakWeaponRules.PulseGate pulseGate;
        private JakWeaponRules.ActionSequence sequence;
        private uint nextId, revision, receivedRevision, committedId;
        private double clock;
        private float snapshotClock;
        private int waveCount;
        private float rate = 1f, actionAge;
        private Kind kind;

        public Kind ActionKind => kind;
        public uint ActionId => sequence.Active;
        public float SampledAttackSpeed => rate;
        public bool IsCharging => kind == Kind.Charge;
        public float WaveCharge => IsCharging ? JakWeaponRules.Charge(actionAge, rate) : 0f;
        public int WaveCount => waveCount;
        public WaveSnapshot GetWave(int index) => waves[index].Snapshot;
        public int ArcPulseCount { get; private set; }
        public int ArcHitCount { get; private set; }
        public int WaveCommitCount { get; private set; }
        public int WaveHitCount { get; private set; }
        public bool HasWaveCapacity => waveCount < MaxWaves;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            skills = GetComponent<SkillLocator>();
            bodyMachine = EntityStateMachine.FindByCustomName(gameObject, "Body");
            arcWorld = new JakArcWorld(body);
            for (int i = 0; i < waves.Length; i++) waves[i] = new Wave();
        }
        private void Start()
        {
            Transform model = body.modelLocator ? body.modelLocator.modelTransform : null;
            if (model) children = model.GetComponent<ChildLocator>();
        }
        public static bool CanUseWeapons(CharacterBody owner)
        {
            if (!owner || !owner.isActiveAndEnabled || !owner.healthComponent || !owner.healthComponent.alive ||
                JakContent.DarkBuff && owner.HasBuff(JakContent.DarkBuff)) return false;
            Transform model = owner.modelLocator ? owner.modelLocator.modelTransform : null;
            JakBoardRig rig = model ? model.GetComponent<JakBoardRig>() : null;
            return !rig || rig.Phase != JakBoardPhase.Riding;
        }
        public uint AllocateActionId()
        {
            if (JakWeaponRules.Newer(sequence.Latest, nextId)) nextId = sequence.Latest;
            do { nextId++; } while (nextId == 0);
            return nextId;
        }
        internal bool Begin(Kind value, uint id, float sampledRate, float age)
        {
            if (!NetworkServer.active && !Util.HasEffectiveAuthority(gameObject)) return true;
            if (!sequence.Begin(id)) return false;
            kind = value;
            rate = JakWeaponRules.SampleRate(sampledRate);
            actionAge = Mathf.Max(0f, age);
            serverRope = null;
            arcFocus = null;
            Dirty();
            return true;
        }
        internal void End(uint id)
        {
            if (!sequence.End(id)) return;
            kind = Kind.None;
            serverRope = null;
            arcFocus = null;
            Dirty();
        }
        // Controller calls this before board/form overrides. Already committed waves are not canceled.
        // Ending the action stops server damage at once. Only the owner transitions its
        // client-authoritative Weapon machine; a server-forced end reaches it by snapshot.
        public void CancelUncommitted()
        {
            if (sequence.Active != 0) End(sequence.Active);
            EntityStateMachine weapon = skills && skills.primary ? skills.primary.stateMachine : null;
            if (weapon && !weapon.destroying && (weapon.state is States.ArcWielder || weapon.state is States.WaveConcussor) &&
                Util.HasEffectiveAuthority(gameObject)) weapon.SetState(new Idle());
        }
        internal void SetChargeAge(uint id, float age)
        {
            if (sequence.Active == id && kind == Kind.Charge) actionAge = Mathf.Max(0f, age);
        }
        public Vector3 MuzzleOrigin()
        {
            Vector3 safe = body.corePosition;
            Transform muzzle = children ? children.FindChild("MuzzleRight") : null;
            Vector3 desired = muzzle ? muzzle.position : body.aimOrigin;
            // Native model attachments can penetrate a wall. Always probe from the body,
            // never the camera, and stop short of the obstruction.
            Vector3 offset = desired - safe;
            float length = offset.magnitude;
            if (length > 0f && Physics.Raycast(safe, offset / length, out RaycastHit hit, length,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) return hit.point - offset / length * 0.05f;
            return desired;
        }
        internal JakShot ArcShot() => JakAim.Shot(bodyMachine, body.inputBank.GetAimRay());
        // Server-only first-target acquisition. Peers aim at the replicated result and never select.
        internal void SelectArcFocus(Vector3 muzzle, in JakShot shot)
        {
            HurtBox focus = null;
            Vector3 direction = shot.Ray.direction.normalized;
            if (!shot.Spinning && direction.sqrMagnitude > 0f)
            {
                // Native scoring uses Jak's control position, independently of muzzle/LOS origin.
                Vector3 origin = body.transform.position;
                bool airborne = body.characterMotor && !body.characterMotor.isGrounded;
                ArcCandidate[] candidates = targets.CollectArc(origin, body.teamComponent.teamIndex, arcFocus, out int count);
                try
                {
                    ArcFocus incumbent = arcFocus ? new ArcFocus { Health = arcFocus.healthComponent.GetInstanceID(), Box = arcFocus.GetInstanceID() } : default;
                    targets.ArcVisibilityOrigin = muzzle;
                    int first = JakArcTargeting.Select(new System.Numerics.Vector3(origin.x, origin.y, origin.z),
                        new System.Numerics.Vector3(direction.x, direction.y, direction.z), candidates, count, targets, incumbent, airborne);
                    if (first >= 0) focus = targets.ArcBox(first);
                }
                finally { targets.ReleaseArc(); }
            }
            if (focus == arcFocus) return;
            arcFocus = focus;
            Dirty();
        }

        private void UpdateArc()
        {
            if (!NetworkServer.active || kind != Kind.Arc || !CanUseWeapons(body) ||
                !skills.primary || skills.primary.skillDef != JakContent.ArcWielder) return;
            Vector3 fire = MuzzleOrigin();
            JakShot shot = ArcShot();
            SelectArcFocus(fire, shot);
            arcWorld.Focus = arcFocus;
            arcWorld.BeginUpdate();
            Vector3 direction = arcWorld.Direction(fire, shot);
            if (serverRope == null)
            {
                serverRope = new JakArcRope();
                serverRope.Enter(JakArcWorld.ToV(fire), JakArcWorld.ToV(direction), JakArcWorld.ToV(body.transform.position));
            }
            serverRope.Update(JakArcWorld.ToV(fire), JakArcWorld.ToV(direction), JakArcWorld.ToV(body.transform.position), Time.fixedDeltaTime, true, arcWorld, actionAge);
            if (pulseGate.TryPulse(clock, rate))
            {
                ArcPulseCount++; arcWindow = true; arcVictims.Clear();
                arcCrit = Util.CheckRoll(body.crit, body.master);
            }
            if (!arcWindow) return;
            for (int i = 0; i < serverRope.Path.SpanCount; i++)
            {
                HurtBox target = arcWorld.Sweep(serverRope.Path.Spans[i], arcVictims, out Vector3 point);
                if (!target) continue;
                int victimIndex = arcVictims.Count;
                arcVictims.Add(target.healthComponent.GetInstanceID()); ArcHitCount++;
                Hit(target, point, JakWeaponRules.ArcDamage(victimIndex) * body.damage, arcCrit, Balance.ArcProc, DamageSource.Primary, Vector3.zero);
            }
        }
        // Invoked by the frozen native recovery state, never by remote presentation.
        internal bool CommitWave(uint id, float strength, Vector3 origin, bool crit)
        {
            if (!NetworkServer.active && !Util.HasEffectiveAuthority(gameObject)) return false;
            if (kind != Kind.Charge || sequence.Active != id || committedId == id || !HasWaveCapacity || !CanUseWeapons(body) ||
                !skills.secondary || skills.secondary.skillDef != JakContent.WaveConcussor) return false;
            committedId = id;
            Wave wave = waves[waveCount++];
            wave.Clear();
            wave.Id = id;
            wave.Strength = Mathf.Clamp(strength, Balance.WaveMinimumCharge / Balance.WaveFullCharge, 1f);
            Vector3 offset = origin - body.corePosition;
            // The owner freezes the muzzle, while the server also enforces body-local range/obstruction.
            origin = body.corePosition + Vector3.ClampMagnitude(offset, 3f);
            offset = origin - body.corePosition;
            if (offset.sqrMagnitude > 0f && Physics.Raycast(body.corePosition, offset.normalized, out RaycastHit hit,
                offset.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) origin = hit.point - offset.normalized * 0.05f;
            wave.Origin = origin;
            wave.Damage = body.damage;
            wave.Crit = crit;
            WaveCommitCount++;
            End(id);
            if (NetworkServer.active) AcquireWaveTargets(wave); // Initial overlap is part of the attack.
            Dirty();
            return true;
        }
        private void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            if (!body || !body.healthComponent || !body.healthComponent.alive)
            {
                CancelUncommitted();
                ClearWaves();
                return;
            }
            if (kind != Kind.None) actionAge += Time.fixedDeltaTime;
            UpdateArc();
            for (int i = waveCount - 1; i >= 0; i--)
            {
                Wave wave = waves[i];
                float lifetime = JakWeaponRules.WaveLifetime(wave.Strength);
                if (wave.Elapsed >= lifetime) { RemoveWave(i); continue; }
                wave.Elapsed = Mathf.Min(lifetime, wave.Elapsed + Time.fixedDeltaTime);
                if (NetworkServer.active) AcquireWaveTargets(wave);
                // Retain the terminal snapshot for one step so presentation can reach its outer radius.
            }
            if (NetworkServer.active && (kind != Kind.None || waveCount > 0))
            {
                snapshotClock += Time.fixedDeltaTime;
                if (snapshotClock >= 0.05f) { snapshotClock = 0f; Dirty(); }
            }
        }
        private void AcquireWaveTargets(Wave wave)
        {
            if (wave.VictimsFull) return;
            float radius = JakWeaponRules.ExpandingRadius(wave.Strength, wave.Elapsed);
            int count = targets.QueryVisible(wave.Origin, radius, body.teamComponent.teamIndex);
            for (int i = 0; i < count; i++)
            {
                HurtBox target = targets.Box(i);
                Vector3 point = targets.Point(i);
                float distance = Vector3.Distance(wave.Origin, point);
                if (!JakWeaponTargets.Eligible(target, TeamMask.GetEnemyTeams(body.teamComponent.teamIndex)) || distance > radius ||
                    wave.HasVictim(target.healthComponent.GetInstanceID()) || !wave.AddVictim(target.healthComponent.GetInstanceID())) continue;
                WaveHitCount++;
                Vector3 force = (point - wave.Origin).normalized * Balance.WaveForce;
                Hit(target, point, JakWeaponRules.WaveDamage(wave.Strength, distance) * wave.Damage, wave.Crit, Balance.WaveProc, DamageSource.Secondary, force);
            }
            targets.Clear();
        }
        private void Hit(HurtBox target, Vector3 point, float damage, bool crit, float proc, DamageSource source, Vector3 force)
        {
            if (!NetworkServer.active) return;
            // Same native server hit path as LightningOrb and BlastAttack, including rejected hits,
            // hurtbox modifiers, on-hit items and force protections. No manual item-proc calls.
            var info = new DamageInfo {
                attacker = gameObject, inflictor = gameObject, damage = damage, crit = crit,
                position = point, force = force, canRejectForce = true, procCoefficient = proc,
                damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, source), inflictedHurtbox = target
            };
            info.ModifyDamageInfo(target.damageModifier);
            HealthComponent health = target.healthComponent;
            health.TakeDamage(info);
            GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
            GlobalEventManager.instance.OnHitAll(info, health.gameObject);
        }
        private void RemoveWave(int index)
        {
            Wave spare = waves[index];
            for (int i = index; i + 1 < waveCount; i++) waves[i] = waves[i + 1];
            waves[--waveCount] = spare;
            spare.Clear();
            Dirty();
        }
        private void ClearWaves() { while (waveCount > 0) RemoveWave(waveCount - 1); }
        private void OnDisable() { CancelUncommitted(); ClearWaves(); targets.Clear(); }
        private void Dirty()
        {
            if (!NetworkServer.active) return;
            do { revision++; } while (revision == 0);
            SetDirtyBit(1u);
        }
        public override int GetNetworkChannel() => 0; // Reliable ordered snapshots and native skill transitions.
        public override float GetNetworkSendInterval() => 0.05f;
        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            writer.Write(revision);
            writer.Write(sequence.Latest);
            writer.Write((byte)kind);
            writer.Write(sequence.Active);
            writer.Write(rate);
            writer.Write(actionAge);
            writer.Write((byte)waveCount);
            for (int i = 0; i < waveCount; i++)
            {
                Wave wave = waves[i];
                writer.Write(wave.Id); writer.Write(wave.Origin); writer.Write(wave.Strength); writer.Write(wave.Elapsed); writer.Write(wave.Crit);
            }
            HurtBoxReference.FromHurtBox(arcFocus).Write(writer);
            return true;
        }
        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            uint incomingRevision = reader.ReadUInt32(), latest = reader.ReadUInt32();
            Kind incomingKind = (Kind)reader.ReadByte();
            uint id = reader.ReadUInt32();
            float incomingRate = reader.ReadSingle(), age = reader.ReadSingle();
            bool apply = initialState || JakWeaponRules.Newer(incomingRevision, receivedRevision);
            bool owner = Util.HasEffectiveAuthority(gameObject);
            bool applyAction = apply && (!owner || latest == sequence.Latest &&
                (incomingKind == Kind.None || sequence.Active == id && kind == incomingKind));
            int incomingWaves = reader.ReadByte();
            if (incomingWaves > MaxWaves) throw new InvalidOperationException("Invalid Wave snapshot count.");
            // The owner retains predicted finite attacks; server snapshots do not replay their commits.
            bool applyWaves = apply && !owner;
            if (applyWaves) ClearWaves();
            for (int i = 0; i < incomingWaves; i++)
            {
                uint waveId = reader.ReadUInt32(); Vector3 origin = reader.ReadVector3();
                float strength = reader.ReadSingle(), elapsed = reader.ReadSingle(); bool crit = reader.ReadBoolean();
                if (applyWaves)
                {
                    Wave wave = waves[waveCount++];
                    wave.Id = waveId; wave.Origin = origin; wave.Strength = strength; wave.Elapsed = elapsed; wave.Crit = crit;
                }
                else if (apply && owner)
                {
                    // Correct the predicted frozen values without replaying the release or reviving a finished wave.
                    for (int j = 0; j < waveCount; j++)
                        if (waves[j].Id == waveId) { waves[j].Origin = origin; waves[j].Strength = strength; waves[j].Crit = crit; break; }
                }
            }
            HurtBoxReference focus = default;
            focus.Read(reader);
            if (!apply) return;
            receivedRevision = incomingRevision;
            // Focus is server-owned for every peer, including the owner.
            arcFocus = focus.ResolveHurtBox();
            if (!applyAction) return;
            if (!sequence.ApplySnapshot(latest, id)) return;
            kind = incomingKind;
            if (!owner) { rate = JakWeaponRules.SampleRate(incomingRate); actionAge = Mathf.Max(0f, age); }
            // A server-forced cancellation must not leave the owner charging an already ended action.
            if (owner && incomingKind == Kind.None) CancelUncommitted();
        }
    }
}
