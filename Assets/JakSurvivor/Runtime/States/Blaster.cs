using EntityStates;
using RoR2;
using UnityEngine;

namespace JakSurvivor.States
{
    public sealed class Blaster : BaseSkillState
    {
        private float duration;
        private EntityStateMachine body;
        internal bool Spinning { get; private set; }
        internal Ray ShotRay { get; private set; }
        internal float ShotTime { get; private set; }

        public override void OnEnter()
        {
            base.OnEnter();
            body = EntityStateMachine.FindByCustomName(gameObject, "Body");
            JakShot shot = JakAim.Shot(body, GetAimRay());
            Spinning = shot.Spinning;
            duration = shot.Interval(Balance.BlasterDuration) / attackSpeedStat;
            Ray aim = shot.Ray;
            ShotRay = aim;
            ShotTime = Time.fixedTime;
            if (!shot.Spinning) StartAimMode(GetAimRay(), 2f);
            PlayCrossfade(JakAnimator.Layers.Gesture, JakAnimator.States.FireBlaster, JakAnimator.Parameters.BlasterRate, duration, duration / 8f);
            PlayAnimation(JakAnimator.Layers.Gun, JakAnimator.States.FireBlaster, JakAnimator.Parameters.BlasterRate, duration);
            JakAudio.Play("blaster", gameObject);
            JakEffectPlayer.Spawn("muzzle", FindModelChild("MuzzleRight")).ShotDirection = aim.direction;
            AddRecoil(-0.5f, -0.8f, -0.15f, 0.15f);
            if (isAuthority)
            {
                new BulletAttack
                {
                    owner = gameObject,
                    weapon = gameObject,
                    origin = aim.origin,
                    aimVector = aim.direction,
                    damage = Balance.BlasterDamage * damageStat,
                    damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Primary),
                    procCoefficient = 1f,
                    force = 120f,
                    isCrit = RollCrit(),
                    radius = 0.15f,
                    maxDistance = 250f,
                    falloffModel = BulletAttack.FalloffModel.None,
                    smartCollision = true,
                    tracerEffectPrefab = JakEffects.Get("tracer"),
                    hitEffectPrefab = JakEffects.Get("hit"),
                    hitCallback = Hit,
                    muzzleName = "MuzzleRight"
                }.Fire();
            }
        }

        private static bool Hit(BulletAttack attack, ref BulletAttack.BulletHit hit)
        {
            attack.hitEffectPrefab = JakEffects.Get(hit.hitHurtBox ? "hit" : "terrain");
            return BulletAttack.defaultHitCallback(attack, ref hit);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= duration && isAuthority) outer.SetNextStateToMain();
        }

        // A normal shot yields once a Spin takes the gun, so held primary starts the volley on the spin's first tick.
        public override InterruptPriority GetMinimumInterruptPriority() =>
            !Spinning && JakAim.SpinOwnsGun(body) ? InterruptPriority.Any : InterruptPriority.Skill;
    }
}
