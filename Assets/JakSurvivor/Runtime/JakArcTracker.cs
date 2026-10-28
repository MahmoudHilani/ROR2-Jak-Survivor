using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;
using V = JakSurvivor.JakArcRope.V;

namespace JakSurvivor
{
    [DisallowMultipleComponent, DefaultExecutionOrder(10010)]
    internal sealed class JakArcTracker : MonoBehaviour
    {
        internal enum Phase { Active, Inactive, Dead }
        internal Phase State { get; private set; }=Phase.Dead;
        internal JakArcRope Rope { get; private set; }
        internal JakLightningBolt[] Bolts { get; private set; }
        internal int CosmeticHitCount { get; private set; }
        internal int ResourceCount => meshes==null ? 0 : meshes.Length;
        internal Transform Muzzle => muzzle;
        // The model layer, not the body collision layer, is in the gameplay camera mask.
        private int Layer => muzzle ? muzzle.gameObject.layer : gameObject.layer;
        internal bool CaptureHidden;
        private readonly JakLightningBolt.TickClock clock=new JakLightningBolt.TickClock();
        private readonly HashSet<int> victims=new HashSet<int>();
        private JakWeaponRules.PulseGate gate;
        private CharacterBody body;
        private JakWeaponPresentation presentation;
        private JakWeaponCombat combat;
        private JakArcWorld world;
        private Transform muzzle,model;
        private System.Random random;
        private Mesh[] meshes;
        private Material[] materials;
        private JakLightningBolt.Vertex[][] strips;
        private JakEffectPlayer effect;
        private uint effectLease;
        private JakAudio.Voice loop,spin;
        private Vector3[] positions;
        private Vector2[] uvs, ends;
        private Color32[] colors;
        private JakShockSpec explodeSpec,terminalSpec;
        private float spinIntensity;
        private int lastSpark,sparkInterval,stateTick,sampledFrame;
        private bool requested;
        private uint actionId;
        internal void Drive(uint id,Transform follow)
        {
            SampleClock();
            requested=id!=0 && follow;
            if(!requested) return;
            body=GetComponent<CharacterBody>(); presentation=GetComponent<JakWeaponPresentation>(); combat=GetComponent<JakWeaponCombat>();
            if(!body) return;
            Transform current=body.modelLocator ? body.modelLocator.modelTransform : null;
            if(model!=current || muzzle!=follow) Kill();
            model=current; muzzle=follow; requested=true;
            if(State==Phase.Active && (!effect || !effect.IsLease(effectLease))) Release();
            if(State==Phase.Active) { actionId=id; return; }
            if(Rope==null) Initialize(id);
            Rope.Enter(JakArcWorld.ToV(muzzle.position),JakArcWorld.ToV(Aim()),JakArcWorld.ToV(body.transform.position));
            foreach(var bolt in Bolts) bolt.Fade(0,clock.Now);
            State=Phase.Active; actionId=id;
            effect=JakEffectPlayer.Spawn("arc-muzzle",muzzle.position,Quaternion.identity,muzzle);
            effectLease=effect.LeaseId;
            effect.gameObject.layer=Layer; effect.Driven=true;
            stateTick=clock.Now;
        }
        private void Initialize(uint id)
        {
            lastSpark=sparkInterval=0;
            random=new System.Random(unchecked((int)id)); Rope=new JakArcRope(); world=new JakArcWorld(body);
            explodeSpec=JakEffects.ShockSpec("lightning-blue-shot-attack-explode"); terminalSpec=JakEffects.ShockSpec("lightning-blue-shot-terminal");
            Bolts=new JakLightningBolt[6]; meshes=new Mesh[12]; materials=new Material[2]; strips=new JakLightningBolt.Vertex[12][];
            for(int i=0;i<6;i++)
            {
                Bolts[i]=new JakLightningBolt(random,i>=4);
                for(int j=0;j<2;j++)
                {
                    int n=i*2+j, capacity=Bolts[i].Capacity;
                    strips[n]=new JakLightningBolt.Vertex[capacity];
                    meshes[n]=new Mesh { name="JakNativeArcStrip" }; meshes[n].MarkDynamic();
                    meshes[n].vertices=new Vector3[capacity]; meshes[n].triangles=Triangles(capacity);
                }
            }
            int largest=Bolts[0].Capacity;
            positions=new Vector3[largest]; uvs=new Vector2[largest]; ends=new Vector2[largest]; colors=new Color32[largest];
            materials[0]=CreateMaterial("jak3-vfx-lightning-tile.png"); materials[1]=CreateMaterial("jak3-vfx-pal-lightning.png");
        }
        internal static Material CreateMaterial(string texture)
        {
            var material=new Material(JakEffects.Assets.lightningShader);
            material.SetTexture("_MainTex",JakEffects.Material(texture).GetTexture("_MainTex")); return material;
        }
        internal void Release()
        {
            SampleClock();
            requested=false;
            if(State!=Phase.Active) return;
            if(effect) effect.Release(effectLease); effect=null;
            // Active :exit starts the fade; the inactive update below draws this frame.
            foreach(var bolt in Bolts) bolt.Fade(2,clock.Now);
            stateTick=clock.Now; State=Phase.Inactive;
        }
        private void Awake() { clock.Reset(Time.time); sampledFrame=Time.frameCount; }
        private void SampleClock()
        {
            if(sampledFrame==Time.frameCount) return;
            sampledFrame=Time.frameCount; clock.Advance(Time.deltaTime);
        }
        private void OnEnable() => Camera.onPreCull+=Draw;
        private void LateUpdate()
        {
            SampleClock();
            if(State==Phase.Dead) return;
            if(!body || !body.healthComponent || !body.healthComponent.alive || !muzzle || !muzzle.gameObject.activeInHierarchy ||
                !body.modelLocator || model!=body.modelLocator.modelTransform) { Kill(); return; }
            if(!requested) Release();
            Sound();
            if(State==Phase.Active || Bolts[0].FadeMode!=3)
            {
                Rope.Update(JakArcWorld.ToV(muzzle.position),JakArcWorld.ToV(Aim()),
                    JakArcWorld.ToV(body.transform.position),Time.deltaTime,State==Phase.Active,world,
                    State==Phase.Active && combat.ActionKind==JakWeaponCombat.Kind.Arc ? combat.ActionAge : -1f);
                foreach(var bolt in Bolts) { bolt.Setup(Rope.Path); bolt.Update(clock.Now,clock.Previous); }
                if(Rope.Path.Terminal && clock.Now-lastSpark>=sparkInterval)
                {
                    lastSpark=clock.Now; sparkInterval=5*JakLightningBolt.IntRange(random,3,5);
                    int count=JakLightningBolt.IntRange(random,3,5);
                    // gun-blue-shot.gc:1309-1317: spec 13 replace probes from the terminal spark.
                    for(int i=0;i<count;i++) JakShock.ProbeReplace(gameObject,terminalSpec,168,JakArcWorld.ToVector(Rope.Path.Points[Rope.Path.Count-1]),null,Layer);
                }
                if(State==Phase.Active) CosmeticSweep();
            }
            // gun-blue-shot.gc L940-949: inactive calls update/draw again at the same clock.
            if(State==Phase.Inactive)
            {
                foreach(var bolt in Bolts) bolt.Update(clock.Now,clock.Previous);
                if(clock.Now-stateTick>=2400) Kill();
            }
        }
        // gun-blue-shot.gc L912-939 (inactive) and L1398-1427 (active).
        private void Sound()
        {
            float speed=presentation ? presentation.GunSpinSpeed : 0f, elapsed=(clock.Now-stateTick)/60f;
            bool active=State==Phase.Active;
            float target=active ? 1f-Square(1f-JakArcRope.Clamp(speed/109226.664f)) : JakArcRope.Clamp(speed/218453.33f);
            float step=2f*Time.deltaTime;
            spinIntensity=spinIntensity<target ? Mathf.Min(target,spinIntensity+step) : Mathf.Max(target,spinIntensity-step);
            if(active)
                loop=JakAudio.Play("arc-loop",muzzle.gameObject,volume1024: 768f,pitchMod: elapsed<1f ? 1524f*Mathf.Lerp(-0.5f,0f,elapsed) : 0f);
            else if(elapsed<1f)
                loop=JakAudio.Play("arc-loop",muzzle.gameObject,volume1024: 768f,pitchMod: 1524f*Mathf.Lerp(0f,-0.5f,elapsed));
            else { JakAudio.Stop(loop); loop=null; }
            if(spinIntensity>(active ? 0.1f : 0.001f))
                spin=JakAudio.Play("arc-spin",muzzle.gameObject,volume1024: 1024f*Mathf.Lerp(active ? 0.75f : 0.3f,1f,spinIntensity),
                    pitchMod: 1524f*Mathf.Lerp(-0.5f,0f,spinIntensity));
            else if(!active) { JakAudio.Stop(spin); spin=null; }
        }
        private static float Square(float value) => value*value;
        // Every peer aims at the server's replicated focus; presentation never selects targets.
        private Vector3 Aim()
        {
            world.Focus=combat.ArcFocus;
            world.BeginUpdate();
            return world.Direction(muzzle.position,combat.ArcShot());
        }
        private void CosmeticSweep()
        {
            if(gate.TryPulse(clock.Now/300.0,combat.SampledAttackSpeed)) victims.Clear();
            for(int i=0;i<Rope.Path.SpanCount;i++)
            {
                HurtBox victim=world.Sweep(Rope.Path.Spans[i],victims,out _);
                if(!victim) continue;
                victims.Add(victim.healthComponent.GetInstanceID());
                CosmeticHitCount++;
                // gun-blue-shot.gc:2240-2249: spec 12 from a victim joint, tipped with part 664.
                if(JakLightningBolt.IntRange(random,0,10)<2) JakShock.Probe(gameObject,explodeSpec,664,JakShock.VictimJoint(victim),victim.healthComponent.gameObject,Layer);
            }
        }
        private void Draw(Camera camera)
        {
            if(CaptureHidden || State==Phase.Dead || !camera || meshes==null || (camera.cullingMask&(1<<Layer))==0) return;
            for(int i=0;i<6;i++)
            {
                Bolts[i].Emit(JakArcWorld.ToV(camera.transform.up),strips[i*2],strips[i*2+1],out int a,out int b);
                DrawStrip(camera,i*2,a,Bolts[i].Width,materials[i<4 ? 0 : 1]); DrawStrip(camera,i*2+1,b,Bolts[i].Width,materials[i<4 ? 0 : 1]);
            }
        }
        internal static int[] Triangles(int count)
        {
            var result=new int[Math.Max(0,count-2)*3];
            for(int i=0;i<count-2;i++) { result[i*3]=i; result[i*3+1]=i+(i%2==0 ? 1 : 2); result[i*3+2]=i+(i%2==0 ? 2 : 1); }
            return result;
        }
        // JakLightning's end fade: distance from each strip end in bolt widths. The source strip
        // starts and stops abruptly; from RoR2's shoulder camera those cuts read as a rectangle.
        internal static Vector2 EndFade(JakLightningBolt.Vertex vertex,float length,float width) =>
            new Vector2(vertex.Distance/width,(length-vertex.Distance)/width);
        private void DrawStrip(Camera camera,int index,int count,float width,Material material)
        {
            if(count<3) return;
            Mesh mesh=meshes[index];
            int capacity=strips[index].Length;
            float length=strips[index][count-1].Distance;
            for(int i=0;i<capacity;i++)
            {
                // Unused tail vertices collapse onto the last emitted one, leaving degenerate triangles.
                var v=strips[index][Math.Min(i,count-1)];
                positions[i]=JakArcWorld.ToVector(v.Position); uvs[i]=new Vector2(v.U,v.V); ends[i]=EndFade(v,length,width); colors[i]=new Color32(128,128,128,v.Alpha);
            }
            mesh.SetVertices(positions,0,capacity); mesh.SetUVs(0,uvs,0,capacity); mesh.SetUVs(1,ends,0,capacity); mesh.SetColors(colors,0,capacity); mesh.RecalculateBounds();
            Graphics.DrawMesh(mesh,Matrix4x4.identity,material,Layer,camera,0,null,ShadowCastingMode.Off,false);
        }
        internal void Kill()
        {
            Release(); JakShock.Clear(gameObject);
            JakAudio.Stop(loop); JakAudio.Stop(spin); loop=spin=null; spinIntensity=0f;
            if(meshes!=null) foreach(var mesh in meshes) if(mesh) Destroy(mesh);
            if(materials!=null) foreach(var material in materials) if(material) Destroy(material);
            meshes=null; materials=null; strips=null; Bolts=null; Rope=null; State=Phase.Dead; muzzle=model=null;
        }
        private void OnDisable() { Camera.onPreCull-=Draw; Kill(); }
        private void OnDestroy() { Camera.onPreCull-=Draw; Kill(); }
    }
}
