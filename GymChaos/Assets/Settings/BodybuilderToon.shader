Shader "GymChaos/BodybuilderToon"
{
    // The anime-drawn remake (Goku): the same flat, unlit body as
    // GymChaos/BodybuilderUnlit (cel shadow and ink are baked into the
    // texture) plus a dark ink outline drawn as an inverted hull.
    Properties
    {
        _BaseMap ("Body Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0.10,0.06,0.08,1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.006
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ToonBody"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Push the hull out along the world normal by a fixed width.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.positionHCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
