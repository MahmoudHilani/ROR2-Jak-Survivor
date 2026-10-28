using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    internal readonly struct JakShot
    {
        public readonly Ray Ray;
        public readonly bool Spinning;
        public JakShot(Ray ray, bool spinning) { Ray = ray; Spinning = spinning; }
        public float Interval(float normal) => Spinning ? JakSpin.VolleyInterval : normal;
    }

    internal static class JakAim
    {
        public static JakShot Shot(EntityStateMachine body, Ray aim)
        {
            return body && body.state is States.SpinAttack spin && spin.TryAim(aim, out Ray shot)
                ? new JakShot(shot, true) : new JakShot(aim, false);
        }

        public static bool SpinOwnsGun(EntityStateMachine body) =>
            body && body.state is States.SpinAttack spin && spin.Timeline.OwnsGun(spin.Age);
    }
}
