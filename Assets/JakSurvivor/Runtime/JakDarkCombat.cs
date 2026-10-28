using System;
using System.Collections.Generic;
using System.Reflection;
using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor
{
    // The server owns the native per-victim dark-effect mask; clients only display its snapshots.
    [DisallowMultipleComponent]
    public sealed class JakDarkCombat : NetworkBehaviour
    {
        public readonly struct VictimSnapshot
        {
            public readonly NetworkInstanceId NetId;
            public readonly float StartedAt;
            private readonly CharacterBody victim;

            internal VictimSnapshot(CharacterBody victim, NetworkInstanceId netId, float startedAt)
            { this.victim = victim; NetId = netId; StartedAt = startedAt; }

            public CharacterBody Body
            {
                get
                {
                    if (victim) return victim;
                    GameObject target = NetworkServer.active ? NetworkServer.FindLocalObject(NetId) : ClientScene.FindLocalObject(NetId);
                    return target ? target.GetComponent<CharacterBody>() : null;
                }
            }
            public float Age => Mathf.Max(0f, Run.FixedTimeStamp.now.t - StartedAt);
            internal CharacterBody ServerBody => victim;
        }

        private static readonly HashSet<CharacterBody> flaggedVictims = new HashSet<CharacterBody>();
        // The public pending-state API cannot distinguish a reaction from an attack queued for the same tick.
        private static readonly FieldInfo NextStateField = typeof(EntityStateMachine).GetField("nextState", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<VictimSnapshot> victims = new List<VictimSnapshot>();
        private CharacterBody body;
        private uint version;

        public IReadOnlyList<VictimSnapshot> Victims => victims;
        public uint Version => version;

        private void Awake()
        {
            if (NextStateField == null) throw new MissingFieldException(typeof(EntityStateMachine).FullName, "nextState");
            body = GetComponent<CharacterBody>();
        }
        private void OnEnable() => GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
        private void OnDisable()
        {
            GlobalEventManager.onServerDamageDealt -= OnServerDamageDealt;
            ClearVictims();
        }

        private void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || !body || report.attackerBody != body || !body.healthComponent || !body.healthComponent.alive ||
                !JakContent.DarkBuff || !body.HasBuff(JakContent.DarkBuff) || report.damageDealt <= 0f || report.combinedHealthBeforeDamage <= 0f)
                return;
            CharacterBody victim = report.victimBody;
            if (!victim || flaggedVictims.Contains(victim)) return;
            SetStateOnHurt hurt = victim.GetComponent<SetStateOnHurt>();
            EntityStateMachine machine = hurt ? hurt.targetStateMachine : null;
            // onServerDamageDealt runs after the hurt receiver queues its reaction, before the machine commits it.
            // The current state and combinedHealthBeforeDamage therefore supply the native pre-hit gate.
            if (machine && IsReaction(machine.state, hurt)) return;
            var entry = new VictimSnapshot(victim, victim.netId, Run.FixedTimeStamp.now.t);
            if (!Continue(entry, hurt, body)) return;
            flaggedVictims.Add(victim);
            victims.Add(entry);
            Dirty();
        }

        internal static bool IsReaction(EntityState state, SetStateOnHurt hurt) => state != null &&
            (state is HurtState || state is StunState || state is ShockState || state is FrozenState ||
             hurt && state.GetType() == hurt.hurtState.stateType);

        internal static bool HasPendingReaction(EntityStateMachine machine, SetStateOnHurt hurt) =>
            machine && machine.HasPendingState() && IsReaction((EntityState)NextStateField.GetValue(machine), hurt);

        private static bool Continue(VictimSnapshot entry, SetStateOnHurt hurt, CharacterBody caster)
        {
            CharacterBody victim = entry.ServerBody;
            if (!victim || !victim.healthComponent || entry.Age >= 15f) return false;
            Transform modelTransform = victim.modelLocator ? victim.modelLocator.modelTransform : null;
            CharacterModel model = modelTransform ? modelTransform.GetComponent<CharacterModel>() : null;
            // Camera visibility is local to each player; the shared victim set uses logical visibility even on headless servers.
            TeamIndex observerTeam = caster.teamComponent ? caster.teamComponent.teamIndex : TeamIndex.None;
            if (!model || !model.isActiveAndEnabled || model.invisibilityCount > 0 ||
                victim.GetVisibilityLevel(observerTeam) == VisibilityLevel.Invisible) return false;
            if (!victim.healthComponent.alive) return true;
            EntityStateMachine machine = hurt ? hurt.targetStateMachine : null;
            return machine && (IsReaction(machine.state, hurt) || HasPendingReaction(machine, hurt));
        }

        private void Update()
        {
            if (!NetworkServer.active) return;
            if (!body || !body.healthComponent || !body.healthComponent.alive)
            {
                // RoR2 owns this replicated list on the caster body, so caster death releases its victim masks.
                ClearVictims();
                return;
            }
            for (int i = victims.Count - 1; i >= 0; i--)
            {
                CharacterBody victim = victims[i].ServerBody;
                if (!Continue(victims[i], victim ? victim.GetComponent<SetStateOnHurt>() : null, body)) RemoveVictim(i);
            }
        }

        private void RemoveVictim(int index)
        {
            flaggedVictims.Remove(victims[index].ServerBody);
            victims.RemoveAt(index);
            Dirty();
        }
        private void ClearVictims()
        {
            if (victims.Count == 0) return;
            for (int i = 0; i < victims.Count; i++) flaggedVictims.Remove(victims[i].ServerBody);
            victims.Clear();
            if (NetworkServer.active) Dirty();
        }
        private void Dirty()
        {
            do { version++; } while (version == 0);
            SetDirtyBit(1u);
        }

        public override int GetNetworkChannel() => 0;
        public override float GetNetworkSendInterval() => 0.05f;
        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            writer.Write(version);
            writer.Write(victims.Count);
            for (int i = 0; i < victims.Count; i++)
            {
                writer.Write(victims[i].NetId);
                writer.Write(victims[i].StartedAt);
            }
            return true;
        }
        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            uint incomingVersion = reader.ReadUInt32();
            int count = reader.ReadInt32();
            if (count < 0) throw new InvalidOperationException("Invalid Dark victim snapshot count.");
            bool apply = initialState || unchecked((int)(incomingVersion - version)) > 0;
            if (apply) victims.Clear();
            for (int i = 0; i < count; i++)
            {
                NetworkInstanceId id = reader.ReadNetworkId();
                float startedAt = reader.ReadSingle();
                if (apply) victims.Add(new VictimSnapshot(null, id, startedAt));
            }
            if (apply) version = incomingVersion;
        }
    }
}
