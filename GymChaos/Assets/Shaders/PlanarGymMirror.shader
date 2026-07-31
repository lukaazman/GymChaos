Shader "GymChaos/PlanarMirror"
{
    Properties
    {
        _ReflectionTex ("Reflection", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.82, 0.9, 0.94, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 reflectionCS : TEXCOORD2;
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);
            float4 _Tint;
            float _FresnelStrength;
            float4x4 _MirrorVP;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.reflectionCS = mul(_MirrorVP, float4(positionInputs.positionWS, 1.0));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.reflectionCS.xy / max(0.0001, input.reflectionCS.w);
                uv = uv * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
                half3 reflected = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, saturate(uv)).rgb;
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                half fresnel = pow(1.0h - saturate(dot(viewDirection, normalize(input.normalWS))), 4.0h);
                reflected *= _Tint.rgb;
                reflected += fresnel * _FresnelStrength;
                return half4(reflected, 1.0h);
            }
            ENDHLSL
        }
    }
}
