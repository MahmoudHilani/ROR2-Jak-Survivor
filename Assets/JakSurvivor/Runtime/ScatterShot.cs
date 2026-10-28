using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace JakSurvivor
{
    internal readonly struct ScatterShot
    {
        internal const float Range = 20f, Radius = 0.2f, NearRange = 6f;
        internal readonly Vector3 Origin, Direction;
        private readonly Quaternion rotation;

        internal ScatterShot(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction.normalized;
            if (!(Direction.sqrMagnitude > 0f)) throw new System.ArgumentException("Scatter requires a nonzero finite direction.", nameof(direction));
            rotation = Quaternion.LookRotation(Direction);
        }

        internal Vector3 ClosestFanPoint(Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(rotation) * (point - Origin);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z), -Mathf.PI / 4f, Mathf.PI / 4f);
            float horizontal = local.x * Mathf.Sin(yaw) + local.z * Mathf.Cos(yaw);
            float pitch = Mathf.Clamp(Mathf.Atan2(local.y, horizontal), -Mathf.PI / 12f, Mathf.PI / 12f);
            Vector3 ray = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            return Origin + rotation * ray * Mathf.Clamp(Vector3.Dot(local, ray), 0f, Range);
        }

        internal EffectData EffectData => new EffectData { origin = Origin, start = Origin, rotation = rotation };

        internal bool IsBlocked(Vector3 aimOrigin) =>
            Physics.Linecast(aimOrigin, Origin, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ||
            Physics.CheckSphere(Origin, 0.02f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        internal bool Contains(Vector3 point) => (point - ClosestFanPoint(point)).sqrMagnitude <= Radius * Radius + 0.00001f;
        internal float DamageCoefficient(float distance) => distance < NearRange ? 10f : 20f / 3f;

        internal bool TryVisiblePoint(Collider collider, out Vector3 point)
        {
            Bounds bounds = collider.bounds;
            // Check center and fan-clipped corners for range and angular intersections.
            for (int sample = -1; sample < 8; sample++)
            {
                Vector3 seed = sample < 0 ? bounds.center : bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((sample & 1) == 0 ? -1f : 1f, (sample & 2) == 0 ? -1f : 1f, (sample & 4) == 0 ? -1f : 1f));
                point = collider.ClosestPoint(ClosestFanPoint(seed));
                for (int iteration = 0; iteration < 4 && !Contains(point); iteration++)
                    point = collider.ClosestPoint(ClosestFanPoint(point));
                if (TrySurface(collider, ref point)) return true;
            }

            // ClosestPoint seeds stop short of a curved silhouette. Search its edges
            // directly so a thin wall cannot hide an otherwise exposed sphere.
            Vector3 center = bounds.center - Origin;
            float reach = center.magnitude + bounds.extents.magnitude;
            if (collider.Raycast(new Ray(Origin, center.normalized), out _, reach))
            {
                Quaternion facing = Quaternion.LookRotation(center);
                for (int edge = 0; edge < 8; edge++)
                {
                    float angle = edge * Mathf.PI / 4f;
                    Vector3 side = facing * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * bounds.extents.magnitude;
                    float inside = 0f, outside = 1f;
                    for (int step = 0; step < 10; step++)
                    {
                        float fraction = (inside + outside) * 0.5f;
                        if (collider.Raycast(new Ray(Origin, (center + side * fraction).normalized), out _, reach)) inside = fraction;
                        else outside = fraction;
                    }
                    if (collider.Raycast(new Ray(Origin, (center + side * inside).normalized), out RaycastHit surface, reach))
                    {
                        point = surface.point;
                        if (TrySurface(collider, ref point)) return true;
                    }
                }
            }
            point = default;
            return false;
        }

        private bool TrySurface(Collider collider, ref Vector3 point)
        {
            if (!Contains(point)) return false;
            Vector3 offset = point - Origin;
            if (offset.sqrMagnitude > 0.000001f && collider.Raycast(new Ray(Origin, offset.normalized), out RaycastHit surface, offset.magnitude + Radius))
                point = surface.point;
            return Contains(point) && !Physics.Linecast(Origin, point, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        internal void Fire(CharacterBody body, Vector3 aimOrigin, float damage, bool crit)
        {
            // Camera/aim origins can remain behind cover while the posed gun crosses it.
            if (IsBlocked(aimOrigin)) return;
            var accepted = new HashSet<HealthComponent>();
            var attack = new BulletAttack
            {
                owner = body.gameObject, weapon = body.gameObject, origin = Origin, aimVector = Direction,
                teamIndexOverride = body.teamComponent.teamIndex,
                damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Secondary),
                procCoefficient = 1f, force = 250f, isCrit = crit, falloffModel = BulletAttack.FalloffModel.None,
                hitEffectPrefab = null
            };
            foreach (Collider collider in Physics.OverlapSphere(Origin, Range + Radius, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox hurtBox = collider.GetComponent<HurtBox>();
                HealthComponent health = hurtBox ? hurtBox.healthComponent : null;
                if (!health || !health.alive || health == body.healthComponent || accepted.Contains(health) ||
                    TeamComponent.GetObjectTeam(health.gameObject) == body.teamComponent.teamIndex ||
                    !TryVisiblePoint(collider, out Vector3 point)) continue;
                accepted.Add(health);
                Vector3 outward = point - Origin;
                float distance = outward.magnitude;
                // Source damage bands follow the victim, independently of hurtbox order.
                Vector3 victimPosition = health.body ? health.body.corePosition : health.transform.position;
                attack.damage = damage * DamageCoefficient(Vector3.Distance(Origin, victimPosition));
                var hit = new BulletAttack.BulletHit
                {
                    ray = new Ray(Origin, distance > 0.0001f ? outward / distance : Direction), point = point,
                    surfaceNormal = distance > 0.0001f ? -outward / distance : -Direction, distance = distance,
                    collider = collider, entityObject = health.gameObject, hitHurtBox = hurtBox, hurtBox = hurtBox,
                    damageModifier = hurtBox.damageModifier
                };
                BulletAttack.defaultHitCallback(attack, ref hit);
                EffectManager.SpawnEffect(JakEffects.Get("scatter-hit"), new EffectData
                {
                    origin = point, start = Origin, rotation = Quaternion.LookRotation(hit.surfaceNormal)
                }, true);
            }
        }
    }
}
