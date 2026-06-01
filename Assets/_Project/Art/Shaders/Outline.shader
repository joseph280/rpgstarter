Shader "RPGStarter/PostProcess/Outline"
{
    Properties
    {
        _OutlineColor      ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineThickness  ("Outline Thickness (px)", Range(0.5, 4)) = 1.0
        _DepthThreshold    ("Depth Edge Threshold", Range(0.0001, 0.01)) = 0.001
        _NormalThreshold   ("Normal Edge Threshold", Range(0.05, 1.0)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

        TEXTURE2D_X(_BlitTexture);
        SAMPLER(sampler_BlitTexture);
        float4 _BlitTexture_TexelSize;

        float4 _OutlineColor;
        float  _OutlineThickness;
        float  _DepthThreshold;
        float  _NormalThreshold;

        struct Attributes { uint vertexID : SV_VertexID; };
        struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionHCS = GetFullScreenTriangleVertexPosition(v.vertexID);
            o.uv          = GetFullScreenTriangleTexCoord(v.vertexID);
            return o;
        }

        // Sample depth + normal at a UV; reusing URP's helper textures.
        void SampleSceneAt(float2 uv, out float depth, out float3 normal)
        {
            depth  = SampleSceneDepth(uv);
            normal = SampleSceneNormals(uv);
        }

        // 3x3 Sobel-like cross sample: compares depth/normal across 4 cardinal neighbors.
        float4 Frag(Varyings i) : SV_Target
        {
            float4 sceneCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, i.uv);

            float2 texel = _BlitTexture_TexelSize.xy * _OutlineThickness;

            float  dC; float3 nC; SampleSceneAt(i.uv,                              dC, nC);
            float  dL; float3 nL; SampleSceneAt(i.uv + float2(-texel.x, 0),        dL, nL);
            float  dR; float3 nR; SampleSceneAt(i.uv + float2( texel.x, 0),        dR, nR);
            float  dU; float3 nU; SampleSceneAt(i.uv + float2(0,  texel.y),        dU, nU);
            float  dD; float3 nD; SampleSceneAt(i.uv + float2(0, -texel.y),        dD, nD);

            // Depth discontinuity (sky returns 0 in linear depth on URP — clamp to ignore).
            float depthEdge = abs(dL - dR) + abs(dU - dD);
            float depthMask = step(_DepthThreshold, depthEdge) * step(0.0001, dC);

            // Normal discontinuity (1 - dot is small on flat surfaces, large at silhouettes).
            float normalEdge = (1 - dot(nL, nR)) + (1 - dot(nU, nD));
            float normalMask = step(_NormalThreshold, normalEdge);

            float edge = saturate(max(depthMask, normalMask));
            return lerp(sceneCol, _OutlineColor, edge);
        }
        ENDHLSL

        Pass
        {
            Name "RPGStarterOutline"
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
