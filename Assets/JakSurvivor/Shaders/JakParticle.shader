Shader "JakSurvivor/Particle"
{
    Properties
    {
        _MainTex ("Recovered sprite texture", 2D) = "white" {}
        // GS vertex color, 128 = 1.0; a vector so linear players do not convert it.
        _TintColor ("Tint", Vector) = (1,1,1,1)
        _DstBlend ("Destination blend (One = PS2 additive)", Float) = 1
        _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        // The additive addend is formed in the fragment and output with alpha 1.
        Blend SrcAlpha [_DstBlend]
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_local __ JAK_GLOW_PROBE
            #include "UnityCG.cginc"
            Texture2D _MainTex;
            SamplerState sampler_linear_clamp;
            float4 _TintColor;
            float _DstBlend;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float4 probe : TEXCOORD1; float2 depth : TEXCOORD2; };
            struct v2f
            {
                float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR;
                #if JAK_GLOW_PROBE
                float4 screen : TEXCOORD1; float depth : TEXCOORD2;
                #endif
            };
            #if JAK_GLOW_PROBE
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            // GlowRenderer.cpp:5-26: a glow is scaled by the fraction of a screen square around its offset
            // center that passes the depth test, so the shooter's own body hides it like any wall.
            // Sprite3_Glow.cpp:100-122 clamps the half-size to 1..50 lines of the PS2's 416 and keeps the
            // square on screen.
            float GlowVisibility(float4 probe)
            {
                float4 clip = mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(probe.xyz, 1.0)));
                if (clip.w <= 0.0) return 0.0;
                float size = clamp(probe.w * abs(UNITY_MATRIX_P[1][1]) / (2.0 * clip.w), 1.0 / 416.0, 50.0 / 416.0);
                float2 extent = float2(size * _ScreenParams.y / _ScreenParams.x, size);
                float4 screen = ComputeScreenPos(clip);
                float2 center = clamp(screen.xy / screen.w, extent, 1.0 - extent);
                float passed = 0.0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        float2 uv = center + extent * ((float2(x, y) + 0.5) * 0.5 - 1.0);
                        float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0.0, 0.0)));
                        passed += clip.w <= scene ? 1.0 : 0.0;
                    }
                return passed / 16.0;
            }
            #endif
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _TintColor;
                #if JAK_GLOW_PROBE
                o.color.rgb *= GlowVisibility(v.probe);
                o.screen = ComputeScreenPos(o.vertex);
                o.depth = v.depth.x;
                #endif
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                // Texel RGB is bytes / 255 and alpha bytes / 128 (doubled at extraction).
                float4 texel = _MainTex.Sample(sampler_linear_clamp, i.uv);
                #if JAK_GLOW_PROBE
                // Not in the source, where glows ignore depth: RoR2's shoulder camera keeps Jak beside
                // his muzzle, so the halo would light him. It stays behind whatever is nearer than the
                // muzzle, easing in over a quarter metre so the ground has no hard edge.
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
                i.color.rgb *= saturate(1.0 + (scene - i.depth) / 0.25);
                #endif
                // RoR2 renders in linear space and decodes these sRGB textures. The PS2 blended
                // in gamma space, so modulate there and linearize only the result.
                bool linearSpace = unity_ColorSpaceDouble.r > 3.0;
                if (linearSpace) texel.rgb = LinearToGammaSpace(texel.rgb);
                // GS MODULATE clamps each channel to 255; alpha 128 is a blend factor of 1.
                float3 color = saturate(i.color.rgb * texel.rgb);
                float alpha = min(i.color.a * texel.a, 255.0 / 128.0);
                if (_DstBlend == 1.0)
                {
                    float3 addend = color * alpha;
                    return float4(linearSpace ? GammaToLinearSpace(addend) : addend, 1.0);
                }
                // An exact gamma-space blend over a linear framebuffer is impossible; blend linearly.
                return float4(linearSpace ? GammaToLinearSpace(color) : color, saturate(alpha));
            }
            ENDCG
        }
    }
}
