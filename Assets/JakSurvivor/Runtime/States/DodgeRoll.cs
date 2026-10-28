using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class DodgeRoll : BaseCharacterMain, ISkillState
    {
        private Vector3 direction;
        private bool leapRequested;
        public GenericSkill activatorSkillSlot { get; set; }

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                direction = Vector3.ProjectOnPlane(inputBank.moveVector, Vector3.up);
                direction = direction.sqrMagnitude > 0.01f ? direction.normalized : characterDirection.forward;
                EntityStateMachine weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
                weapon.SetInterruptState(EntityStateCatalog.InstantiateState(ref weapon.mainStateType), InterruptPriority.Skill);
            }
            PlayCrossfade(JakAnimator.Layers.Gesture, JakAnimator.States.Empty, 0.05f);
            PlayAnimation(JakAnimator.Layers.BodyAction, JakAnimator.States.Roll, JakAnimator.Parameters.RollRate, JakRoll.Duration);
            JakAudio.Play(JakRoll.RollSound(characterBody.footPosition), gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            characterDirection.forward = direction;
            characterDirection.moveVector = direction;
            bool grounded = characterMotor.isGrounded && !characterMotor.Motor.MustUnground();
            float speed = JakRoll.Speed(moveSpeedStat, fixedAge);
            Vector3 velocity = grounded
                ? characterMotor.Motor.GetDirectionTangentToSurface(direction, characterMotor.Motor.GroundingStatus.GroundNormal) * speed
                : direction * speed;
            if (!grounded) velocity.y = characterMotor.velocity.y;
            characterMotor.velocity = velocity;
            characterMotor.moveDirection = characterMotor.walkSpeed > 0f ? velocity / characterMotor.walkSpeed : Vector3.zero;
            bool canLeap = (grounded || characterMotor.lastGroundedTime.timeSince <= JakJump.GroundGraceWindow) &&
                JakJump.SpentCount(characterMotor.jumpCount, grounded) < characterBody.maxJumpCount;
            leapRequested = canLeap && (leapRequested || inputBank.jump.justPressed);
            if (leapRequested && fixedAge >= JakRoll.LeapOpensAt && canLeap)
                outer.SetNextState(new RollLeap { carry = Vector3.ProjectOnPlane(velocity, Vector3.up) });
            else if (fixedAge >= JakRoll.Duration) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            PlayCrossfade(JakAnimator.Layers.BodyAction, JakAnimator.States.Empty, 0.05f);
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            this.Serialize(skillLocator, writer);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            this.Deserialize(skillLocator, reader);
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
