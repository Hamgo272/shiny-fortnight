// The Moon world's surfaces: regolith, rocks, the Earth and the suit glove. Lit by the main light
// only, which in the Moon world is its own low sun (MoonWorld makes it RenderSettings.sun), with its
// real-time shadows, and a faint flat "earthshine" for the shadowed side; there's no sky light, so
// shadows stay near black, which is most of what makes it look like the Moon. Core's room light,
// which follows the passthrough estimate, is an additional light here and is ignored.
//   Regolith doesn't light like a matte (Lambert) surface: it scatters light back toward the sun
//   and doesn't darken toward the edges of a slope. _Lunar blends Lambert toward Lommel-Seeliger
//   (2 mu0 / (mu0 + mu)), with a little brightening when looking straight down-sun (the opposition
//   surge). A scanned regolith texture at _BaseMap's tiling gives the grain up close and a second,
//   larger one (_MacroMap) breaks the repeat further out; vertex color carries the terrain's own
//   albedo (crater rays, packed soil). _Specular gives the glove's metal ring a highlight.
Shader "CardboardXR/Moon"
{
    Properties
    {
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _BaseMap("Albedo", 2D) = "white" {}
        [Normal] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        _MacroMap("Macro Albedo", 2D) = "white" {}
        [Normal] _MacroBump("Macro Normal", 2D) = "bump" {}
        _MacroScale("Macro Tiling (of Albedo's)", Float) = 0.125
        _MacroStrength("Macro Strength", Range(0, 1)) = 0
        _MacroMean("Macro Mean Albedo", Float) = 0.4
        _VertexColor("Use Vertex Color", Range(0, 1)) = 0
        _Lunar("Lunar Scattering", Range(0, 1)) = 0
        _Ambient("Earthshine", Color) = (0.012, 0.014, 0.02, 1)
        _Specular("Specular", Range(0, 1)) = 0
        _Gloss("Gloss", Range(1, 256)) = 32
        _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _MacroMap_ST;
            float _BumpScale;
            float _MacroScale;
            float _MacroStrength;
            float _MacroMean;
            float _VertexColor;
            float _Lunar;
            float4 _Ambient;
            float _Specular;
            float _Gloss;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "MoonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MacroMap); SAMPLER(sampler_MacroMap);
            TEXTURE2D(_MacroBump); SAMPLER(sampler_MacroBump);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float4 color : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = float4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input, half facing : VFACE) : SV_Target
            {
                float2 uv = TRANSFORM_TEX(input.uv, _BaseMap);
                float2 macroUv = uv * _MacroScale;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                if (_MacroStrength > 0)
                {
                    half3 macro = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, macroUv).rgb;
                    // The macro layer tints around its own average, so it varies the grain's brightness
                    // without changing the overall tone.
                    albedo *= lerp(1, macro / max(_MacroMean, 0.01), _MacroStrength);
                    half3 macroN = UnpackNormalScale(SAMPLE_TEXTURE2D(_MacroBump, sampler_MacroBump, macroUv), _MacroStrength);
                    normalTS = normalize(half3(normalTS.xy + macroN.xy, normalTS.z * macroN.z));
                }
                albedo *= _BaseColor.rgb * lerp(1, input.color.rgb * 1.6, _VertexColor);

                float3 geomN = normalize(input.normalWS) * (facing > 0 ? 1 : -1);
                float3 tangent = normalize(input.tangentWS.xyz);
                float3 bitangent = cross(geomN, tangent) * input.tangentWS.w;
                float3 n = normalize(mul(normalTS, float3x3(tangent, bitangent, geomN)));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float mu0 = saturate(dot(n, light.direction));
                float mu = saturate(dot(geomN, v));
                float lommel = 2 * mu0 / max(mu0 + mu, 1e-3);
                float phase = acos(saturate(dot(light.direction, v)));
                float surge = 1 + 0.35 * _Lunar * exp(-phase / 0.12);
                float diffuse = lerp(mu0, lommel, _Lunar) * surge;
                float3 h = normalize(light.direction + v);
                float spec = _Specular * pow(saturate(dot(n, h)), _Gloss) * (mu0 > 0 ? 1 : 0);

                float3 lit = light.color * light.shadowAttenuation * light.distanceAttenuation;
                float3 color = albedo * (lit * diffuse + _Ambient.rgb) + lit * spec;
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
