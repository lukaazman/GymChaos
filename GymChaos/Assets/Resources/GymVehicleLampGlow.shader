Shader "GymChaos/GymVehicleLampGlow"
{
    // Car lamp glow: added on top of the body's own lamp surface instead of
    // painting over it, strongest straight on and softer at grazing angles so
    // the lens reads as light, not as a coat of colour.
    Properties
    {
        _MainTex ("Lamp Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 0.9, 0.72, 1)
        _Intensity ("Intensity", Float) = 1
        _UseTexture ("Texture (0 none, 1 tint, 2 detail)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+5"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GymVehicleLampGlow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float edge : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Intensity;
                float _UseTexture;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                // Vertex alpha: 0 on the rim of the lamp patch, 1 inside.
                output.edge = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 lamp = _Color.rgb;
                if (_UseTexture > 0.5)
                {
                    half3 lens = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                    // Tint: the lens colour itself; detail: the lens pattern
                    // (reflector, ribs) shows through the lamp colour.
                    lamp *= _UseTexture < 1.5 ? lens : (0.35 + 0.65 * dot(lens, half3(0.3, 0.59, 0.11)) * 2.0);
                }
                float facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                // Hot core straight on, falling off toward the lens rim.
                float glow = (0.3 + 0.7 * facing * facing) * smoothstep(0.0, 1.0, input.edge);
                return half4(lamp * (_Intensity * glow), 1);
            }
            ENDHLSL
        }
    }
}
