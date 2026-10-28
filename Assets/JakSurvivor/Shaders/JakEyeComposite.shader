Shader "JakSurvivor/EyeComposite"
{
    Properties
    {
        _Iris ("Iris", 2D) = "white" {}
        _Pupil ("Pupil", 2D) = "black" {}
        _Lid ("Lid", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Iris, _Pupil, _Lid;
            float4 _Eye, _LidState;

            float3 sourceColor(float3 color)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return color;
                #else
                    return LinearToGammaSpace(color);
                #endif
            }

            float4 sprite(sampler2D source, float2 pixel, float2 center, float scale)
            {
                float2 begin = trunc((center - scale * 0.5) * 512) / 512;
                float2 end = trunc((center + scale * 0.5) * 512) / 512;
                float2 uv = (pixel - begin) / max(end - begin, 0.00001);
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1) * step(0.00001, scale);
                float4 color = tex2D(source, float2(uv.x, 1 - uv.y));
                color.rgb = sourceColor(color.rgb);
                color.a *= inside;
                return color;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                // eye.gc:121-405: 32px scissor, center 16+32*x/y, half-size 16*scale.
                float2 pixel = float2(input.uv.x, 1 - input.uv.y);
                float2 center = 0.5 + _Eye.xy;
                float3 color = sourceColor(tex2D(_Iris, float2(0, 1)).rgb);
                float4 iris = sprite(_Iris, pixel, center, _Eye.z);
                color = lerp(color, iris.rgb, iris.a);
                float4 pupil = sprite(_Pupil, pixel, center, _Eye.w);
                color = lerp(color, pupil.rgb, pupil.a);
                // eye.gc:328-398: top=-32+32*lid, height=32*lid-scale; right lid mirrors U.
                // GS alpha #x1 is (Cd-Cs)*As+Cs, so lid alpha masks the destination.
                float lidBegin = trunc((-1 + _LidState.x) * 512) / 512;
                float lidEnd = trunc((-1 + _LidState.x + _LidState.y) * 512) / 512;
                float2 lidUV = float2(lerp(pixel.x, 1 - pixel.x, _LidState.z), (pixel.y - lidBegin) / max(lidEnd - lidBegin, 0.00001));
                if (_LidState.y > 0 && lidUV.y >= 0 && lidUV.y <= 1)
                {
                    float4 lid = tex2D(_Lid, float2(lidUV.x, 1 - lidUV.y));
                    color = lerp(sourceColor(lid.rgb), color, lid.a);
                }
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = GammaToLinearSpace(color);
                #endif
                return float4(color, 1);
            }
            ENDCG
        }
    }
}
