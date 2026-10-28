using EntityStates;
using RoR2;
using UnityEngine;

namespace JakSurvivor.States
{
    public class JakMain : GenericCharacterMain
    {
        private protected JakJump.Hold jump;
        private EntityStateMachine weapon;
        private DarkClaws bufferedBlast;
        // Set on the ground and spent by any jump, so only walking off a ledge earns the grace jump.
        private bool ledgeGrace;
        protected virtual bool BuffersBlast => true;
        internal void ContinueJump(JakJump.Hold hold) => jump = hold;
        // target-darkjak.gc:1865,2160: Dark Bomb ignores jumps and runs without stick input.
        private bool Rooted => weapon && weapon.state is DarkBomb;
        private bool Grounded => characterMotor.isGrounded && !characterMotor.Motor.MustUnground();

        public override void OnEnter()
        {
            base.OnEnter();
            weapon = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
        }

        public override void HandleMovements()
        {
            base.HandleMovements();
            if (!Rooted) return;
            if (characterMotor) characterMotor.moveDirection = Vector3.zero;
            if (characterDirection) characterDirection.moveVector = characterDirection.forward;
        }

        public override void ProcessJump()
        {
            if (!characterMotor || Rooted) return;
            int count = characterMotor.jumpCount;
            // target-util.gc:1336-1351: a ledge walk-off keeps the ground jump within the surface grace.
            if (jumpInputReceived && count == 1 && ledgeGrace && !Grounded &&
                characterMotor.lastGroundedTime.timeSince <= JakJump.GroundGraceWindow)
                characterMotor.jumpCount = count = 0;
            if (count >= characterBody.baseJumpCount)
            {
                base.ProcessJump();
                if (characterMotor.jumpCount != count) jump.Cancel();
                return;
            }
            JakJump.Kind kind = count == 0 ? JakJump.Kind.Ground : JakJump.Kind.Double;
            float velocity = characterMotor.velocity.y;
            base.ProcessJump();
            if (characterMotor.jumpCount == count) return;
            ledgeGrace = false;
            Animator animator = GetModelAnimator();
            if (animator) animator.SetFloat(JakAnimator.Parameters.JumpRate, 1f);
            JakJump.Definition definition = JakJump.Get(kind);
            JakAudio.Play(definition.Sound, gameObject, gain: definition.Gain);
            characterMotor.velocity.y = jump.Start(kind, characterBody.transform.position.y, velocity);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            if (BuffersBlast) BufferBlast();
            if (!characterMotor) return;
            bool grounded = Grounded;
            if (grounded) ledgeGrace = true;
            jump.Step(characterBody.transform.position.y, inputBank.jump.down, grounded, ref characterMotor.velocity.y);
        }

        private void BufferBlast()
        {
            GenericSkill special = skillLocator ? skillLocator.special : null;
            if (inputBank && inputBank.skill4.justPressed && special && special.skillDef == JakContent.DarkBlast &&
                weapon && weapon.state is DarkClaws claws && !weapon.CanInterruptState(InterruptPriority.Skill)) bufferedBlast = claws;
            if (bufferedBlast == null) return;
            if (!weapon || weapon.state != bufferedBlast) { bufferedBlast = null; return; }
            if (!weapon.CanInterruptState(InterruptPriority.Skill)) return;
            bufferedBlast = null;
            // A press still held is fired by HandleSkill itself.
            if (!inputBank.skill4.down && CanExecuteSkill(special) && special.ExecuteIfReady()) inputBank.skill4.hasPressBeenClaimed = true;
        }
    }
}
