using EntityStates;
using RoR2;
using RoR2.Skills;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class WaveConcussor : BaseSkillState
    {
        private JakWeaponCombat combat;
        private JakVisuals visuals;
        private SkillDef startingSkill;
        private uint id;
        private float sampledRate;
        private bool releaseRequested, committing, accepted;
        public float Strength => JakWeaponRules.Charge(fixedAge, sampledRate);

        public override void OnEnter()
        {
            base.OnEnter();
            combat = GetComponent<JakWeaponCombat>();
            visuals = JakVisuals.Find(GetModelTransform());
            startingSkill = activatorSkillSlot ? activatorSkillSlot.skillDef : null;
            if (isAuthority)
            {
                id = combat.AllocateActionId();
                sampledRate = JakWeaponRules.SampleRate(attackSpeedStat);
            }
            accepted = combat.Begin(JakWeaponCombat.Kind.Charge, id, sampledRate, fixedAge);
            if (!accepted) { if (isAuthority) outer.SetNextStateToMain(); return; }
            if (visuals)
            {
                visuals.SetWeaponMode(JakWeaponMode.WaveConcussor);
                JakAnimator.WaveCharge(visuals.Animator, sampledRate, Strength);
            }
            StartAimMode(2f);
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!accepted) return;
            // A replacement/form/board/stun cancellation is never a normal button release.
            if (!JakWeaponCombat.CanUseWeapons(characterBody) || !activatorSkillSlot ||
                startingSkill != JakContent.WaveConcussor || activatorSkillSlot.skillDef != startingSkill)
            {
                combat.End(id);
                if (isAuthority) outer.SetNextStateToMain();
                return;
            }
            combat.SetChargeAge(id, fixedAge);
            StartAimMode(2f);
            if (!isAuthority) return;
            if (!IsKeyDownAuthority() || fixedAge >= Balance.WaveAutoReleaseDelay) releaseRequested = true;
            if (!releaseRequested || !JakWeaponRules.CanRelease(fixedAge, sampledRate) || !combat.HasWaveCapacity) return;
            if (activatorSkillSlot.stock < 1)
            {
                combat.End(id);
                outer.SetNextStateToMain();
                return;
            }
            var recovery = new WaveRecovery {
                activatorSkillSlot = activatorSkillSlot,
                ActionId = id, Strength = Strength, Origin = combat.MuzzleOrigin(), Crit = RollCrit(), SampledRate = sampledRate
            };
            // Exactly one native debit, against the unchanged skill that started the charge.
            // DeductStock intentionally preserves an already-running recharge stopwatch.
            activatorSkillSlot.DeductStock(1);
            committing = true;
            // Immediate transition closes the transaction. A queued recovery could be replaced
            // before entering, leaving an expenditure with no committed finite wave.
            outer.SetState(recovery);
            characterBody.OnSkillActivated(activatorSkillSlot);
        }
        public override void ModifyNextState(EntityState nextState)
        {
            base.ModifyNextState(nextState);
            committing = nextState is WaveRecovery recovery && recovery.ActionId == id;
        }
        public override void OnExit()
        {
            if (!committing)
            {
                if (combat) combat.End(id);
                if (visuals) JakAnimator.StopWeaponAction(visuals.Animator);
                JakController controller = GetComponent<JakController>();
                if (controller) controller.RestorePrimaryWeaponMode();
            }
            base.OnExit();
        }
        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(id); writer.Write(sampledRate); writer.Write(fixedAge); writer.Write(releaseRequested);
        }
        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            id = reader.ReadUInt32(); sampledRate = reader.ReadSingle(); fixedAge = reader.ReadSingle(); releaseRequested = reader.ReadBoolean();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }
}
