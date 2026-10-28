using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class RollLeap : JakMain
    {
        internal Vector3 carry;
        private JakMain nextMain;

        public override void OnEnter()
        {
            base.OnEnter();
            PlayAnimation(JakAnimator.Layers.BodyAction, JakAnimator.States.RollLeap, JakAnimator.Parameters.RollRate, JakRoll.LeapDuration);
            JakJump.Definition definition = JakJump.Get(JakJump.Kind.Roll);
            JakAudio.Play(definition.Sound, gameObject, gain: definition.Gain);
            if (!isAuthority) return;
            bool grounded = characterMotor.isGrounded && !characterMotor.Motor.MustUnground();
            int count = JakJump.SpentCount(characterMotor.jumpCount, grounded);
            int quail = characterBody.inventory ? characterBody.inventory.GetItemCountEffective(RoR2Content.Items.JumpBoost) : 0;
            bool boosted = count < characterBody.baseJumpCount && characterBody.isSprinting && quail > 0 &&
                characterBody.moveSpeed > 0f && characterBody.acceleration * characterMotor.airControl > 0f;
            if (boosted) carry += carry.normalized * JakJump.QuailSpeed(quail, characterBody.acceleration, characterMotor.airControl);
            characterMotor.jumpCount = count + 1;
            Vector3 velocity = carry;
            velocity.y = jump.Start(JakJump.Kind.Roll, characterBody.transform.position.y, characterMotor.velocity.y);
            characterMotor.velocity = velocity;
            characterMotor.Motor.ForceUnground(0f);
            if (boosted)
                EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/BoostJumpEffect"), new EffectData
                {
                    origin = characterBody.footPosition,
                    rotation = Util.QuaternionSafeLookRotation(velocity)
                }, transmit: true);
            characterBody.TriggerJumpEventGlobally();
        }

        public override void HandleMovements()
        {
            base.HandleMovements();
            if (!isAuthority) return;
            characterMotor.moveDirection = characterMotor.walkSpeed > 0f ? carry / characterMotor.walkSpeed : Vector3.zero;
            if (carry.sqrMagnitude > 0f)
            {
                characterDirection.forward = carry.normalized;
                characterDirection.moveVector = carry.normalized;
            }
        }

        public override void ProcessJump() { }

        protected override void PerformSkill(GenericSkill slot, ref InputBankTest.ButtonState buttonState)
        {
            // target.gc:3886-3899: gun-combo leaves the flip for falling before firing.
            if (slot.stateMachine != outer && slot.CanExecute()) ReturnToMain();
            base.PerformSkill(slot, ref buttonState);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && (fixedAge >= JakRoll.LeapDuration ||
                fixedAge >= JakRoll.LeapLandingMinimum && characterMotor.isGrounded)) ReturnToMain();
        }

        private void ReturnToMain()
        {
            nextMain = new JakMain();
            outer.SetNextState(nextMain);
        }

        public override void OnExit()
        {
            nextMain?.ContinueJump(jump);
            PlayCrossfade(JakAnimator.Layers.BodyAction, JakAnimator.States.Empty, 0.05f);
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(carry); }
        public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); carry = reader.ReadVector3(); }
    }
}
