using System;
using V = JakSurvivor.JakArcRope.V;

namespace JakSurvivor
{
    internal sealed class JakLightningBolt
    {
        internal sealed class TickClock
        {
            private double time;
            internal int Now { get; private set; }
            internal int Previous { get; private set; }
            internal void Reset(float initial) { time=initial; Now=Previous=(int)Math.Floor(time*300.0); }
            internal void Advance(float delta) { Previous=Now; time+=delta; Now=(int)Math.Floor(time*300.0); }
        }
        internal readonly struct Appearance
        {
            internal readonly float WidthMin, WidthMax;
            internal readonly int RegenerateMin, RegenerateMax, TravelTicks, InnerPoints, Points;
            internal readonly float Offset;
            internal Appearance(bool broad)
            {
                // gun-blue-shot.gc L970-1112, L1228-1292 (seconds truncate to 300 Hz ticks).
                WidthMin=broad ? 2f : 0.3f; WidthMax=broad ? 2f : 0.5f;
                RegenerateMin=broad ? 10 : 50; RegenerateMax=broad ? 20 : 75;
                TravelTicks=broad ? 25 : 150; InnerPoints=broad ? 3 : 4; Points=broad ? 5 : 6; Offset=broad ? 1f : 2f;
            }
        }
        internal struct Vertex
        {
            internal V Position;
            internal float U, V;
            internal byte Alpha;
            // Method 16's source distance along the bolt, in metres from the muzzle.
            internal float Distance;
            internal Vertex(V p,float u,float v,byte alpha,float distance) { Position=p; U=u; V=v; Alpha=alpha; Distance=distance; }
        }
        internal static readonly float Reduction=0.4f*(float)Math.Log(9)/(float)Math.Log(12);
        internal readonly Appearance Style;
        internal readonly V[] Current, Desired;
        internal readonly V[] Starts=new V[12];
        internal readonly byte[] Flags=new byte[12];
        private readonly Random random;
        private int activeSpans,lastGenerate,generateInterval,fadeStart;
        internal int FadeMode { get; private set; }
        internal float FadeScalar { get; private set; }=1f;
        internal float Width { get; }
        internal float UVShift { get; private set; }
        internal float RepeatDistance { get; private set; }=64f;
        internal int Capacity => 2*12*Style.Points;
        internal int UpdateCount { get; private set; }
        internal JakLightningBolt(Random random,bool broad)
        {
            this.random=random; Style=new Appearance(broad);
            Current=new V[Style.Points*11+1]; Desired=new V[Current.Length];
            generateInterval=IntRange(random,Style.RegenerateMin,Style.RegenerateMax);
            Width=FloatRange(random,Style.WidthMin,Style.WidthMax); UVShift=UnitRandom(random);
        }
        // lightning-new.gc L16-75: inclusive integers and uint31 mantissa [0,1).
        internal static int IntRange(Random random,int min,int max)
        {
            if(min<max) max++; else min++;
            float value=FloatRange(random,min,max);
            if(value<0f) value-=1f;
            return (int)value;
        }
        internal static float UnitRandom(Random random) => (random.Next() >> 8)*(1f/8388608f);
        internal static float FloatRange(Random random,float min,float max) => min+(max-min)*UnitRandom(random);
        internal void Setup(JakArcRope.ArcPath path)
        {
            activeSpans=Math.Min(11,path.Count-1); RepeatDistance=path.RepeatDistance;
            Array.Copy(path.Points,Starts,path.Count);
        }
        // method 13, lightning-new.gc L208-237.
        internal void Fade(int mode,int tick)
        {
            if(FadeMode==mode) return;
            if(mode==0 || mode==2) { FadeScalar=1f; fadeStart=tick; }
            if(mode==1) { FadeScalar=0f; fadeStart=tick; }
            if(mode==3) for(int i=0;i<12;i++) Flags[i]&=254;
            FadeMode=mode;
        }
        private void Endpoint(int index,int span)
        {
            Desired[index]=new V(UnitRandom(random)-0.5f,UnitRandom(random)-0.5f,span);
            Current[index].z=span;
        }
        // method 21 visits midpoint, left subtree, right subtree (L110-151).
        internal void Midpoint(int first,int last,float scale)
        {
            if(last-first<=1) return;
            int mid=(first+last)/2;
            Desired[mid]=new V((Desired[first].x+Desired[last].x)*0.5f+scale*(UnitRandom(random)-0.5f),
                (Desired[first].y+Desired[last].y)*0.5f+scale*(UnitRandom(random)-0.5f),(Desired[first].z+Desired[last].z)*0.5f);
            Midpoint(first,mid,scale*Reduction); Midpoint(mid,last,scale*Reduction);
        }
        internal static float Interpolation(int previous,int now,int generated,int travel)
        {
            float old=JakArcRope.Clamp((previous-generated)/(float)travel), next=JakArcRope.Clamp((now-generated)/(float)travel);
            return old<1f ? (next-old)/(1f-old) : 0f;
        }
        // methods 11 and 22; UV advance happens here once per native update, never per camera.
        internal void Update(int tick,int previous)
        {
            UpdateCount++;
            if(FadeMode==2) { FadeScalar=1f-JakArcRope.Clamp((tick-fadeStart)/90f); if(FadeScalar<=0f) Fade(3,tick); }
            else if(FadeMode==1) { FadeScalar=JakArcRope.Clamp((tick-fadeStart)/90f); if(FadeScalar>=1f) Fade(0,tick); }
            if(FadeMode==3) return;
            if(FadeMode==0) FadeScalar=1f;
            for(int s=activeSpans;s<12;s++) Flags[s]&=254;
            bool elapsed=tick-lastGenerate>=generateInterval;
            int stride=Style.InnerPoints+1;
            for(int s=0;s<activeSpans;s++)
            {
                Flags[s]=(byte)(elapsed || (Flags[s]&1)==0 ? Flags[s]|2 : Flags[s]&253);
                if((Flags[s]&2)==0) continue;
                int start=s*stride;
                Array.Copy(Desired,start,Current,start,stride);
                Endpoint(start,s);
            }
            if(elapsed)
            {
                Endpoint(activeSpans*stride,activeSpans);
                Current[activeSpans*stride]=Desired[activeSpans*stride];
                lastGenerate=tick; generateInterval=IntRange(random,Style.RegenerateMin,Style.RegenerateMax);
            }
            for(int s=0;s<activeSpans;s++) if((Flags[s]&2)!=0) Midpoint(s*stride,(s+1)*stride,1f);
            float fraction=Interpolation(previous,tick,lastGenerate,Style.TravelTicks);
            if(previous-lastGenerate<Style.TravelTicks)
            {
                for(int i=0;i<=activeSpans*stride;i++)
                {
                    Current[i].x+=(Desired[i].x-Current[i].x)*fraction; Current[i].y+=(Desired[i].y-Current[i].y)*fraction; Current[i].z=Desired[i].z;
                }
            }
            for(int s=0;s<activeSpans;s++) Flags[s]|=1;
            UVShift+=(tick-previous)/-150f;
            UVShift-=(int)UVShift;
            if(UVShift<1f) UVShift+=1f;
        }
        private static V Transform(V p,V right,V up,V forward) => right*p.x+up*p.y+forward*p.z;
        internal void Emit(V cameraUp,Vertex[] strip1,Vertex[] strip2,out int count1,out int count2)
        {
            count1=count2=0;
            if(FadeMode==3 || activeSpans==0) return;
            float distance=0f,previousV=0f;
            int stride=Style.InnerPoints+1;
            V lastTangent=default;
            for(int s=0;s<activeSpans;s++)
            {
                V direction=(Starts[s+1]-Starts[s]).Unit;
                float length=(Starts[s+1]-Starts[s]).Length;
                V.Frame(direction,direction.x*direction.x+direction.z*direction.z<0.01f ? new V(1,0,0) : new V(0,1,0),out V right,out V up);
                int start=s*stride;
                for(int j=0;j<stride;j++)
                {
                    float offset=j==0 && s==0 ? 0f : Style.Offset;
                    V local=Current[start+j]; local.x*=offset; local.y*=offset; local.z=(local.z-s)*length;
                    int a=j==0 ? start : start+j-1, b=j==0 ? start+1 : start+j;
                    V tangent=Current[b]-Current[a]; tangent.x*=offset; tangent.y*=offset; tangent.z*=length;
                    tangent=Transform(tangent,right,up,direction);
                    Pair(Starts[s]+Transform(local,right,up,direction),distance+local.Length,tangent,cameraUp,
                        strip1,strip2,ref count1,ref count2,ref previousV);
                    lastTangent=tangent;
                }
                distance+=length;
            }
            // method 12 final point uses the last span frame without length scaling (L587-627).
            V finalDirection=(Starts[activeSpans]-Starts[activeSpans-1]).Unit;
            V.Frame(finalDirection,finalDirection.x*finalDirection.x+finalDirection.z*finalDirection.z<0.01f ? new V(1,0,0) : new V(0,1,0),out V finalRight,out V finalUp);
            V final=Current[activeSpans*stride]; final.x=final.y=0f; final.z-=activeSpans;
            Pair(Starts[activeSpans]+Transform(final,finalRight,finalUp,finalDirection),distance,lastTangent,cameraUp,
                strip1,strip2,ref count1,ref count2,ref previousV);
        }
        internal byte Alpha(float distance) => (byte)(int)(128f*(1f-JakArcRope.Clamp(distance/RepeatDistance))*FadeScalar);
        private void Pair(V position,float distance,V tangent,V cameraUp,Vertex[] one,Vertex[] two,ref int n1,ref int n2,ref float previousV)
        {
            V.Frame(tangent.Unit,cameraUp,out V right,out V up);
            V a=position+right*(Width/2f), b=position-right*(Width/2f), c=position+up*(Width/2f), d=position-up*(Width/2f);
            float v=(distance/7f)%6f+UVShift;
            byte alpha=Alpha(distance);
            if(v<previousV && n1>=2 && n2>=2)
            {
                float fraction=(6f+v-previousV)/6f;
                V sa=V.Lerp(one[n1-2].Position,a,fraction), sb=V.Lerp(one[n1-1].Position,b,fraction);
                V sc=V.Lerp(two[n2-2].Position,c,fraction), sd=V.Lerp(two[n2-1].Position,d,fraction);
                float s=one[n1-1].Distance+(distance-one[n1-1].Distance)*fraction;
                Add(one,ref n1,sa,0,6,alpha,s); Add(one,ref n1,sb,1,6,alpha,s); Add(one,ref n1,sa,0,0,alpha,s); Add(one,ref n1,sb,1,0,alpha,s);
                Add(two,ref n2,sc,1,6,alpha,s); Add(two,ref n2,sd,0,6,alpha,s); Add(two,ref n2,sc,1,0,alpha,s); Add(two,ref n2,sd,0,0,alpha,s);
            }
            Add(one,ref n1,a,0,v,alpha,distance); Add(one,ref n1,b,1,v,alpha,distance);
            Add(two,ref n2,c,1,v,alpha,distance); Add(two,ref n2,d,0,v,alpha,distance); previousV=v;
        }
        private void Add(Vertex[] strip,ref int count,V p,float u,float v,byte alpha,float distance)
        {
            if(count<Math.Min(Capacity,strip.Length)) strip[count++]=new Vertex(p,u,v,alpha,distance);
        }
    }
}
