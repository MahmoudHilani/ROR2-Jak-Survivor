using System.Collections.Generic;
using System.Linq;
using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class SpinAttack : JakMain, ISkillState
    {
        public GenericSkill activatorSkillSlot { get; set; }
        private JakSpinVariant variant;
        private float rate, entryHeading, previousAge;
        private JakSpinTimeline timeline;
        private OverlapAttack attack;
        private JakWeaponCombat combat;
        private JakController controller;
        private JakMain continuation;
        internal float Age => fixedAge;
        internal JakSpinVariant Variant => variant;
        internal float EntryHeading => entryHeading;
        internal JakSpinTimeline Timeline => timeline;

        public override void OnEnter()
        {
            base.OnEnter();
            controller = GetComponent<JakController>();
            if (isAuthority)
            {
                bool grounded = characterMotor.isGrounded && !characterMotor.Motor.MustUnground();
                JakSpinForm form = skillLocator.primary.skillDef == JakContent.ArcWielder ? JakSpinForm.Blue : JakSpinForm.Yellow;
                variant = JakSpin.Variant(!grounded, form);
                rate = JakWeaponRules.SampleRate(attackSpeedStat);
                entryHeading = characterDirection.yaw;
            }
            timeline = JakContent.SpinCatalog.Timeline(variant, rate);
            combat = GetComponent<JakWeaponCombat>();
            ref readonly JakSpinRecipe.Variant recipe = ref JakSpinRecipe.Get(variant);
            PlayCrossfade(JakAnimator.Layers.BodyAction, JakAnimator.Spin.State(variant), JakAnimator.Parameters.SpinRate, timeline.PoseEnd, recipe.EntryBlend);
            if (fixedAge > 0)
            {
                Animator animator = GetModelAnimator();
                animator.Play(JakAnimator.Spin.State(variant), animator.GetLayerIndex(JakAnimator.Layers.BodyAction), fixedAge / timeline.PoseEnd);
            }
            JakAudio.Play(recipe.Sound, gameObject);
            previousAge = fixedAge;
            if (!isAuthority) return;
            attack = new OverlapAttack {
                attacker = gameObject, inflictor = gameObject, teamIndex = GetTeam(),
                damage = Balance.SpinDamage * damageStat, procCoefficient = Balance.SpinProc, isCrit = RollCrit(),
                hitBoxGroup = GetModelTransform().GetComponents<HitBoxGroup>().Single(g => g.groupName == JakAnimator.Spin.HitBoxGroup),
                isSphereOverlap = true, pushAwayForce = Balance.SpinForce,
                damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Secondary),
                modifyOutgoingOverlapInfoCallback = FlattenPush
            };
            if (timeline.Airborne) Hop();
        }

        public override void HandleMovements()
        {
            base.HandleMovements();
            if (isAuthority && timeline.OwnsGun(fixedAge))
            {
                Vector3 direction = Quaternion.Euler(0, entryHeading, 0) * Vector3.forward;
                characterDirection.forward = direction;
                characterDirection.moveVector = direction;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || continuation != null) return;
            if (timeline.CrossesActive(previousAge, fixedAge))
                attack.Fire();
            previousAge = fixedAge;
            if (fixedAge >= timeline.PoseEnd || timeline.Airborne && characterMotor.isGrounded && !characterMotor.Motor.MustUnground())
            {
                continuation = new SpinRecovery { Rate = rate, Variant = variant };
                outer.SetNextState(continuation);
            }
        }

        public override void ProcessJump()
        {
            if (timeline.IsDefault) return;
            switch (timeline.JumpAt(fixedAge))
            {
                case JakSpinJump.Ignored: return;
                case JakSpinJump.CancelSpin:
                    int count = characterMotor.jumpCount;
                    base.ProcessJump();
                    if (isAuthority && characterMotor.jumpCount != count)
                    { continuation = new JakMain(); outer.SetNextState(continuation); }
                    return;
                default: base.ProcessJump(); return;
            }
        }

        protected override bool CanExecuteSkill(GenericSkill slot) => base.CanExecuteSkill(slot) &&
            (slot != skillLocator.special || !timeline.OwnsGun(fixedAge));

        internal bool TryAim(Ray aim, out Ray shot)
        {
            if (!timeline.OwnsGun(fixedAge)) { shot = aim; return false; }
            var direction = JakSpin.ShotDirection(entryHeading + timeline.MuzzleYaw(fixedAge), aim.direction.y);
            shot = new Ray(combat.MuzzleOrigin(), new Vector3(direction.X, direction.Y, direction.Z));
            return true;
        }

        private void Hop()
        {
            if (!JakSpin.HopAvailable(-characterMotor.lastGroundedTime.timeSince, -controller.SpinHop.timeSince)) return;
            jump.Cancel();
            characterMotor.velocity.y = JakSpin.HopVelocity(characterMotor.velocity.y, -Physics.gravity.y);
            characterMotor.Motor.ForceUnground(0f);
            controller.SpinHop = Run.FixedTimeStamp.now;
        }

        private static void FlattenPush(List<OverlapAttack.OverlapInfo> hits)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                OverlapAttack.OverlapInfo hit = hits[i];
                var effect = new EffectData { origin = hit.hitPosition, rotation = Util.QuaternionSafeLookRotation(-hit.pushDirection) };
                effect.SetHurtBoxReference(hit.hurtBox);
                EffectManager.SpawnEffect(JakEffects.Get("spin-hit"), effect, true);
                var direction = JakSpin.Push(hit.pushDirection.x, hit.pushDirection.z, hit.hurtBox.healthComponent.body.isChampion);
                hit.pushDirection = new Vector3(direction.X, 0, direction.Z);
                hits[i] = hit;
            }
        }

        public override void OnExit()
        {
            PlayCrossfade(JakAnimator.Layers.BodyAction, JakAnimator.States.Empty, 0.1f);
            continuation?.ContinueJump(jump);
            base.OnExit();
        }
        public override InterruptPriority GetMinimumInterruptPriority() => timeline.IsDefault || timeline.PhaseAt(fixedAge) == JakSpinPhase.Active
            ? InterruptPriority.PrioritySkill : InterruptPriority.Skill;
        public override void OnSerialize(NetworkWriter writer)
        { base.OnSerialize(writer); this.Serialize(skillLocator, writer); writer.Write((byte)variant); writer.Write(rate); writer.Write(fixedAge); writer.Write(entryHeading); }
        public override void OnDeserialize(NetworkReader reader)
        { base.OnDeserialize(reader); this.Deserialize(skillLocator, reader); variant = (JakSpinVariant)reader.ReadByte(); rate = reader.ReadSingle(); fixedAge = reader.ReadSingle(); entryHeading = reader.ReadSingle(); }
    }
}
