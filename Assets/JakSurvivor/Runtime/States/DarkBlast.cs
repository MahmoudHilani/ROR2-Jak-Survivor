using EntityStates;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class DarkBlast : JakMain, ISkillState
    {
        public enum Phase : byte { Charge, Release }
        public GenericSkill activatorSkillSlot { get; set; }
        internal Phase CurrentPhase;
        internal float Rate;
        internal bool Launched { get; private set; }
        internal float Age => fixedAge;
        private JakEffectPlayer charge;
        private uint chargeLease;
        private JakAudio.Voice voice;
        private JakMain continuation;
        private bool recovering;
        private Run.FixedTimeStamp phaseBornAt;

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                if (Rate <= 0f) Rate = JakWeaponRules.SampleRate(attackSpeedStat);
                phaseBornAt = Run.FixedTimeStamp.now;
            }
            if (isAuthority)
            {
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                if (weapon && weapon.state is DarkClaws) weapon.SetState(EntityStateCatalog.InstantiateState(ref weapon.mainStateType));
            }
            PlayCrossfade(JakAnimator.Layers.Gesture, JakAnimator.States.Empty, .05f);
            SeekPose();
            if (CurrentPhase == Phase.Charge)
            {
                Transform hand = FindModelChild("HandR");
                if (hand)
                {
                    charge = JakEffectPlayer.Spawn("blast-charge", hand);
                    chargeLease = charge.LeaseId;
                    charge.Driven = true;
                }
                voice = JakAudio.Play("blast-charge", gameObject);
            }
            else if (fixedAge < .1f) JakAudio.Play("blast-swing", gameObject);
            StartAimMode(2f);
        }
        private bool Valid => characterBody && healthComponent && healthComponent.alive && characterBody.HasBuff(JakContent.DarkBuff) &&
            skillLocator && activatorSkillSlot && activatorSkillSlot == skillLocator.special && activatorSkillSlot.skillDef == JakContent.DarkBlast;
        private void SeekPose()
        {
            Animator animator = GetModelAnimator();
            if (!animator) return;
            string state = CurrentPhase == Phase.Charge ? JakAnimator.Blast.Charge : recovering ? JakAnimator.Blast.Recovery : JakAnimator.Blast.Release;
            float age = recovering ? fixedAge - JakBlast.ReleaseTime / Rate : fixedAge;
            animator.SetFloat(JakAnimator.Blast.Rate, Rate);
            animator.PlayInFixedTime(state, animator.GetLayerIndex(JakAnimator.Layers.BodyAction), Mathf.Max(0f, age * Rate));
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (outer.HasPendingState()) return;
            StartAimMode(2f);
            if (!Valid)
            {
                StopCharge();
                if (isAuthority) outer.SetNextStateToMain();
                return;
            }
            if (characterDirection) characterDirection.forward = GetAimRay().direction;
            if (CurrentPhase == Phase.Charge)
            {
                if (isAuthority && !inputBank.skill4.down)
                {
                    continuation = new DarkBlast { activatorSkillSlot = activatorSkillSlot, CurrentPhase = Phase.Release, Rate = Rate };
                    outer.SetNextState(continuation);
                }
                return;
            }
            if (!recovering && fixedAge * Rate >= JakBlast.ReleaseTime) { recovering = true; SeekPose(); }
            if (!isAuthority) return;
            if (!Launched && JakBlast.CrossesLaunch(fixedAge, Rate))
            {
                if (!Valid || activatorSkillSlot.stock < 1) { outer.SetNextStateToMain(); return; }
                Launched = true;
                Ray aim = GetAimRay();
                Transform hand = FindModelChild("HandR");
                Vector3 origin = hand ? hand.position : characterBody.corePosition;
                Vector3 point = aim.GetPoint(1000f);
                if (Util.CharacterRaycast(gameObject, aim, out RaycastHit hit, 1000f, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore)) point = hit.point;
                Vector3 direction = (point - origin).normalized;
                if (direction.sqrMagnitude < .01f) direction = aim.direction;
                bool crit = RollCrit();
                float damage = damageStat * Balance.DarkBlastDamage;
                activatorSkillSlot.DeductStock(1);
                ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                    projectilePrefab = JakBlastProjectile.Prefab, position = origin, rotation = Util.QuaternionSafeLookRotation(direction),
                    owner = gameObject, damage = damage, crit = crit, force = 0f,
                    damageTypeOverride = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Special)
                });
                characterBody.OnSkillActivated(activatorSkillSlot);
            }
            if (fixedAge * Rate >= JakBlast.ReleaseTime + JakBlast.RecoveryTime)
            {
                continuation = new JakMain();
                outer.SetNextState(continuation);
            }
        }
        protected override bool CanExecuteSkill(GenericSkill slot) => slot == skillLocator.utility && base.CanExecuteSkill(slot);
        protected override bool BuffersBlast => false;
        private void StopCharge()
        {
            if (charge) charge.Release(chargeLease);
            charge = null;
            JakAudio.Stop(voice); voice = null;
        }
        public override void OnExit()
        {
            StopCharge();
            PlayCrossfade(JakAnimator.Layers.BodyAction, JakAnimator.States.Empty, .08f);
            continuation?.ContinueJump(jump);
            base.OnExit();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => CurrentPhase == Phase.Release && !Launched && fixedAge * Rate < JakBlast.LaunchTime
            ? InterruptPriority.PrioritySkill : InterruptPriority.Skill;
        public override void OnSerialize(NetworkWriter writer)
        { base.OnSerialize(writer); this.Serialize(skillLocator, writer); writer.Write((byte)CurrentPhase); writer.Write(Rate); Run.FixedTimeStamp.Serialize(writer, phaseBornAt); writer.Write(Launched); }
        public override void OnDeserialize(NetworkReader reader)
        { base.OnDeserialize(reader); this.Deserialize(skillLocator, reader); CurrentPhase = (Phase)reader.ReadByte(); Rate = reader.ReadSingle(); phaseBornAt = Run.FixedTimeStamp.Deserialize(reader); fixedAge = phaseBornAt.timeSinceClamped; Launched = reader.ReadBoolean(); recovering = CurrentPhase == Phase.Release && fixedAge * Rate >= JakBlast.ReleaseTime; }
    }
}
