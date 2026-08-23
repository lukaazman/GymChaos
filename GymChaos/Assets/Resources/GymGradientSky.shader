Shader "GymChaos/GymGradientSky"
{
    Properties
    {
        _DayHorizon ("Day Horizon", Color) = (0.34, 0.66, 1, 1)
        _DayZenith ("Day Zenith", Color) = (0.045, 0.22, 0.68, 1)
        _NightHorizon ("Night Horizon", Color) = (0.025, 0.08, 0.22, 1)
        _NightZenith ("Night Zenith", Color) = (0.001, 0.004, 0.025, 1)
        _CloudLight ("Cloud Light", Color) = (0.94, 0.98, 1, 1)
        _CloudShadow ("Cloud Shadow", Color) = (0.32, 0.48, 0.68, 1)
        _Daylight ("Daylight", Range(0, 1)) = 1
        _CloudFade ("Cloud Fade", Range(0, 1)) = 1
        _StarFade ("Star Fade", Range(0, 1)) = 0
        _CloudOffset ("Cloud Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        // Draw only into pixels that are still at the far depth. Opaque gym
        // walls, equipment and characters therefore remain in front while
        // transparent window panes still reveal the exterior.
        ZTest LEqual

        Pass
        {
            Name "GymGradientSky"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DayHorizon;
                float4 _DayZenith;
                float4 _NightHorizon;
                float4 _NightZenith;
                float4 _CloudLight;
                float4 _CloudShadow;
                float _Daylight;
                float _CloudFade;
                float _StarFade;
                float _CloudOffset;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                // URP uses a reversed depth buffer on D3D/desktop builds. A
                // sky vertex forced to z=w is then placed at the near plane
                // and draws over the gym, making every wall and prop look
                // transparent. Put the sky at the actual far plane for both
                // depth conventions so scene geometry occludes it normally.
                #if UNITY_REVERSED_Z
                    output.positionHCS.z = 0.0;
                #else
                    output.positionHCS.z = output.positionHCS.w;
                #endif
                output.directionOS = input.positionOS;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float2 Hash22(float2 value)
            {
                float first = Hash21(value);
                float second = Hash21(value + 17.13);
                return frac(float2(first, second));
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);

                float bottomLeft = Hash21(cell);
                float bottomRight = Hash21(cell + float2(1, 0));
                float topLeft = Hash21(cell + float2(0, 1));
                float topRight = Hash21(cell + float2(1, 1));
                float bottom = lerp(bottomLeft, bottomRight, local.x);
                float top = lerp(topLeft, topRight, local.x);
                return lerp(bottom, top, local.y);
            }

            float FractalNoise(float2 value)
            {
                float result = 0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 4; octave++)
                {
                    result += ValueNoise(value) * amplitude;
                    value = value * 2.03 + float2(13.7, 7.1);
                    amplitude *= 0.5;
                }
                return result;
            }

            float StarLayer(
                float2 uv, float scale, float threshold,
                float minimumRadius, float maximumRadius, float seedOffset)
            {
                float2 grid = uv * scale;
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float2 starCenter = Hash22(cell + seedOffset) - 0.5;
                float starSeed = Hash21(cell + seedOffset * 2.31);
                float enabled = smoothstep(threshold, threshold + 0.035, starSeed);
                float radius = lerp(
                    minimumRadius, maximumRadius,
                    Hash21(cell + seedOffset * 3.73));
                float distanceSquared = dot(local - starCenter * 0.72, local - starCenter * 0.72);
                float core = exp(-distanceSquared / max(0.00001, radius * radius));
                float halo = exp(-distanceSquared / max(0.00001, radius * radius * 12.0)) * 0.24;
                float twinkle = 0.84 + 0.16 * sin(
                    _Time.y * (1.2 + starSeed * 2.2) + starSeed * 6.28318);
                return (core + halo) * enabled * twinkle;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float vertical = saturate((direction.y + 0.035) / 1.035);
                float gradient = pow(vertical, 0.68);

                float3 daySky = lerp(_DayHorizon.rgb, _DayZenith.rgb, gradient);
                float3 nightSky = lerp(_NightHorizon.rgb, _NightZenith.rgb, gradient);
                float3 color = lerp(nightSky, daySky, saturate(_Daylight));

                // A subtle horizon haze keeps the low-poly gradient from
                // looking like a flat painted strip when seen through the
                // distant gym windows.
                float horizonHaze = pow(
                    saturate(1.0 - abs(direction.y) * 2.8), 2.2);
                float3 hazeColor = lerp(
                    _NightHorizon.rgb, _DayHorizon.rgb, saturate(_Daylight));
                color = lerp(color, hazeColor, horizonHaze * 0.11);

                // Clouds occupy an elevated band only. The projection makes
                // them read as distant layered atmosphere instead of solid
                // blocks next to the gym floor or window sill.
                float cloudBand = smoothstep(0.18, 0.32, direction.y) *
                    (1.0 - smoothstep(0.58, 0.72, direction.y));
                float2 cloudUV = direction.xz / max(0.28, direction.y + 0.62);
                cloudUV = cloudUV * float2(1.18, 0.72) +
                    float2(_CloudOffset * 0.00055, _CloudOffset * 0.00017);
                float broadCloud = FractalNoise(cloudUV * 2.25);
                float detailedCloud = FractalNoise(cloudUV * 5.1 + 19.7);
                float cloudShape = broadCloud * 0.78 + detailedCloud * 0.22;
                float cloudMask = smoothstep(0.49, 0.69, cloudShape) *
                    cloudBand * saturate(_CloudFade);
                float cloudLight = saturate(
                    0.32 + direction.y * 0.76 + detailedCloud * 0.26);
                float3 cloudColor = lerp(
                    _CloudShadow.rgb, _CloudLight.rgb, cloudLight);
                color = lerp(color, cloudColor, cloudMask * 0.72);

                // A second, thinner high-altitude layer breaks up the single
                // cloud band and adds distant cirrus structure without making
                // the horizon look busy or turning the clouds into geometry
                // blocks.
                float cirrusBand = smoothstep(0.48, 0.61, direction.y) *
                    (1.0 - smoothstep(0.76, 0.88, direction.y));
                float2 cirrusUV = direction.xz / max(0.42, direction.y + 0.86);
                cirrusUV = cirrusUV * float2(0.82, 0.46) +
                    float2(_CloudOffset * 0.00031, -_CloudOffset * 0.00012);
                float cirrusNoise = FractalNoise(cirrusUV * 1.35 + 41.2);
                float cirrusMask = smoothstep(0.53, 0.67, cirrusNoise) *
                    cirrusBand * saturate(_CloudFade);
                color = lerp(color, _CloudLight.rgb, cirrusMask * 0.22);

                // Radial star points replace billboard particles. The two
                // scales produce small distant lights and a sparse set of
                // larger glowing stars; exponential falloff keeps every point
                // round and soft instead of square.
                // Keep the horizon subdued, but distribute stars through the
                // full elevated night sky instead of making them read like
                // lights sitting on the gym's distant floor line.
                float starBand = smoothstep(0.06, 0.22, direction.y) *
                    (1.0 - smoothstep(0.80, 0.94, direction.y));
                float2 starUV = direction.xz / max(0.34, direction.y + 0.68);
                float smallStars = StarLayer(
                    starUV, 54.0, 0.91, 0.010, 0.027, 4.7);
                float largerStars = StarLayer(
                    starUV + float2(8.1, -3.4), 25.0, 0.955, 0.018, 0.052, 31.2);
                float stars = (smallStars * 0.72 + largerStars * 1.18) *
                    starBand * saturate(_StarFade);
                color += stars * float3(0.92, 0.97, 1.0);

                return half4(saturate(color), 1.0h);
            }
            ENDHLSL
        }
    }
}
