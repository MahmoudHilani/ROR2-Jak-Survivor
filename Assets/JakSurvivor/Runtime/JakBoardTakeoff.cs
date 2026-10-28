using KinematicCharacterController;
using UnityEngine;

namespace JakSurvivor
{
    public sealed class JakBoardTakeoff
    {
        private readonly RaycastHit[] hits = new RaycastHit[KinematicCharacterMotor.MaxCollisionBudget];
        private readonly Collider[] overlaps = new Collider[KinematicCharacterMotor.MaxCollisionBudget];

        public bool LeavesGround(KinematicCharacterMotor motor, Vector3 velocity, float gravity, float seconds)
        {
            float backstep = KinematicCharacterMotor.GroundProbingBackstepDistance;
            float snap = KinematicCharacterMotor.MinimumGroundProbingDistance;
            if (!motor.GroundingStatus.SnappingPrevented)
                snap = (motor.StepHandling == StepHandlingMethod.None ? motor.CurrentCapsuleRadius
                    : Mathf.Max(motor.CurrentCapsuleRadius, motor.MaxStepHeight)) + motor.GroundDetectionExtraDistance;
            Vector3 start = motor.TransientPosition;
            // KCC UpdatePhase1/ProbeGround: the next tick snaps BEFORE moving; downhill PreMove left y clamped to zero.
            if (!motor.GroundingStatus.SnappingPrevented && Sweep(motor, start, backstep, snap, out RaycastHit below) > 0 && Stable(motor, below))
                start -= motor.CharacterUp * (below.distance - backstep - KinematicCharacterMotor.CollisionOffset);
            velocity.y += gravity * seconds;
            Vector3 next = start + velocity * seconds;
            // A concave change puts the prediction inside the rising surface, where the sweep below cannot start; the motor will slide along it.
            if (motor.CharacterCollisionsOverlap(next, motor.TransientRotation, overlaps, 0f, true) > 0) return false;
            // Jak 3 collide-shape.gc:1152-1175,1039-1126; target-board.gc:1657: contact restores ground, no downward snap.
            // KCC ProbeGround leaves CollisionOffset; MinimumGroundProbingDistance is its numerical contact tolerance.
            float contact = KinematicCharacterMotor.CollisionOffset + KinematicCharacterMotor.MinimumGroundProbingDistance;
            int count = Sweep(motor, next, backstep, snap, out _);
            for (int i = 0; i < count; i++)
                if (Stable(motor, hits[i]) && hits[i].distance - backstep <= contact) return false;
            return true;
        }

        private int Sweep(KinematicCharacterMotor motor, Vector3 position, float backstep, float distance, out RaycastHit closest) =>
            motor.CharacterCollisionsSweep(position + motor.CharacterUp * backstep, motor.TransientRotation,
                -motor.CharacterUp, backstep + distance, out closest, hits, 0f, true);

        private static bool Stable(KinematicCharacterMotor motor, RaycastHit hit) =>
            Vector3.Angle(motor.CharacterUp, hit.normal) <= motor.MaxStableSlopeAngle;
    }
}
