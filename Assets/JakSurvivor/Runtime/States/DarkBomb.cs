using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    // target-darkjak.gc:1862-2160 (target-darkjak-bomb0). JakMain roots stick input while this runs (:2160 no-stick post).
    public sealed class DarkBomb : BaseSkillState
    {
        // target-darkjak.gc:1984,2011: -491520 units/s^2 on top of gravity until landing.
        private const float SlamAcceleration = 491520f / 4096f;
        internal enum Phase : byte { Windup, Falling, Landing }
        internal Phase CurrentPhase;
        internal float Rate;
        private Run.FixedTimeStamp phaseBornAt;
        private bool detonated, continuing;
        private Animator animator;
        internal float ClipTime => CurrentPhase == Phase.Falling ? JakAnimator.Bomb.FallTime :
            Mathf.Min(CurrentPhase == Phase.Windup ? JakAnimator.Bomb.FallTime : JakAnimator.Bomb.Duration,
                (CurrentPhase == Phase.Landing ? JakAnimator.Bomb.FallTime : 0f) + phaseBornAt.timeSinceClamped * Rate);

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                if (Rate <= 0f) Rate = JakWeaponRules.SampleRate(attackSpeedStat);
                phaseBornAt = Run.FixedTimeStamp.now;
            }
            animator = GetModelAnimator();
            if (JakAnimator.HasWeaponAction(animator)) JakAnimator.StopWeaponAction(animator);
            PlayAnimation(JakAnimator.Layers.FullBody, JakAnimator.States.Bomb, JakAnimator.Parameters.BombRate, JakAnimator.Bomb.Duration / Rate);
            if (animator)
            {
                // PlayInFixedTime scales by the playback-rate parameter, which is zero while falling.
                animator.Play(JakAnimator.States.Bomb, animator.GetLayerIndex(JakAnimator.Layers.FullBody), ClipTime / JakAnimator.Bomb.Duration);
                if (CurrentPhase == Phase.Falling) animator.SetFloat(JakAnimator.Parameters.BombRate, 0f);
            }
            if (CurrentPhase == Phase.Windup && phaseBornAt.timeSinceClamped < .1f)
                JakAudio.Play("bomb-charge", gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || outer.HasPendingState()) return;
            // target-darkjak.gc:1970-2021: the windup's falling pose holds until Jak lands.
            // Any ground contact counts, so a slope too steep to stand on can't hold the slam forever.
            bool landed = isGrounded || characterMotor && characterMotor.Motor.GroundingStatus.FoundAnyGround;
            if (CurrentPhase == Phase.Windup && ClipTime >= JakAnimator.Bomb.FallTime)
                Continue(landed ? Phase.Landing : Phase.Falling);
            else if (CurrentPhase == Phase.Falling && landed) Continue(Phase.Landing);
            if (characterMotor)
            {
                Vector3 velocity = characterMotor.velocity;
                // target-darkjak.gc:1967,1976 keep 98% of airborne momentum per frame; :2023,2135 stop on the ground.
                float keep = landed || detonated ? 0f : Mathf.Pow(0.98f, GetDeltaTime() * 60f);
                velocity.x *= keep;
                velocity.z *= keep;
                if (CurrentPhase == Phase.Falling) velocity.y -= SlamAcceleration * GetDeltaTime();
                characterMotor.velocity = velocity;
            }
            if (!detonated && CurrentPhase == Phase.Landing && ClipTime >= JakAnimator.Bomb.Duration * JakAnimator.Bomb.ImpactFraction) Detonate();
            if (ClipTime >= JakAnimator.Bomb.Duration) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (animator && CurrentPhase == Phase.Falling) animator.SetFloat(JakAnimator.Parameters.BombRate, Rate);
            if (isAuthority && !continuing && !detonated && !outer.destroying && healthComponent && healthComponent.alive) Detonate();
            base.OnExit();
        }

        private void Continue(Phase phase)
        {
            // A phase transition sends the authority's landing decision to existing and late observers.
            continuing = true;
            outer.SetNextState(new DarkBomb { CurrentPhase = phase, Rate = Rate });
        }

        private void Detonate()
        {
            detonated = true;
            // target-darkjak.gc:2044,2078-2086: blast and effect share one origin at Jak's body-center height.
            Vector3 origin = characterBody.corePosition;
            EffectManager.SpawnEffect(JakEffects.Get("bomb"), new EffectData { origin = origin, scale = Balance.DarkBombRadius }, true);
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = GetTeam(),
                position = origin,
                radius = Balance.DarkBombRadius,
                baseDamage = Balance.DarkBombDamage * damageStat,
                baseForce = 1400f,
                procCoefficient = 1f,
                crit = RollCrit(),
                damageType = new DamageTypeCombo(DamageType.Stun1s, DamageTypeExtended.Generic, DamageSource.Secondary),
                falloffModel = BlastAttack.FalloffModel.None,
                losType = BlastAttack.LoSType.NearestHit,
                attackerFiltering = AttackerFiltering.NeverHitSelf
            }.Fire();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write((byte)CurrentPhase); writer.Write(Rate);
            Run.FixedTimeStamp.Serialize(writer, phaseBornAt);
            writer.Write(detonated);
        }
        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            CurrentPhase = (Phase)reader.ReadByte(); Rate = reader.ReadSingle();
            phaseBornAt = Run.FixedTimeStamp.Deserialize(reader);
            detonated = reader.ReadBoolean();
        }
    }
}
