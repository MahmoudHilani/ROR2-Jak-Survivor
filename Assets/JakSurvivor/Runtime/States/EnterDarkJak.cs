using EntityStates;
using RoR2;

namespace JakSurvivor.States
{
    public sealed class EnterDarkJak : BaseSkillState
    {
        private float duration;

        public override void OnEnter()
        {
            base.OnEnter();
            duration = 0.65f / attackSpeedStat;
            if (isAuthority) characterBody.AddTimedBuffAuthority(JakContent.DarkBuff.buffIndex, Balance.DarkDuration);
            JakAudio.Play("dark-enter", gameObject);
            EffectManager.SpawnEffect(JakEffects.Get("transform"), new EffectData { origin = characterBody.corePosition, scale = 2f }, false);
            PlayAnimation(JakAnimator.Layers.Transformation, JakAnimator.States.DarkEnter, JakAnimator.Parameters.TransformRate, duration);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
