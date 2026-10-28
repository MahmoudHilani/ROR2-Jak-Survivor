using EntityStates;
using RoR2;
using RoR2.Skills;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class DarkClaws : BasicMeleeAttack, SteppedSkillDef.IStepSetter
    {
        public int Step { get; private set; }
        private Balance.ClawStep definition;
        public bool InHitStop => authorityInHitPause;
        protected override bool allowExitFire => false;
        public void SetStep(int step) => Step = step;
        public override void OnEnter()
        {
            definition = Balance.ClawCombo[Step];
            baseDuration = definition.Duration;
            damageCoefficient = definition.Damage;
            hitBoxGroupName = JakAnimator.ClawHitBoxGroup;
            mecanimHitboxActiveParameter = JakAnimator.Parameters.ClawHitBoxActive;
            hitEffectPrefab = JakEffects.Get("claw");
            procCoefficient = 1f;
            pushAwayForce = definition.Finisher ? 600f : 200f;
            damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Primary);
            hitPauseDuration = definition.Finisher ? 0.12f : 0.08f;
            scaleHitPauseDurationAndVelocityWithAttackSpeed = true;
            swingEffectPrefab = definition.Finisher ? JakContent.ClawFinisherSlashEffect : JakContent.ClawSlashEffect;
            swingEffectMuzzleString = JakAnimator.ClawSlashChildren[Step];
            base.OnEnter();
            if (characterDirection) characterDirection.forward = GetAimRay().direction;
            JakAudio.Play("claws", gameObject);
        }
        protected override void PlayAnimation()
        {
            if (JakAnimator.HasWeaponAction(animator)) JakAnimator.StopWeaponAction(animator);
            PlayAnimation(JakAnimator.Layers.FullBody, JakAnimator.States.ClawCombo[Step], JakAnimator.Parameters.ClawRate, duration);
        }
        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write((byte)Step); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Step = reader.ReadByte(); }
        public override InterruptPriority GetMinimumInterruptPriority() => fixedAge < definition.HitEnd * duration ? InterruptPriority.PrioritySkill : InterruptPriority.Skill;
    }
}
