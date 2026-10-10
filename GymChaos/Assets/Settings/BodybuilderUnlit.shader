Shader "GymChaos/BodybuilderUnlit"
{
    Properties
    {
        _BaseMap ("Body Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _ScanMaxLod ("Max Mip Level", Float) = 0
        _UseVertexColor ("Multiply Vertex Color", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "BodybuilderUnlit"
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 color : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            half4 _BaseColor;
            float4 _BaseMap_TexelSize;
            float _ScanMaxLod;
            float _UseVertexColor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                // Jolly Dog's scan multiplies its atlas by sRGB vertex
                // colours. Other characters keep the flag at 0, so whatever
                // their meshes carry in COLOR never changes their look.
                output.color = lerp(
                    half3(1.0h, 1.0h, 1.0h),
                    SRGBToLinear(input.color.rgb),
                    (half)saturate(_UseVertexColor));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Scan atlases pack thousands of tiny UV islands; lower mip levels
                // mix neighbouring islands into specks (skin dots on
                // shirts). Clamp the sampled level, also where the C# side
                // cannot shorten the mip chain (no CopyTexture support).
                float2 texel = input.uv * _BaseMap_TexelSize.zw;
                float2 dx = ddx(texel);
                float2 dy = ddy(texel);
                float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
                lod = clamp(lod, 0.0, _ScanMaxLod);
                half3 color = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, lod).rgb * _BaseColor.rgb * input.color;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
