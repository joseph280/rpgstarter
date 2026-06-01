Shader "Celestia/Character/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap         ("Base Map (Albedo)", 2D)  = "white" {}
        [MainColor]   _BaseColor       ("Base Color",        Color) = (1,1,1,1)

        _ShadowBands       ("Shadow Bands (2 or 3)", Range(2, 3)) = 3
        _ShadowSoftness    ("Band Softness",         Range(0.001, 0.05)) = 0.005
        _AmbientStrength   ("Ambient Strength",      Range(0, 1)) = 0.35
        _RimColor          ("Rim Color",             Color) = (1,1,1,1)
        _RimPower          ("Rim Power",             Range(1, 16)) = 4
        _RimStrength       ("Rim Strength",          Range(0, 1)) = 0.0

        [Normal] _BumpMap  ("Normal Map", 2D) = "bump" {}
        _BumpScale         ("Normal Scale", Range(0, 2)) = 1.0

        _EmissionMap       ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _ShadowBands;
                float  _ShadowSoftness;
                float  _AmbientStrength;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float  _BumpScale;
                float4 _EmissionColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vn = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = vp.positionCS;
                OUT.positionWS  = vp.positionWS;
                OUT.normalWS    = vn.normalWS;
                OUT.tangentWS   = vn.tangentWS;
                OUT.bitangentWS = vn.bitangentWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(vp);
                return OUT;
            }

            // Quantize a continuous lit value [0,1] into N hard bands with a soft seam.
            float ToonBand(float x, float bands, float softness)
            {
                float scaled = x * bands;
                float floorScaled = floor(scaled);
                float frac = scaled - floorScaled;
                float band = floorScaled + smoothstep(0.5 - softness, 0.5 + softness, frac);
                return band / bands;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // Albedo
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // Normal mapping
                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float3x3 tbn = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 normalWS = normalize(mul(normalTS, tbn));

                // Main light + shadows
                Light mainLight = GetMainLight(IN.shadowCoord);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float lit = NdotL * mainLight.shadowAttenuation;

                // Toon banding
                float band = ToonBand(lit, _ShadowBands, _ShadowSoftness);
                float3 mainContribution = albedo.rgb * mainLight.color * band;

                // Ambient (flat fill)
                float3 ambient = albedo.rgb * _AmbientStrength;

                // Rim light (optional, off by default)
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(1.0 - saturate(dot(viewDir, normalWS)), _RimPower) * _RimStrength;
                float3 rimContribution = _RimColor.rgb * rim;

                // Emission
                float3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;

                float3 color = ambient + mainContribution + rimContribution + emission;
                return float4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct AttribS { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct VaryS   { float4 positionHCS : SV_POSITION; };

            VaryS ShadowVert(AttribS IN)
            {
                VaryS OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(VaryS IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
