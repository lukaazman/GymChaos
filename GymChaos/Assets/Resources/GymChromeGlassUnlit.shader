// Cheap unlit chrome/glass for the distant city backdrop. One shader drives
// three roles; the loader assigns one material per role:
// - body (facades, frames, metal, roofs): one dark, non-reflective colour;
// - window glass (small window panes): reflective glass;
// - curtain walls (the large glass faces): a world-space window grid, so
//   only the panes reflect and the mullions/spandrels between them keep the
//   dark body colour. Some panes glow warm at night.
// Reflection is an analytic sky (same gradient family as GymGradientSky)
// through a Fresnel term. No lights, shadows, probes or cubemap lookups.
Shader "GymChaos/Unlit Chrome Glass"
{
    Properties
    {
        _BaseMap ("Facade Pattern", 2D) = "white" {}
        _BaseColor ("Steel Tint", Color) = (0.15, 0.2, 0.27, 1)
        _BodyBrightness ("Body Brightness", Range(0, 2)) = 1
        _PatternContrast ("Pattern Contrast", Range(0, 3)) = 1.9
        _Reflectivity ("Reflectivity", Range(0, 1)) = 0.9
        _SunGlint ("Sun Glint", Range(0, 2)) = 0.7
        _WindowGrid ("Window Grid (0 off, 1 on)", Range(0, 1)) = 0
        _WindowCell ("Window Cell (width, floor height) m", Vector) = (2.1, 3.4, 0, 0)
        _WindowFill ("Pane Fill (width, height)", Vector) = (0.72, 0.62, 0, 0)
        _NightLitFraction ("Night Lit Pane Fraction", Range(0, 1)) = 0.22
        _FogStrength ("Fog Strength", Range(0, 1)) = 0.55
        _TowerHeight ("Tower Height", Float) = 70
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        _DayHorizon ("Day Horizon", Color) = (0.42, 0.68, 0.95, 1)
        _DayZenith ("Day Zenith", Color) = (0.07, 0.24, 0.62, 1)
        _NightHorizon ("Night Horizon", Color) = (0.05, 0.11, 0.24, 1)
        _NightZenith ("Night Zenith", Color) = (0.004, 0.01, 0.035, 1)
        _GroundReflection ("Ground Reflection", Color) = (0.03, 0.045, 0.065, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Name "UnlitChromeGlass"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BodyBrightness;
                half _PatternContrast;
                half _Reflectivity;
                half _FresnelPower;
                half _SunGlint;
                half _WindowGrid;
                float4 _WindowCell;
                float4 _WindowFill;
                half _NightLitFraction;
                half _FogStrength;
                float _TowerHeight;
                half4 _DayHorizon;
                half4 _DayZenith;
                half4 _NightHorizon;
                half4 _NightZenith;
                half4 _GroundReflection;
            CBUFFER_END

            // Set globally by GymTimeOfDay (0 = night, 1 = day).
            half _GymChromeDaylight;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Anti-aliased 1D pane mask: 1 inside [0, fill) of each cell.
            // Once a cell spans only a few pixels it fades to the average
            // coverage, so distant towers do not shimmer.
            float PaneMask(float coordinate, float fill)
            {
                float cell = frac(coordinate);
                float width = max(fwidth(coordinate), 1e-4);
                float start = smoothstep(0.0, width, cell);
                float end = 1.0 - smoothstep(fill - width, fill, cell);
                return lerp(start * end, fill, saturate((width - 0.12) / 0.25));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half luma = dot(texel, half3(0.299h, 0.587h, 0.114h));
                half pattern = saturate(0.5h + (luma - 0.5h) * _PatternContrast);
                half3 body = _BaseColor.rgb * (0.35h + pattern * 0.65h) * _BodyBrightness;

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 reflected = reflect(-viewWS, normalWS);

                half daylight = saturate(_GymChromeDaylight);
                half3 horizon = lerp(_NightHorizon.rgb, _DayHorizon.rgb, daylight);
                half3 zenith = lerp(_NightZenith.rgb, _DayZenith.rgb, daylight);
                half up = saturate(reflected.y);
                half3 sky = lerp(horizon, zenith, sqrt(up));
                half3 ground = _GroundReflection.rgb * (0.4h + 0.6h * daylight);
                half3 reflection = lerp(ground, sky, smoothstep(-0.08h, 0.06h, reflected.y));
                // Fake reflected skyline: a dark band just above the horizon
                // whose height varies with reflection azimuth, so towers
                // facing different ways mirror different neighbours.
                half azimuth = atan2(reflected.z, reflected.x);
                float skylineCell = floor(azimuth * 7.64);
                half skyline = 0.05h + 0.24h * frac(sin(skylineCell * 12.9898) * 43758.5453);
                half mirrored = 1.0h - smoothstep(skyline - 0.008h, skyline + 0.008h, reflected.y);
                mirrored *= smoothstep(-0.12h, 0.0h, reflected.y);
                reflection = lerp(reflection, _BaseColor.rgb * (0.35h + 0.35h * daylight), mirrored * 0.75h);

                // Pane coverage: the whole surface for plain glass, only the
                // panes of a world-space grid on curtain walls.
                half pane = 1.0h;
                half litPane = 0.0h;
                if (_WindowGrid > 0.5h)
                {
                    float3 across = cross(float3(0.0, 1.0, 0.0), normalWS);
                    float acrossLength = length(across);
                    across = acrossLength > 1e-3 ? across / acrossLength : float3(1.0, 0.0, 0.0);
                    float2 facade = float2(
                        dot(input.positionWS, across) / max(_WindowCell.x, 0.1),
                        input.positionWS.y / max(_WindowCell.y, 0.1));
                    half wall = 1.0h - smoothstep(0.55h, 0.8h, abs(normalWS.y));
                    pane = PaneMask(facade.x, _WindowFill.x) *
                        PaneMask(facade.y, _WindowFill.y) * wall;
                    litPane = step(1.0 - _NightLitFraction, Hash21(floor(facade)));
                }

                half facing = saturate(dot(normalWS, viewWS));
                half fresnel = 0.22h + 0.78h * pow(1.0h - facing, _FresnelPower);
                half mixAmount = saturate(_Reflectivity * fresnel) * pane;
                half3 bodyLit = body * (0.45h + 0.55h * daylight);
                // Glass shows the reflection over a dark interior.
                half3 interior = _BaseColor.rgb * 0.25h;
                half3 glass = lerp(interior, reflection, saturate(_Reflectivity * (0.35h + 0.65h * fresnel)));
                half3 color = lerp(bodyLit, glass, pane * step(0.01h, _Reflectivity));
                pane *= step(0.01h, _Reflectivity);
                color = lerp(color, reflection, mixAmount * 0.15h);
                // Darker street level, brighter crowns: reads as tall glass.
                half heightLight = saturate(input.positionWS.y / max(_TowerHeight, 1.0));
                color *= 0.72h + 0.28h * heightLight;
                // Main-light glint on the glass only (chrome cue).
                half glint = pow(saturate(dot(reflected, _MainLightPosition.xyz)), 48.0h);
                color += _MainLightColor.rgb * glint * _SunGlint * daylight * pane;
                // Occupied offices glow behind some panes after dark.
                color += half3(1.0h, 0.72h, 0.38h) * 0.55h * litPane * pane * (1.0h - daylight);

                // Partial fog keeps the glass contrast readable at distance.
                color = lerp(color, MixFog(color, input.fogFactor), _FogStrength);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
