using System.Collections.Generic;
using R2API;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor
{
    public sealed class JakBlastProjectile : NetworkBehaviour
    {
        internal static GameObject Prefab { get; private set; }
        private ProjectileController controller;
        private ProjectileDamage damage;
        private TeamFilter team;
        private Run.FixedTimeStamp bornAt;
        private const float Skin = .01f;
        private Vector3 velocity, normal;
        private float fireHeight;
        private bool impact;
        private readonly JakBlast.Stall stall = new JakBlast.Stall();
        private Collider[] overlaps = new Collider[64];
        private readonly HashSet<HealthComponent> victims = new HashSet<HealthComponent>();
        private JakBlastPresentation presentation;
        internal float Age => bornAt.timeSinceClamped;
        internal float Radius => JakBlast.Radius(Age);
        internal int HitCount => victims.Count;

        internal static GameObject BuildPrefab()
        {
            var source = new GameObject("JakDarkBlastProjectile");
            source.SetActive(false);
            source.AddComponent<NetworkIdentity>().localPlayerAuthority = false;
            source.AddComponent<TeamFilter>();
            var controller = source.AddComponent<ProjectileController>();
            controller.allowPrediction = false;
            controller.authorityHandlesCollisionEvents = false;
            controller.doNotPerformImpactOnCollision = true;
            controller.procCoefficient = Balance.DarkBlastProc;
            controller.startSound = "";
            source.AddComponent<ProjectileDamage>();
            source.AddComponent<ProjectileNetworkTransform>().checkForLocalPlayerAuthority = false;
            source.AddComponent<JakBlastProjectile>();
            Prefab = source.InstantiateClone("JakDarkBlastProjectile", true);
            Object.Destroy(source);
            Prefab.SetActive(true);
            return Prefab;
        }
        private void Awake()
        {
            controller = GetComponent<ProjectileController>();
            damage = GetComponent<ProjectileDamage>();
            team = GetComponent<TeamFilter>();
            controller.onInitialized += Initialized;
        }
        private void Initialized(ProjectileController shot)
        {
            if (!NetworkServer.active) return;
            bornAt = Run.FixedTimeStamp.now;
            velocity = transform.forward * JakBlast.Speed;
            fireHeight = transform.position.y; normal = -transform.forward;
        }
        private void Start()
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                presentation = new GameObject("DarkBlastFlight").AddComponent<JakBlastPresentation>();
                presentation.Begin(this);
            }
        }
        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (Age >= JakBlast.Lifetime) { NetworkServer.Destroy(gameObject); return; }
            float dt = Time.fixedDeltaTime;
            float radius = Radius, core = radius * .1f;
            Vector3 start = transform.position, end = start + velocity * dt;
            // Native half-radius downward terrain probe; sweep the lift as part of the displacement.
            if (Physics.SphereCast(end + Vector3.up * radius * .5f, radius * .5f, Vector3.down, out RaycastHit ground, radius,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                end.y += JakBlast.Lift(end.y, ground.point.y, radius, dt);
            Vector3 move = end - start;
            float length = move.magnitude;
            // Only the core collides with backgnd (one iteration, target-darkjak.gc:151-197); a pinned or embedded core stays put.
            bool wall = Physics.CheckSphere(start, core, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (wall) end = start;
            else if (length > 0f && Physics.SphereCast(start - move / length * Skin, core, move / length, out RaycastHit stop, length + Skin,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                end = start + move / length * Mathf.Max(0f, stop.distance - Skin);
                wall = true; normal = stop.normal;
                impact |= JakBlast.Slide(ref velocity.x, ref velocity.y, ref velocity.z, normal.x, normal.y, normal.z);
            }
            velocity.y = JakBlast.Flatten(velocity.y, dt);
            impact = JakBlast.Impacts(impact, end.y, fireHeight, velocity.magnitude);
            // Victims come from the enemy-only touch sphere for the whole flight, pinned or not (target-darkjak.gc:177-183).
            HitSegment(start, end, radius);
            transform.position = end;
            if (stall.Record((end - start).magnitude, wall) || impact)
            {
                // projectile-method-26 explodes at root trans with no surface orientation (target-darkjak.gc:252-264).
                EffectManager.SpawnEffect(JakEffects.Get("blast-wall"), new EffectData { origin = end, rotation = Util.QuaternionSafeLookRotation(normal) }, true);
                NetworkServer.Destroy(gameObject);
            }
        }
        private void HitSegment(Vector3 start, Vector3 end, float radius)
        {
            int count;
            while ((count = Physics.OverlapCapsuleNonAlloc(start, end, radius, overlaps, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide)) == overlaps.Length)
                System.Array.Resize(ref overlaps, overlaps.Length * 2);
            Vector3 move = end - start;
            TeamMask enemies = TeamMask.GetEnemyTeams(team.teamIndex);
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i]; overlaps[i] = null;
                HurtBox box = collider.GetComponent<HurtBox>();
                if (!JakWeaponTargets.Eligible(box, enemies) || box.healthComponent.gameObject == controller.owner || victims.Contains(box.healthComponent)) continue;
                Vector3 offset = collider.bounds.center - start;
                float fraction = JakBlast.SegmentFraction(offset.x, offset.y, offset.z, move.x, move.y, move.z);
                Vector3 nearest = start + move * fraction, point = collider.ClosestPoint(nearest);
                if (!JakWeaponTargets.Visible(nearest, point) || !victims.Add(box.healthComponent)) continue;
                var info = new DamageInfo {
                    attacker = controller.owner, inflictor = gameObject, damage = damage.damage, crit = damage.crit,
                    procChainMask = controller.procChainMask, procCoefficient = controller.procCoefficient,
                    damageColorIndex = damage.damageColorIndex, damageType = damage.damageType,
                    position = point, force = transform.forward * damage.force, canRejectForce = true, inflictedHurtbox = box
                };
                info.ModifyDamageInfo(box.damageModifier);
                HealthComponent health = box.healthComponent;
                GameObject victim = health.gameObject;
                health.TakeDamage(info);
                GlobalEventManager.instance.OnHitEnemy(info, victim);
                GlobalEventManager.instance.OnHitAll(info, victim);
                EffectManager.SpawnEffect(JakEffects.Get("blast-hit"), new EffectData { origin = point }, true);
            }
        }
        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            if (!initialState) return false;
            Run.FixedTimeStamp.Serialize(writer, bornAt);
            return true;
        }
        public override void OnDeserialize(NetworkReader reader, bool initialState)
        { if (initialState) bornAt = Run.FixedTimeStamp.Deserialize(reader); }
        private void OnDestroy()
        {
            if (controller) controller.onInitialized -= Initialized;
            if (presentation) presentation.Retire();
        }
    }
}
