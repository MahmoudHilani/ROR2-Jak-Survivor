Shader "JakSurvivor/Lightning"
{
    Properties
    {
        _MainTex ("Recovered bolt texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        // SrcAlpha One is applied in the fragment so it can happen in PS2 gamma space.
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            // gs-clamp zero (lightning-new.gc L38-39) repeats both axes; the shared recovered
            // textures are clamped and not CPU-readable, so wrap with an inline sampler.
            Texture2D _MainTex;
            SamplerState sampler_linear_repeat;
            // ends: distance from the strip's start and end, in fade lengths.
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 ends : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float2 ends : TEXCOORD1; float4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.ends = v.ends;
                o.color = v.color;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                // GS channels are bytes / 255; recovered PNG alpha was doubled at extraction.
                float4 texel = _MainTex.Sample(sampler_linear_repeat, i.uv);
                // RoR2 renders in linear space and decodes these sRGB textures. The PS2 blended
                // in gamma space, so form its addend there and linearize only the result.
                bool linearSpace = unity_ColorSpaceDouble.r > 3.0;
                if (linearSpace) texel.rgb = LinearToGammaSpace(texel.rgb);
                float4 color = saturate(2.0 * i.color * texel);
                // The recovered texture retains edge alpha; fade it at the ribbon boundary so the strip is not visible as a rectangle.
                color.a *= smoothstep(0.0, 0.2, i.uv.x) * smoothstep(0.0, 0.2, 1.0 - i.uv.x);
                // Fade the cut ends too, or a wide strip seen end-on is a hard-edged slab.
                color.a *= smoothstep(0.0, 1.0, i.ends.x) * smoothstep(0.0, 1.0, i.ends.y);
                float3 addend = color.rgb * color.a;
                return float4(linearSpace ? GammaToLinearSpace(addend) : addend, 1.0);
            }
            ENDCG
        }
    }
}
