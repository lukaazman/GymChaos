// Super Saiyan flame aura sheet (see GokuAura.cs). The quad is placed in
// world space from uniforms: _Center + _AxisU * p.x + _AxisV * p.y, with p
// in metres inside _Rect. Flames stream along +V.
Shader "GymChaos/GokuAura"
{
    Properties
    {
        _AuraBlend ("Aura Blend", Range(0, 1)) = 0
        _Front ("Front Tint Sheet", Float) = 0
        _OuterColor ("Outer Color", Color) = (1, 0.80, 0.12, 1)
        _MidColor ("Mid Color", Color) = (1, 0.93, 0.42, 1)
        _CoreColor ("Core Color", Color) = (1, 1, 0.88, 1)
        // Placement, set per camera by GokuAura.cs.
        [HideInInspector] _Center ("Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AxisU ("Axis U", Vector) = (1, 0, 0, 0)
        [HideInInspector] _AxisV ("Axis V", Vector) = (0, 1, 0, 0)
        [HideInInspector] _Rect ("Rect", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Segment ("Segment", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Shape ("Shape", Vector) = (0.36, 0.24, 1.15, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GokuAura"
            // Premultiplied: the soft halo adds light without covering.
            Blend One OneMinusSrcAlpha
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 p : TEXCOORD0;
            };

            float _AuraBlend;
            float _Front;
            half4 _OuterColor;
            half4 _MidColor;
            half4 _CoreColor;
            float4 _Center;
            float4 _AxisU;
            float4 _AxisV;
            float4 _Rect;
            float4 _Segment;
            // x body radius, y base thickness, z flame length, w radial (0..1)
            float4 _Shape;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 p = lerp(_Rect.xy, _Rect.zw, input.uv);
                float3 positionWS = _Center.xyz + _AxisU.xyz * p.x + _AxisV.xyz * p.y;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.p = p;
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // Sharp crests where the noise crosses 0.5: pointed flame tips.
            float Ridge(float n)
            {
                float r = 1.0 - abs(n * 2.0 - 1.0);
                return r * r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 p = input.p;
                float radius = _Shape.x;
                float baseThickness = _Shape.y;
                float flameLength = _Shape.z;
                float radial = _Shape.w;

                // Distance to the head-feet capsule and the outward direction.
                float2 a = _Segment.xy;
                float2 b = _Segment.zw;
                float2 ba = b - a;
                float h = saturate(dot(p - a, ba) / max(dot(ba, ba), 1e-5));
                float2 nearest = a + ba * h;
                float2 offset = p - nearest;
                float centreDistance = length(offset);
                float2 dir = centreDistance > 1e-4 ? offset / centreDistance : float2(0, 1);
                float d = centreDistance - radius;

                // Surface point the flame grows from. Shifting it back along
                // the flame axis with distance bends the side tongues so they
                // lick towards +V like drawn flames.
                float2 surface = nearest + dir * radius;
                surface.y -= max(d, 0.0) * 0.75 * (1.0 - radial);
                float travel = t * 3.2 + max(d, 0.0) * 1.4 * radial;

                float n1 = Noise(float2(surface.x * 6.5, surface.y * 6.5 - travel));
                float n2 = Noise(float2(surface.x * 11.0 + 7.3, surface.y * 11.0 - travel * 1.6));
                float n3 = Noise(float2(surface.x * 3.0 - 4.1, surface.y * 3.0 - t * 1.1));
                float spikes = Ridge(n1) * 0.72 + Ridge(n2) * 0.28;
                spikes = pow(spikes, 1.8);

                // Long tongues along +V, short ones on the sides and tail.
                float up = dir.y;
                float directional = lerp(0.2, 1.0, smoothstep(-0.2, 0.9, up));
                float amplitude = flameLength * lerp(directional, 0.55, radial);
                float flicker = 0.9 + 0.1 * sin(t * 23.0 + n3 * 6.0);
                float thickness = (baseThickness * (0.8 + 0.4 * n3) + amplitude * spikes) *
                    flicker * lerp(0.35, 1.0, _AuraBlend);

                float e = d / max(thickness, 1e-3);
                // Hard anime edge with a thin soft rim.
                float inside = 1.0 - smoothstep(0.93, 1.0, e);
                // Inner flame layer: a second, tighter set of tongues.
                float innerThickness = thickness * (0.5 + 0.12 * Ridge(n2));
                float innerBand = 1.0 - smoothstep(0.82, 1.0, d / max(innerThickness, 1e-3));

                half3 color = lerp(_OuterColor.rgb, _MidColor.rgb, innerBand);
                color = lerp(color, _CoreColor.rgb, saturate(1.15 - e * 1.5) * (1.0 - _Front));
                // Thin bright streaks rising through the aura.
                float streak = smoothstep(0.86, 1.0,
                    Noise(float2(p.x * 26.0, p.y * 1.8 - t * 7.0)));
                color += streak * 0.35 * inside;

                float alpha = inside * lerp(0.62, 0.95, saturate(1.0 - e));
                // Soft halo outside the hard edge.
                float halo = exp(-max(d - thickness, 0.0) * 7.0) * (1.0 - inside) * 0.2;

                // Never let the halo show the sheet's rectangle.
                float2 uv = (p - _Rect.xy) / max(_Rect.zw - _Rect.xy, 1e-3);
                float2 border = smoothstep(0.0, 0.1, uv) * smoothstep(0.0, 0.1, 1.0 - uv);
                halo *= border.x * border.y;

                float strength = _AuraBlend * lerp(1.0, 0.2, _Front);
                alpha *= strength;
                half3 rgb = color * alpha + _OuterColor.rgb * halo * strength;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
