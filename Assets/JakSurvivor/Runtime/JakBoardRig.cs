using System;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    // ModelLocator.LateUpdate positions and tilts the detached model at default order.
    [DefaultExecutionOrder(100)]
    public sealed class JakBoardRig : MonoBehaviour
    {
        private CharacterModel model;
        private Transform drawOffset;
        private Vector3 restPosition, previousPosition;
        private CharacterBody observedBody;
        private JakBoardPhysics.State state;
        public JakBoardPhase Phase => state.Phase;
        public float Lift => state.Lift;
        public bool AirPose => state.AirPose;
        public bool LandingBounce => state.LandingBounce;
        public bool QuickJumpWindow => state.QuickJumpWindow;
        public event Action PhaseChanged;

        private void Awake()
        {
            model = GetComponent<CharacterModel>();
            drawOffset = transform.Find(JakAnimator.DrawOffset);
            restPosition = drawOffset.localPosition;
        }

        public void SetPhase(JakBoardPhase phase)
        {
            bool changed = Phase != phase;
            if (phase == JakBoardPhase.Off)
            {
                state = default;
                observedBody = null;
                drawOffset.localPosition = restPosition;
            }
            else if (changed && state.Phase == JakBoardPhase.Off)
            {
                state = JakBoardPhysics.Step(state, 0f, phase, !model.body || model.body.characterMotor.isGrounded, 0f, 0f);
                observedBody = null;
            }
            else if (changed)
                state = new JakBoardPhysics.State(phase, state.Cushion, state.Shock, state.ShockVelocity,
                    state.Grounded, state.VerticalSpeed, state.AirborneSeconds, state.SecondsSinceLanding,
                    phase == JakBoardPhase.Riding && state.AirPose);
            if (changed) PhaseChanged?.Invoke();
        }

        private void LateUpdate()
        {
            CharacterBody body = model.body;
            Vector3 position = body ? body.footPosition : transform.position;
            float seconds = Time.deltaTime;
            Vector3 velocity = body && body == observedBody && seconds > 0f ? (position - previousPosition) / seconds : Vector3.zero;
            previousPosition = position;
            observedBody = body;
            bool grounded = !body || body.characterMotor.isGrounded;
            float height = 0f;
            // Only airborne riding consumes height (the cushion ends at 3 m).
            if (body && Phase == JakBoardPhase.Riding && !grounded)
                height = Physics.Raycast(position, Vector3.down, out RaycastHit hit, 3f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                    ? hit.distance : float.PositiveInfinity;
            state = JakBoardPhysics.Step(state, seconds, Phase, grounded, height, velocity.y, new Vector2(velocity.x, velocity.z).magnitude);
            // board/target-board.gc:1583-1596: play on bottom-out, not on first ground contact.
            if (LandingBounce) JakAudio.Play("board-bounce", body ? body.gameObject : gameObject, gain: state.LandingBounceGain);
            if (state.GetOffFinished) SetPhase(JakBoardPhase.Off);
            drawOffset.position = transform.TransformPoint(restPosition) + Vector3.up * Lift;
        }
    }
}
