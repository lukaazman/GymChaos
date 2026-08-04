Shader "GymChaos/GokuAura"
{
    Properties
    {
        _AuraColor ("Aura Color", Color) = (1, 0.58, 0.035, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.45
        _Expansion ("Expansion", Range(0, 0.5)) = 0.15
        _AuraBlend ("Aura Blend", Range(0, 1)) = 0
        _PulseSpeed ("Pulse Speed", Float) = 3
        _FresnelPower ("Fresnel Power", Range(0.5, 5)) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "GokuAura"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

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
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float pulse : TEXCOORD2;
            };

            half4 _AuraColor;
            float _Opacity;
            float _Expansion;
            float _AuraBlend;
            float _PulseSpeed;
            float _FresnelPower;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normal = normalize(input.normalOS);
                float waveA = sin(dot(input.positionOS.xyz, float3(5.1, 3.7, 4.3)) + _Time.y * _PulseSpeed);
                float waveB = sin(dot(input.positionOS.xyz, float3(-2.9, 6.2, 3.1)) - _Time.y * (_PulseSpeed * 0.73));
                float flutter = 0.5 + 0.5 * (waveA * 0.62 + waveB * 0.38);
                float expansion = _Expansion * (0.72 + flutter * 0.56) * _AuraBlend;
                float3 positionOS = input.positionOS.xyz + normal * expansion;
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(normal);
                output.pulse = flutter;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float rim = pow(1.0 - abs(dot(normalize(input.normalWS), viewDirection)), _FresnelPower);
                float shimmer = 0.74 + input.pulse * 0.32;
                float alpha = saturate(_Opacity * _AuraBlend * (0.32 + rim * 1.05) * shimmer);
                half3 color = _AuraColor.rgb * (1.02h + rim * 1.45h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
