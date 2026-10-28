using EntityStates;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class WaveRecovery : BaseSkillState
    {
        public uint ActionId;
        public float Strength, SampledRate;
        public Vector3 Origin;
        public bool Crit;
        private JakVisuals visuals;
        public override void OnEnter()
        {
            base.OnEnter();
            var combat = GetComponent<JakWeaponCombat>();
            bool committed = combat.CommitWave(ActionId, Strength, Origin, Crit);
            // Commit presentation here rather than waiting for a render-frame poll:
            // a tap's entire combat wave can finish during fixed-step catch-up.
            // Remote peers receive this committed state; they never apply damage.
            if (committed || !isAuthority && !NetworkServer.active)
            {
                Vector3 origin = Origin;
                float strength = Strength;
                if (committed)
                {
                    var wave = combat.GetWave(combat.WaveCount - 1);
                    origin = wave.Origin;
                    strength = wave.Strength;
                }
                var presentation = GetComponent<JakWeaponPresentation>();
                if (presentation) presentation.SetWave(ActionId, origin, strength,
                    JakWeaponRules.ExpandingRadius(strength, fixedAge), fixedAge);
            }
            visuals = JakVisuals.Find(GetModelTransform());
            if (visuals)
            {
                visuals.SetWeaponMode(JakWeaponMode.WaveConcussor);
                JakAnimator.WaveRelease(visuals.Animator, SampledRate);
            }
            StartAimMode(2f);
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && fixedAge >= Balance.WaveRecoveryDuration / JakWeaponRules.SampleRate(SampledRate)) outer.SetNextStateToMain();
        }
        public override void OnExit()
        {
            if (visuals) JakAnimator.StopWeaponAction(visuals.Animator);
            JakController controller = GetComponent<JakController>();
            if (controller) controller.RestorePrimaryWeaponMode();
            base.OnExit();
        }
        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(ActionId); writer.Write(Strength); writer.Write(SampledRate); writer.Write(Origin); writer.Write(Crit); writer.Write(fixedAge);
        }
        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            ActionId = reader.ReadUInt32(); Strength = reader.ReadSingle(); SampledRate = reader.ReadSingle();
            Origin = reader.ReadVector3(); Crit = reader.ReadBoolean(); fixedAge = reader.ReadSingle();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
