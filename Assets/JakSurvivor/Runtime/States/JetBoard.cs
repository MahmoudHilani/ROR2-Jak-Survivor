using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor.States
{
    public sealed class JetBoard : BaseCharacterMain, ISkillState
    {
        private Vector3 direction;
        private float speed;
        private float airSpeed;
        private readonly JakBoardTakeoff takeoff = new JakBoardTakeoff();
        internal float TargetSpeed { get; private set; }
        private JakJump.Hold jump;
        private float lastGrounded = float.NegativeInfinity;
        private bool ollied;
        private JakBoardRig rig;
        public GenericSkill activatorSkillSlot { get; set; }
        private JakAudio.Voice boardSound;

        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                direction = inputBank.moveVector.sqrMagnitude > 0.01f ? inputBank.moveVector.normalized : characterDirection.forward;
                // target-board.gc:915-1016 leaves transv intact; :143-146 admits speed above the board cap.
                speed = Mathf.Max(characterBody.moveSpeed * Balance.BoardSpeed, new Vector3(characterMotor.velocity.x, 0f, characterMotor.velocity.z).magnitude);
                airSpeed = speed;
                TargetSpeed = speed;
            }
            rig = GetModelTransform().GetComponent<JakBoardRig>();
            rig.SetPhase(JakBoardPhase.Riding);
            if (modelLocator) modelLocator.normalizeToFloor = true;
            PlayAnimation(JakAnimator.Layers.FullBody, JakAnimator.States.BoardOn, JakAnimator.Parameters.BoardRate, 0.3f);
            PlayAnimation(JakAnimator.Layers.Board, JakAnimator.States.BoardOpen);
            JakAudio.Play("board-start", gameObject);
            // target-board.gc:1389 uses 0.8-1.0 on the ground; 0.3 is its distant-air fade, not cruise volume.
            boardSound = JakAudio.Play("board-loop", gameObject);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority) return;
            bool grounded = characterMotor.isGrounded && !characterMotor.Motor.MustUnground();
            if (grounded) { lastGrounded = fixedAge; ollied = false; }
            if (inputBank.moveVector.sqrMagnitude > 0.01f)
                direction = Vector3.RotateTowards(direction, inputBank.moveVector.normalized, 3.5f * GetDeltaTime(), 0f);
            direction.y = 0f;
            direction.Normalize();
            characterDirection.forward = direction;
            characterDirection.moveVector = direction;
            // Sprint stats can update after entry; BaseState.moveSpeedStat is an entry snapshot.
            TargetSpeed = Mathf.Max(speed, characterBody.moveSpeed * Balance.BoardSpeed);
            Vector3 velocity;
            if (grounded)
            {
                // collide-reaction-target.gc:404-424, target-board.gc:134: preserve tangent speed at slope changes.
                velocity = characterMotor.Motor.GetDirectionTangentToSurface(direction, characterMotor.Motor.GroundingStatus.GroundNormal) * TargetSpeed;
                airSpeed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
                float gravity = !characterMotor.useGravity ? 0f : characterMotor.useCustomGravity
                    ? characterMotor.CustomGravity : Physics.gravity.y * characterMotor.gravityScale;
                if (takeoff.LeavesGround(characterMotor.Motor, velocity, gravity, GetDeltaTime()))
                    characterMotor.Motor.ForceUnground(0f);
            }
            else
            {
                velocity = direction * airSpeed;
                velocity.y = characterMotor.velocity.y;
            }
            jump.Step(characterBody.transform.position.y, inputBank.jump.down, grounded, ref velocity.y);
            // target-util.gc:1330-1338: surface grace permits crest takeoff, not a board double jump.
            int count = JakJump.SpentCount(characterMotor.jumpCount, grounded);
            if (inputBank.jump.justPressed && !ollied && (grounded || fixedAge - lastGrounded < JakJump.GroundGraceWindow)
                && count < characterBody.maxJumpCount)
            {
                bool quick = rig.QuickJumpWindow;
                velocity.y = jump.Start(JakJump.Kind.Board, characterBody.transform.position.y, velocity.y,
                    quick ? JakJump.QuickHeight : 0f);
                characterMotor.jumpCount = count + 1;
                characterMotor.Motor.ForceUnground(0f);
                ollied = true;
                JakAudio.Play(quick ? "board-q-jump" : JakJump.Get(JakJump.Kind.Board).Sound, gameObject);
            }
            characterMotor.velocity = velocity;
            // CharacterMotor.PreMove preserves non-flying y and only normalizes sprint targets below unit length.
            characterMotor.moveDirection = characterMotor.walkSpeed > 0f ? velocity / characterMotor.walkSpeed : Vector3.zero;
            if (fixedAge >= Balance.BoardMinimumRide && this.IsKeyJustPressedAuthority(skillLocator, inputBank) || fixedAge >= Balance.BoardDuration)
            {
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            JakAudio.Stop(boardSound);
            if (rig) rig.SetPhase(JakBoardPhase.Dismounting);
            if (modelLocator) modelLocator.normalizeToFloor = false;
            if (healthComponent && healthComponent.alive)
                PlayAnimation(JakAnimator.Layers.FullBody, JakAnimator.States.BoardOff, JakAnimator.Parameters.BoardRate, JakBoardPhysics.DismountSeconds);
            PlayAnimation(JakAnimator.Layers.Board, JakAnimator.States.BoardClose);
            base.OnExit();
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            this.Serialize(skillLocator, writer);
            writer.Write(direction);
            writer.Write(speed);
            writer.Write(airSpeed);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            this.Deserialize(skillLocator, reader);
            direction = reader.ReadVector3();
            speed = reader.ReadSingle();
            airSpeed = reader.ReadSingle();
            TargetSpeed = speed;
        }

        protected override void UpdateAnimationParameters()
        {
            base.UpdateAnimationParameters();
            if (hasModelAnimator && characterAnimParamAvailability.isGrounded)
                modelAnimator.SetBool(AnimationParameters.isGrounded, !rig.AirPose);
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
