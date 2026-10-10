Shader "GymChaos/GymFixerGlow"
{
    // Additive magic light for Jolly Dog's lollipop. Mode 0 lights a mesh
    // shell with a soft rim, mode 1 draws round soft sprites (halo quad and
    // sparkle particles, which pass their colour through the vertex stream).
    Properties
    {
        _Color ("Color", Color) = (1, 0.82, 0.45, 1)
        _Intensity ("Intensity", Float) = 0
        _Mode ("Mode (0 shell, 1 sprite)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GymFixerGlow"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewWS : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Mode;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 tint = _Color.rgb * max(0.0, _Intensity);
                if (_Mode > 0.5)
                {
                    float2 centered = input.uv * 2.0 - 1.0;
                    float radial = saturate(1.0 - dot(centered, centered));
                    float core = radial * radial * radial;
                    half3 sprite = tint * input.color.rgb * input.color.a *
                        (core * 1.4 + radial * 0.25);
                    return half4(sprite, 1.0h);
                }

                float3 normal = normalize(input.normalWS);
                float3 view = normalize(input.viewWS);
                float facing = abs(dot(normal, view));
                float rim = pow(1.0 - facing, 1.6);
                // A gentle shimmer keeps the candy alive while it glows.
                float shimmer = 0.85 + 0.15 * sin(_Time.y * 9.0 + input.uv.x * 30.0);
                half3 shell = tint * (0.32 + rim * 1.25) * shimmer;
                return half4(shell, 1.0h);
            }
            ENDHLSL
        }
    }
}
