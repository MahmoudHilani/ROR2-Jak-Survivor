using System;

namespace JakSurvivor
{
    internal sealed class JakArcRope
    {
        internal struct V
        {
            internal float x, y, z;
            internal V(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
            internal float Length => (float)Math.Sqrt(Dot(this,this));
            internal V Unit
            {
                get { float length=Length; return length>0f ? this/length : default; }
            }
            public static V operator +(V a,V b) => new V(a.x+b.x,a.y+b.y,a.z+b.z);
            public static V operator -(V a,V b) => new V(a.x-b.x,a.y-b.y,a.z-b.z);
            public static V operator *(V a,float b) => new V(a.x*b,a.y*b,a.z*b);
            public static V operator /(V a,float b) => a*(1f/b);
            internal static float Dot(V a,V b) => a.x*b.x+a.y*b.y+a.z*b.z;
            internal static V Cross(V a,V b) => new V(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
            internal static V Lerp(V a,V b,float t) => a+(b-a)*t;
            internal static V Rotate(V v,V axis,float radians)
            {
                float cosine=(float)Math.Cos(radians);
                return v*cosine+Cross(axis,v)*(float)Math.Sin(radians)+axis*(Dot(axis,v)*(1f-cosine));
            }
            internal static void Frame(V forward,V up,out V right,out V vertical)
            {
                right=Cross(up,forward).Unit;
                // Unity aim can align exactly with camera up; keep the native frame finite.
                if(right.Length==0f) right=Cross(new V(1,0,0),forward).Unit;
                vertical=Cross(forward,right).Unit;
            }
        }
        internal interface IWorld
        {
            float Probe(V start,V end,float radius);
            int Targets { get; }
            // Health id of the entity's single target point (get-trans 3), or 0 when it is not a target.
            // distance is from point to the target's surface.
            int Target(int index,V point,out V centre,out float distance);
            bool Visible(V from,V to);
        }
        internal struct Knot { internal V Point, Direction; internal float Length; }
        internal readonly struct Span
        {
            internal readonly V Start, End;
            // The target the span starts inside; a sweep from its centre must not report it.
            internal readonly int StartId;
            internal Span(V start,V end,int startId) { Start=start; End=end; StartId=startId; }
        }
        internal sealed class ArcPath
        {
            internal readonly V[] Points=new V[Knots];
            internal readonly int[] TargetIds=new int[Knots];
            internal readonly Span[] Spans=new Span[Knots];
            internal int Count, SpanCount;
            internal bool Terminal;
            internal float RepeatDistance => Math.Min(64f,(Points[Count-1]-Points[0]).Length+4f);
        }
        // gun-blue-shot.gc L784-873, L1347-1385, L1627-1682, L1708-1936.
        internal const int Knots=12;
        internal const float KnotLength=6f, TargetRadius=4f, TargetDot=0.6f, KeepRadius=8f;
        // gun-blue-shot.gc L2478-2484 setup-collision!: enemy projectile sphere 4096 (1m).
        // The 12288 (3m) probe at L641-650 only gates projectile spawning. A 1m hit
        // implies that gate passed, so direct damage needs no separate spawn probe.
        internal const float SweepRadius=1f;
        internal const float MaxTurnDegreesPerSecond=720f, CarryDegreesPerSecond=360f, FullReach=(Knots-1)*KnotLength;
        // Feel tunables, bound to the BepInEx config at plugin load.
        internal static float FollowSeconds=0.025f, AimSeconds=0.07f, GrabSeconds=0.08f, ReachSpeed=250f;
        internal readonly Knot[] Constraints=new Knot[Knots];
        internal readonly ArcPath Path=new ArcPath();
        private readonly int[] found=new int[Knots], held=new int[Knots], grabbed=new int[Knots], keep=new int[1];
        private int[] occluded=new int[Knots];
        private readonly float[] weights=new float[Knots];
        private readonly V[] centres=new V[Knots], offsets=new V[Knots];
        private V previousBody, aim, requested;
        internal float Reach { get; private set; }
        internal float RevolveAngle { get; private set; }
        internal float SwayAngle { get; private set; }
        internal void Enter(V fire,V direction,V body)
        {
            previousBody=body; direction=direction.Unit; aim=requested=direction; Reach=0f;
            Array.Clear(held,0,Knots); Array.Clear(grabbed,0,Knots); Array.Clear(weights,0,Knots);
            for(int i=0;i<Knots;i++) Constraints[i]=new Knot { Point=fire+direction*(KnotLength*i),Direction=direction,Length=KnotLength };
        }
        // Exponential follow by angle; frame-rate independent.
        internal static V Follow(V from,V to,float delta,float seconds)
        {
            float dot=V.Dot(from,to); V axis=V.Cross(from,to); float sine=axis.Length;
            if(sine==0f) return dot<0f ? to : from;
            return V.Rotate(from,axis/sine,(float)Math.Atan2(sine,dot)*(1f-(float)Math.Exp(-delta/seconds))).Unit;
        }
        internal void Constrain(float delta)
        {
            for(int i=0;i<Knots-1;i++)
            {
                V segment=Constraints[i+1].Point-Constraints[i].Point;
                float length=segment.Length; segment=segment.Unit;
                V desired=Constraints[i].Direction.Unit;
                float dot=V.Dot(segment,desired);
                V axis=V.Cross(segment,desired);
                float sine=axis.Length;
                // Native skips links within 0.81 degrees (dot 0.9999). Over eleven links that
                // leaves a lasting curl after a fast turn, so every link converges fully.
                // A captured link is steered by the capture write-back instead.
                if(sine>0f && held[i+1]==0)
                {
                    float angle=(float)Math.Atan2(sine,dot);
                    float step=Math.Min(angle*(1f-(float)Math.Exp(-delta/FollowSeconds)),MaxTurnDegreesPerSecond*delta*(float)Math.PI/180f);
                    segment=V.Rotate(segment,axis/sine,step).Unit;
                    Constraints[i+1].Direction=segment;
                    Constraints[i+1].Point=Constraints[i].Point+segment*length;
                }
            }
            for(int i=0;i<Knots-1;i++) Constraints[i+1].Point=Constraints[i].Point+(Constraints[i+1].Point-Constraints[i].Point).Unit*Constraints[i].Length;
        }
        // age, when known, is the replicated action age; every peer then grows its rope on the same tick.
        internal void Update(V fire,V direction,V body,float delta,bool active,IWorld world,float age=-1f)
        {
            if(active) { RevolveAngle+=65536f*delta; SwayAngle+=65536f*delta; }
            V shift=body-previousBody; previousBody=body;
            for(int i=0;i<Knots;i++) Constraints[i].Point+=shift;
            // Smooth aim motion is carried as-is so a tracked target isn't trailed; only jumps (target
            // acquired, lost or switched, or a flick) are eased.
            direction=direction.Unit;
            V axis=V.Cross(requested,direction); float sine=axis.Length, turn=(float)Math.Atan2(sine,V.Dot(requested,direction));
            if(sine>0f && turn<=CarryDegreesPerSecond*delta*(float)Math.PI/180f) aim=V.Rotate(aim,axis/sine,turn).Unit;
            requested=direction; aim=Follow(aim,direction,delta,AimSeconds); direction=aim;
            Constraints[0].Point=fire; Constraints[0].Direction=direction;
            Reach=Math.Min(FullReach,age>=0f ? ReachSpeed*age : Reach+ReachSpeed*delta);
            Constrain(delta);
            // Unreached links extend straight along the aim, so the growing tip shoots where it's pointed.
            for(int k=Knots-1;k>0 && k*KnotLength>Reach;k--) { Constraints[k].Direction=direction; Constraints[k].Point=fire+direction*(k*KnotLength); }
            Array.Clear(Path.TargetIds,0,Knots); Path.Count=1; Path.SpanCount=0; Path.Terminal=false;
            for(int i=0;i<Knots;i++) Path.Points[i]=Constraints[i].Point;
            V back=fire-direction*1.8f, backEnd=back+direction*2.16f;
            float fraction=world.Probe(back,backEnd,0.1f);
            float grab=delta/GrabSeconds;
            if(fraction>=0f) { Path.Points[0]=V.Lerp(back,backEnd,fraction); Path.Terminal=true; Release(1,grab); Array.Clear(held,0,Knots); return; }
            // L1746-1750: a clear backcheck still sweeps for damage around the muzzle.
            Path.Spans[Path.SpanCount++]=new Span(back,backEnd,0);
            int count=0,lastTarget=-1,next=1; float tip=1f;
            for(int k=1;k<Knots;k++)
            {
                float reached=Clamp((Reach-(k-1)*KnotLength)/KnotLength);
                if(reached<=0f) break;
                next=k+1; tip=reached;
                V point=Path.Points[k], centre=default;
                // The growing tip searches from where it is drawn, so it grabs only what it has reached.
                // Only the knot that held a target last frame gets the relaxed keep test, so an
                // earlier knot can't pull it back along the rope.
                keep[0]=held[k];
                // A held link is written onto its target and skipped by Constrain, so from its own point
                // it always passes the keep test and a near knot could never let go. A held knot is tested
                // where its parent link would carry it, and stays while the aim's projectile is on it.
                bool kept=keep[0]!=0;
                V incoming=kept ? Constraints[k-1].Direction : Constraints[k].Direction;
                V search=kept ? Constraints[k-1].Point+incoming*(KnotLength*tip) : V.Lerp(Constraints[k-1].Point,point,tip);
                int id=FindTarget(world,Path.Points[k-1],Constraints[k-1].Point,search,incoming,fire,direction,count,keep,kept ? 1 : 0,out centre);
                if(id!=0)
                {
                    found[count++]=id; Path.TargetIds[k]=id; lastTarget=k;
                    // Unlike native, the capture is written into the stored chain so the knot
                    // settles on the target instead of drifting off it.
                    V link=Follow(Constraints[k].Direction.Unit,(centre-Constraints[k-1].Point).Unit,delta,GrabSeconds);
                    Constraints[k].Direction=link; Constraints[k].Point=Constraints[k-1].Point+link*Constraints[k-1].Length;
                }
                // Native keeps a lost target's direction until the link above moves, so the
                // rope stayed bent toward dead enemies. Untargeted knots follow the rope.
                else Constraints[k].Direction=(Constraints[k].Point-Constraints[k-1].Point).Unit;
                // A knot switching targets fades off the old one before taking the new one.
                if(id!=0 && (weights[k]==0f || grabbed[k]==id)) { grabbed[k]=id; centres[k]=centre; weights[k]=Math.Min(1f,weights[k]+grab); }
                else Release(k,k+1,grab);
                point=V.Lerp(point,centres[k],weights[k]);
                V start=Path.Points[k-1];
                if(tip<1f) point=V.Lerp(start,point,tip);
                fraction=world.Probe(start,point,0.21f);
                if(fraction>=0f) { point=V.Lerp(start,point,fraction); Path.Terminal=true; }
                Path.Points[k]=point; Path.Count++;
                // The span starts inside the previous knot's target only once that knot is fully on it.
                Path.Spans[Path.SpanCount++]=new Span(start,point,weights[k-1]>=1f ? grabbed[k-1] : 0);
                if(fraction>=0f || tip<1f) break;
            }
            Release(next,grab);
            Array.Copy(Path.TargetIds,held,Knots);
            if(lastTarget==Path.Count-2) Path.Terminal=false;
            for(int k=1;k<Path.Count;k++)
            {
                V.Frame(Constraints[k].Direction,new V(0,1,0),out V right,out V up);
                short angle=unchecked((short)(int)(RevolveAngle+6144f*k));
                offsets[k]=V.Rotate(up,Constraints[k].Direction,angle*(2f*(float)Math.PI/65536f)).Unit*(0.5f*(1f-weights[k]));
            }
            // The growing tip sits inside its link, so its revolve blends between both knots.
            for(int k=1;k<Path.Count;k++) Path.Points[k]+=k<Path.Count-1 || tip>=1f ? offsets[k] : V.Lerp(k>1 ? offsets[k-1] : default,offsets[k],tip);
        }
        private void Release(int from,float grab) => Release(from,Knots,grab);
        private void Release(int from,int to,float grab)
        {
            for(int k=from;k<to;k++) { weights[k]=Math.Max(0f,weights[k]-grab); if(weights[k]==0f) grabbed[k]=0; }
        }
        // Nearest candidate to the search point; only it is line-of-sight tested from the previous
        // drawn point, and an occluded one is skipped on retry.
        private int FindTarget(IWorld world,V from,V previous,V point,V direction,V fire,V aim,int count,int[] held,int heldCount,out V centre)
        {
            int blocked=0;
            while(true)
            {
                int id=0; float nearest=KeepRadius; centre=default;
                for(int i=0,n=world.Targets;i<n;i++)
                {
                    int candidate=world.Target(i,point,out V target,out float distance);
                    if(heldCount>0 && candidate==held[0] && !Has(found,count,candidate) && OnAim(fire,aim,target)) distance=0f;
                    else if(!Candidate(previous,direction,target,distance,candidate,found,count,held,heldCount,nearest)) continue;
                    if(Has(occluded,blocked,candidate)) continue;
                    nearest=distance; id=candidate; centre=target;
                }
                if(id==0 || world.Visible(from,centre)) return id;
                if(blocked==occluded.Length) Array.Resize(ref occluded,blocked*2);
                occluded[blocked++]=id;
            }
        }
        // distance is from the knot to the target's surface. The in-front test runs from the
        // previous knot, unlike native, so a target on or just behind the knot still counts.
        // A target held last frame stays locked within a wider, relaxed test so it doesn't flicker.
        internal static bool Candidate(V previous,V direction,V centre,float distance,int id,int[] excluded,int count,int[] held,int heldCount,float nearest)
        {
            if(id==0 || distance>=nearest || Has(excluded,count,id)) return false;
            bool kept=Has(held,heldCount,id);
            float dot=V.Dot((centre-previous).Unit,direction);
            return kept ? distance<KeepRadius && dot>0f : distance<TargetRadius && dot>TargetDot;
        }
        private static bool Has(int[] ids,int count,int id)
        {
            for(int i=0;i<count;i++) if(ids[i]==id) return true;
            return false;
        }
        private static bool OnAim(V fire,V aim,V centre)
        {
            V offset=centre-fire; float along=V.Dot(offset,aim);
            return along>0f && (offset-aim*along).Length<SweepRadius;
        }
        internal static float Clamp(float value) => Math.Max(0f,Math.Min(1f,value));
    }
}
