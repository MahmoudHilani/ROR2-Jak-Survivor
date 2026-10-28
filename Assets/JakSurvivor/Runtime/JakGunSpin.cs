using System;

namespace JakSurvivor
{
    internal sealed class JakGunSpin
    {
        // target-gun.gc:3340-3352.
        public const float MaximumSpeed = 218453.33f;
        public float Spin { get; private set; }
        public float Speed { get; private set; }
        public float Normalized => Speed / MaximumSpeed;

        public void Step(bool held, float dt)
        {
            float target = held ? MaximumSpeed : 0f;
            float step = (held ? 145635.56f : 109226.664f) * dt;
            Speed = Speed < target ? Math.Min(target, Speed + step) : Math.Max(target, Speed - step);
            Spin = unchecked((short)(int)(Spin + Speed * dt));
        }
    }
}
