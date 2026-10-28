using EntityStates;
using RoR2;
using UnityEngine;

namespace JakSurvivor.States
{
    public sealed class ScatterGun : BaseSkillState
    {
        private float duration;
        private float committedDuration;

        public override void OnEnter()
        {
            base.OnEnter();
            duration = JakAssets.ScatterRecovery / attackSpeedStat;
            committedDuration = 0.1f / attackSpeedStat;
            Ray aim = GetAimRay();
            Transform muzzle = FindModelChild("MuzzleRight");
            var shot = new ScatterShot(muzzle.position, aim.direction);
            StartAimMode(aim, 2f);
            PlayCrossfade(JakAnimator.Layers.Gesture, JakAnimator.States.FireScatter, JakAnimator.Parameters.ScatterRate, duration, duration / 8f);
            PlayAnimation(JakAnimator.Layers.Gun, JakAnimator.States.FireScatter, JakAnimator.Parameters.ScatterRate, duration);
            JakAudio.Play("scatter", gameObject);
            EffectData effect = shot.EffectData;
            effect.genericBool = shot.IsBlocked(aim.origin);
            EffectManager.SpawnEffect(JakEffects.Get("scatter"), effect, false);
            AddRecoil(-2.5f, -3f, -0.5f, 0.5f);
            if (isAuthority) shot.Fire(characterBody, aim.origin, damageStat, RollCrit());
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= duration && isAuthority) outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() =>
            fixedAge < committedDuration ? InterruptPriority.PrioritySkill : InterruptPriority.Any;
    }
}
