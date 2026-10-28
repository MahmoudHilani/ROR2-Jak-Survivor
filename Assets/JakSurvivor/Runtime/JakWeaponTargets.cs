using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace JakSurvivor
{
    // Each body owns its non-alloc query buffers. Never share a search or target set between players.
    internal sealed class JakWeaponTargets : IArcWorld
    {
        private const int SearchLimit = 512;
        private Collider[] colliders = new Collider[SearchLimit];
        private HurtBox[] candidates = new HurtBox[SearchLimit];
        private Vector3[] points = new Vector3[SearchLimit];
        private int count;

        private ArcCandidate[] arcCandidates = new ArcCandidate[SearchLimit];
        private HurtBox[] arcBoxes = new HurtBox[SearchLimit];
        private int arcCount;
        internal Vector3 ArcVisibilityOrigin;
        internal ArcCandidate[] CollectArc(Vector3 origin, TeamIndex ownerTeam, HurtBox focus, out int found)
        {
            int overlaps;
            // A full result may be truncated, so grow and query again.
            while ((overlaps = Physics.OverlapSphereNonAlloc(origin, JakArcTargeting.Reach, colliders,
                LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide)) == colliders.Length) Grow();
            TeamMask enemies = TeamMask.GetEnemyTeams(ownerTeam);
            arcCount = 0;
            for (int i = 0; i < overlaps; i++)
            {
                Collider collider = colliders[i]; colliders[i] = null;
                HurtBox box = collider ? collider.GetComponent<HurtBox>() : null;
                if (!Eligible(box, enemies)) continue;
                AddArc(box);
            }
            // Native retention can outlive the acquisition range. Keep the saved hurtbox
            // available for the no-result path without excluding new competitors.
            if (Eligible(focus, enemies))
            {
                bool present = false;
                for (int i = 0; i < arcCount; i++) if (arcBoxes[i] == focus) { present = true; break; }
                if (!present) AddArc(focus);
            }
            found = arcCount;
            return arcCandidates;
        }
        // Every per-collider buffer keeps the collider buffer's length.
        private void Grow()
        {
            int length = colliders.Length * 2;
            colliders = new Collider[length]; candidates = new HurtBox[length]; points = new Vector3[length];
            arcCandidates = new ArcCandidate[length]; arcBoxes = new HurtBox[length];
        }
        private void AddArc(HurtBox box)
        {
            Bounds bounds = box.collider.bounds;
            Vector3 point = bounds.center;
            arcBoxes[arcCount] = box;
            // get-trans 3.w is the native target sphere radius. RoR2 exposes hurtbox
            // colliders instead; bounding spheres also support capsules and meshes.
            arcCandidates[arcCount++] = new ArcCandidate(box.healthComponent.GetInstanceID(), box.GetInstanceID(),
                new System.Numerics.Vector3(point.x, point.y, point.z), bounds.extents.magnitude);
        }
        bool IArcWorld.Visible(System.Numerics.Vector3 from, int candidate) =>
            Visible(ArcVisibilityOrigin, ArcPoint(candidate));
        internal HurtBox ArcBox(int index) => arcBoxes[index];
        internal Vector3 ArcPoint(int index)
        {
            System.Numerics.Vector3 point = arcCandidates[index].Point;
            return new Vector3(point.X, point.Y, point.Z);
        }
        internal void ReleaseArc()
        {
            for (int i = 0; i < arcCount; i++) arcBoxes[i] = null;
            arcCount = 0;
        }

        // Returns each enemy's closest visible hurtbox point, trying a hurtbox's centre when its
        // closest point is occluded. Testing sight only to the enemy's closest point lets
        // terrain clipping a buried foot or a ledge edge cancel the whole hit.
        internal int QueryVisible(Vector3 origin, float radius, TeamIndex ownerTeam)
        {
            int found;
            while ((found = Physics.OverlapSphereNonAlloc(origin, radius, colliders, LayerIndex.entityPrecise.mask,
                QueryTriggerInteraction.Collide)) == colliders.Length) Grow();
            TeamMask enemies = TeamMask.GetEnemyTeams(ownerTeam);
            count = 0;
            for (int i = 0; i < found; i++)
            {
                Collider collider = colliders[i];
                colliders[i] = null;
                HurtBox box = collider ? collider.GetComponent<HurtBox>() : null;
                if (!Eligible(box, enemies)) continue;
                Vector3 point = collider.ClosestPoint(origin);
                if (!Visible(origin, point) && !Visible(origin, point = collider.bounds.center)) continue;
                int duplicate = -1;
                for (int j = 0; j < count; j++)
                    if (candidates[j].healthComponent == box.healthComponent) { duplicate = j; break; }
                if (duplicate >= 0)
                {
                    float oldDistance = (points[duplicate] - origin).sqrMagnitude;
                    float newDistance = (point - origin).sqrMagnitude;
                    if (newDistance < oldDistance || newDistance == oldDistance && StableLess(box, candidates[duplicate]))
                    { candidates[duplicate] = box; points[duplicate] = point; }
                }
                else { candidates[count] = box; points[count++] = point; }
            }
            return count;
        }
        internal HurtBox Box(int index) => candidates[index];
        internal Vector3 Point(int index) => points[index];
        internal void Clear()
        {
            for (int i = 0; i < count; i++) candidates[i] = null;
            count = 0;
        }
        internal static bool Eligible(HurtBox box, TeamMask enemies) => box && box.isActiveAndEnabled &&
            box.collider && box.collider.enabled && box.healthComponent && box.healthComponent.alive &&
            (!box.hurtBoxGroup || box.hurtBoxGroup.hurtBoxesDeactivatorCounter == 0) && enemies.HasTeam(box.teamIndex);
        internal static bool Visible(Vector3 from, Vector3 to) => !Physics.Linecast(from, to, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        internal static uint TargetId(HurtBox box)
        {
            NetworkIdentity identity = box && box.healthComponent ? box.healthComponent.GetComponent<NetworkIdentity>() : null;
            return identity ? identity.netId.Value : 0;
        }
        internal static bool StableLess(HurtBox a, HurtBox b)
        {
            if (!b) return true;
            uint aId = TargetId(a), bId = TargetId(b);
            if (aId != bId) return aId < bId;
            int aHealth = a.healthComponent.GetInstanceID(), bHealth = b.healthComponent.GetInstanceID();
            return aHealth != bHealth ? aHealth < bHealth : a.indexInGroup < b.indexInGroup;
        }
    }
}
