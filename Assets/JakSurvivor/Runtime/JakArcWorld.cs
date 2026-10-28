using System.Collections.Generic;
using RoR2;
using UnityEngine;
using V = JakSurvivor.JakArcRope.V;

namespace JakSurvivor
{
    internal sealed class JakArcWorld : JakArcRope.IWorld
    {
        private readonly CharacterBody owner;
        private struct TargetSnapshot { internal int Id; internal Bounds Bounds; }
        private readonly List<TargetSnapshot> targets = new List<TargetSnapshot>(128);
        private RaycastHit[] hits=new RaycastHit[32];
        // The server's replicated first target. Its entity is aimed at this hurtbox, not its main one.
        internal HurtBox Focus;
        internal JakArcWorld(CharacterBody owner) { this.owner=owner; }
        private TeamMask Enemies => TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex);
        internal static V ToV(Vector3 p) => new V(p.x,p.y,p.z);
        internal static Vector3 ToVector(V p) => new Vector3(p.x,p.y,p.z);
        public float Probe(V start,V end,float radius)
        {
            Vector3 move=ToVector(end-start); float length=move.magnitude;
            if(length==0f) return -1f;
            return Physics.SphereCast(ToVector(start),radius,move/length,out RaycastHit hit,length,LayerIndex.world.mask,QueryTriggerInteraction.Ignore) ? hit.distance/length : -1f;
        }
        // All knots in a rope step observe the same ordered bodies and collider bounds.
        internal void BeginUpdate()
        {
            targets.Clear();
            TeamMask enemies=Enemies;
            var bodies=CharacterBody.readOnlyInstancesList;
            bool hasFocus=JakWeaponTargets.Eligible(Focus,enemies);
            for(int i=0;i<bodies.Count;i++)
            {
                CharacterBody body=bodies[i];
                HurtBox box=body ? body.mainHurtBox : null;
                if(box && hasFocus && Focus.healthComponent==box.healthComponent) box=Focus;
                if(JakWeaponTargets.Eligible(box,enemies))
                    targets.Add(new TargetSnapshot { Id=box.healthComponent.GetInstanceID(), Bounds=box.collider.bounds });
            }
        }
        public int Targets => targets.Count;
        public int Target(int index,V point,out V centre,out float distance)
        {
            TargetSnapshot target=targets[index];
            centre=ToV(target.Bounds.center);
            distance=(ToV(target.Bounds.ClosestPoint(ToVector(point)))-point).Length;
            return target.Id;
        }
        public bool Visible(V from,V to) => JakWeaponTargets.Visible(ToVector(from),ToVector(to));
        // Nearest enemy along the span, skipping the target it starts inside and this window's victims.
        internal HurtBox Sweep(JakArcRope.Span span,HashSet<int> victims,out Vector3 point)
        {
            point=default;
            Vector3 start=ToVector(span.Start), move=ToVector(span.End-span.Start);
            float length=move.magnitude;
            if(length==0f) return null;
            Vector3 direction=move/length;
            if(Physics.SphereCast(start,0.2f,direction,out RaycastHit wall,length,LayerIndex.world.mask,QueryTriggerInteraction.Ignore)) length=wall.distance;
            int count;
            while((count=Physics.SphereCastNonAlloc(start,JakArcRope.SweepRadius,direction,hits,length,LayerIndex.entityPrecise.mask,QueryTriggerInteraction.Collide))==hits.Length)
                hits=new RaycastHit[hits.Length*2];
            TeamMask enemies=Enemies;
            while(true)
            {
                HurtBox first=null; int nearest=-1;
                for(int i=0;i<count;i++)
                {
                    if(hits[i].distance<0f || nearest>=0 && hits[i].distance>=hits[nearest].distance) continue;
                    Collider collider=hits[i].collider;
                    HurtBox box=collider ? collider.GetComponent<HurtBox>() : null;
                    if(!JakWeaponTargets.Eligible(box,enemies)) continue;
                    int id=box.healthComponent.GetInstanceID();
                    if(id==span.StartId || victims.Contains(id)) continue;
                    nearest=i; first=box;
                }
                if(!first) { point=default; return null; }
                // Unity reports colliders overlapping the start sphere at distance 0 with point (0,0,0).
                RaycastHit hit=hits[nearest];
                point=hit.distance>0f ? hit.point : hit.collider is MeshCollider mesh && !mesh.convex ? hit.collider.bounds.center : hit.collider.ClosestPoint(start);
                // The enemy sphere can reach past the smaller wall clip; line test the chosen hit.
                if(JakWeaponTargets.Visible(start,point)) return first;
                hits[nearest].distance=-1f;
            }
        }
        // Presentation and damage share this; it reads the focus and never changes targeting state.
        // Each rope eases its own root toward the result.
        internal Vector3 Direction(Vector3 fire,in JakShot shot)
        {
            if(shot.Spinning) return shot.Ray.direction.normalized;
            TeamMask enemies=Enemies;
            if(JakWeaponTargets.Eligible(Focus,enemies)) return (Focus.collider.bounds.center-fire).normalized;
            Vector3 end=shot.Ray.GetPoint(1000f); float nearest=1000f;
            if(Physics.Raycast(shot.Ray,out RaycastHit wall,1000f,LayerIndex.world.mask,QueryTriggerInteraction.Ignore)) { end=wall.point; nearest=wall.distance; }
            int count;
            while((count=Physics.RaycastNonAlloc(shot.Ray,hits,nearest,LayerIndex.entityPrecise.mask,QueryTriggerInteraction.Collide))==hits.Length)
                hits=new RaycastHit[hits.Length*2];
            for(int i=0;i<count;i++)
            {
                HurtBox box=hits[i].collider ? hits[i].collider.GetComponent<HurtBox>() : null;
                if(hits[i].distance<nearest && JakWeaponTargets.Eligible(box,enemies)) { end=hits[i].point; nearest=hits[i].distance; }
            }
            return (end-fire).normalized;
        }
    }
}
