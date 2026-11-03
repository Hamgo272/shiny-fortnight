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
                    return half4(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb, 1.0);
                float lod = min(outside * 14.0, 7.0);
                half3 color = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, lod).rgb;
                half dim = lerp(0.9, 0.25, saturate(outside / 0.8));
                return half4(color * dim, 1.0);
            }
            ENDHLSL
        }
    }
}
