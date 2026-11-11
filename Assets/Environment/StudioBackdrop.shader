// Procedural "studio cyclorama" backdrop: a soft mint/cyan wall with a bright glow, a darker teal corner, a thin
// baseboard (dark band, bright line, dark line) and a blue-grey floor. Meant for a camera-filling quad
// (see BackdropFitter). Glows are computed in aspect-corrected space so they stay round in portrait and landscape,
// and the baseboard lines are sized in screen pixels so they stay crisp at any resolution.
Shader "Custom/StudioBackdrop"
{
    Properties
    {
        _WallTop      ("Wall Top",        Color) = (0.80, 0.97, 0.97, 1)
        _WallBottom   ("Wall Bottom",     Color) = (0.68, 0.89, 0.91, 1)
        _GlowColor    ("Glow Color",      Color) = (0.92, 1.00, 0.99, 1)
        _Glow1        ("Glow 1 (xy pos, z radius, w strength)", Vector) = (0.32, 0.80, 0.42, 0.85)
        _Glow2        ("Glow 2 (xy pos, z radius, w strength)", Vector) = (0.60, 0.50, 0.38, 0.60)
        _CornerColor  ("Corner Shade",    Color) = (0.62, 0.76, 0.75, 1)
        _Corner       ("Corner (xy pos, z radius, w strength)", Vector) = (1.05, 1.05, 0.45, 0.75)
        _Horizon      ("Horizon (0 = bottom, 1 = top)", Range(0, 1)) = 0.2
        _BandColor    ("Baseboard Band",  Color) = (0.40, 0.38, 0.48, 1)
        _LineColor    ("Baseboard Line",  Color) = (0.74, 0.90, 0.92, 1)
        _BandPixels   ("Band Height (px at 1080p)", Float) = 14
        _FloorTop     ("Floor Near Line", Color) = (0.55, 0.62, 0.70, 1)
        _FloorSheen   ("Floor Sheen",     Color) = (0.64, 0.78, 0.85, 1)
        _FloorBottom  ("Floor Bottom",    Color) = (0.48, 0.53, 0.60, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WallTop, _WallBottom, _GlowColor, _CornerColor;
                float4 _Glow1, _Glow2, _Corner;
                float _Horizon, _BandPixels;
                half4 _BandColor, _LineColor, _FloorTop, _FloorSheen, _FloorBottom;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // Gaussian blob; radius is in screen heights.
            float Blob(float2 uv, float4 g, float aspect)
            {
                // Round in portrait; in landscape the glows widen with the screen so they still reach the sides.
                float2 d = (uv - g.xy) * float2(min(aspect, 1), 1);
                return g.w * exp(-dot(d, d) / (g.z * g.z));
            }

            // 0 below a, 1 above b, smooth in between, with a minimum width of one pixel for anti-aliasing.
            float Step(float a, float b, float x, float px) { return smoothstep(a - px * 0.5, b + px * 0.5, x); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float px = max(fwidth(uv.y), 1e-5);                 // one screen pixel in uv.y
                float unit = px * (_ScreenParams.y / 1080.0);        // one "1080p pixel" in uv.y

                // Wall
                half3 wall = lerp(_WallBottom.rgb, _WallTop.rgb, smoothstep(_Horizon, 0.85, uv.y));
                wall = lerp(wall, _GlowColor.rgb, Blob(uv, _Glow1, aspect));
                wall = lerp(wall, _GlowColor.rgb, Blob(uv, _Glow2, aspect));
                wall = lerp(wall, _CornerColor.rgb, Blob(uv, _Corner, aspect));

                // Floor: sheen just below the baseboard, darkening toward the bottom and the sides.
                float band = _BandPixels * unit;
                float floorTopY = _Horizon - band;
                float f = saturate(1 - uv.y / max(floorTopY, 1e-4));  // 0 at baseboard, 1 at bottom
                half3 floorCol = f < 0.2 ? lerp(_FloorTop.rgb, _FloorSheen.rgb, smoothstep(0, 0.2, f))
                                         : lerp(_FloorSheen.rgb, _FloorBottom.rgb, smoothstep(0.2, 1, f));
                floorCol *= 1 - 0.12 * abs(uv.x - 0.5) * 2;

                half3 c = uv.y > _Horizon ? wall : floorCol;

                // Baseboard, top to bottom: dark band (60%), bright line (20%), dark line (20%).
                float y = _Horizon - uv.y;   // 0 at horizon, grows downward
                float inBand  = Step(0, 0, y, px) * (1 - Step(band * 0.6, band * 0.6, y, px));
                float inLine  = Step(band * 0.6, band * 0.6, y, px) * (1 - Step(band * 0.8, band * 0.8, y, px));
                float inDark2 = Step(band * 0.8, band * 0.8, y, px) * (1 - Step(band, band, y, px));
                half3 bandCol = lerp(_BandColor.rgb, _BandColor.rgb * 1.15, sin(saturate(y / (band * 0.6)) * PI));
                c = lerp(c, bandCol, inBand);
                c = lerp(c, _LineColor.rgb, inLine);
                c = lerp(c, _BandColor.rgb * 1.1, inDark2);

                // Tiny dither against banding in the soft gradients.
                float n = frac(sin(dot(i.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                c += n / 255.0;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
