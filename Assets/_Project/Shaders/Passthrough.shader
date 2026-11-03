// The passthrough screen: the camera image on a quad in front of the AR camera. It's the background,
// so it draws first, ignores depth and writes none, and everything virtual draws over it wherever
// it is. The render state is fixed here rather than in material properties, which URP resets from
// the surface type. It has only a forward pass: a depth-only pass would put the quad in URP's depth
// prepass and cut through virtual things behind it.
Shader "CardboardXR/Passthrough"
{
    Properties
    {
        [MainTexture] _BaseMap ("Camera Image", 2D) = "black" {}
        _Fill ("Fill (screen size over image size)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Background"
        }

        Pass
        {
            Name "Passthrough"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Fill;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The screen is _Fill times the image's size: the image in the middle at true scale,
                // and around it the image's edge carried outward, read from ever smaller mips so it
                // blurs, and dimmed, so the periphery has the room's light rather than black.
                float2 p = (input.uv - 0.5) * max(_Fill, 1.0);
                float2 outside2 = max(abs(p) - 0.5, 0.0);
                float outside = length(outside2);
                float2 uv = clamp(p + 0.5, 0.0, 1.0);
                if (outside <= 0.0)
                {
                    // The image's last few percent fade to dark, so it ends softly rather than at a
                    // hard edge (only without the fill, whose blurred surround carries on instead).
                    half3 inside = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                    float2 edge = 0.5 - abs(p);
                    half feather = _Fill > 1.0 ? 1.0 : smoothstep(0.0, 0.05, min(edge.x, edge.y));
                    return half4(inside * feather, 1.0);
                }
                // A matte blur: one read of a small mip banded and swam as the image moved, so this
                // averages eight reads on a ring around the edge point, from a mip that's already
                // soft, widening with distance. Then it's eased off the sharp image over the first
                // few percent, a little desaturated and dimmed, and dithered against banding.
                float t = saturate(outside / 0.9);
                float lod = lerp(3.5, 6.5, t);
                float radius = lerp(0.03, 0.16, t);
                half3 blur = 0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 0.785398 + 0.39;
                    float2 tap = clamp(uv + float2(cos(a), sin(a)) * radius * float2(1.0, 1.6), 0.0, 1.0);
                    blur += SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, tap, lod).rgb;
                }
                blur /= 8.0;
                half3 sharp = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 color = lerp(sharp, blur, smoothstep(0.0, 0.06, outside));
                half luma = dot(color, half3(0.299, 0.587, 0.114));
                color = lerp(color, luma.xxx, 0.3 * t);
                color *= lerp(0.85, 0.3, t);
                float noise = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);
                color += (noise - 0.5) / 255.0;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
