// NpcApparel.shader — URP-compatible unlit sprite shader with per-channel
// mask-keyed tinting for the NPC clothing and hair layers.
//
// How it works:
//   1. _MainTex   : neutral-tone master sprite texture (base colours/detail).
//   2. _MaskTex   : companion mask atlas, same UVs. Each recolourable region is
//                   painted a distinct flat colour. Black (#000000) = uncoloured.
//   3. _TintColors: array of up to 8 runtime tint colours. Slot 0 → mask colour 0,
//                   slot 1 → mask colour 1, etc.
//   4. For each pixel the shader samples the mask. If the mask colour matches
//                   slot i, it multiplies the base colour by _TintColors[i].
//                   Black-mask pixels pass through unmodified.
//   5. Alpha is taken from the base texture only.
//
// Feature flag:
//   FEATURE_SHADER_RECOLOUR — when disabled the shader falls back to a plain
//   unlit sprite pass (mask and tint uniforms are compiled out).
//
// Mask colour slots (matches atlas generator output):
//   Slot 0: #FF0000 (red)   — primary recolour region
//   Slot 1: #00FF00 (green) — secondary recolour region
//   Slot 2: #0000FF (blue)  — tertiary recolour region
//   Slot 3: #FFFF00 (yellow)
//   Slot 4: #FF00FF (magenta)
//   Slot 5: #00FFFF (cyan)
//   Slot 6: #FF8000 (orange)
//   Slot 7: #8000FF (violet)
//   Black  (#000000): uncoloured — base texture renders unchanged.

Shader "Waystation/NpcApparel"
{
    Properties
    {
        _MainTex      ("Base Sprite",   2D)        = "white" {}
        _MaskTex      ("Mask Atlas",    2D)        = "black" {}
        // _TintColors is set per-renderer via MaterialPropertyBlock; not shown in Inspector.
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"            = "Transparent"
            "RenderType"       = "Transparent"
            "RenderPipeline"   = "UniversalPipeline"
            "IgnoreProjector"  = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual
        Lighting Off

        Pass
        {
            Name "NpcApparelPass"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // Feature flag: compile with or without recolour logic.
            #pragma multi_compile _ FEATURE_SHADER_RECOLOUR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);

            // SpriteRenderer drives this via the per-renderer colour property.
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;

#ifdef FEATURE_SHADER_RECOLOUR
                // Up to 8 tint colour slots; set via MaterialPropertyBlock._TintColors.
                half4 _TintColors[8];
#endif
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

#ifdef FEATURE_SHADER_RECOLOUR
                half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, IN.uv);

                // Mask colour matching with a small epsilon to handle floating-point
                // precision differences when the GPU samples fully flat-colour mask
                // pixels. Mask atlases are generated with exact flat colours (#FF0000,
                // #00FF00, etc.) so this tolerance only needs to cover GPU precision
                // artefacts, not gradients. 0.2 (out of 1.0) is intentionally generous
                // to account for potential texture format rounding; mask textures should
                // be imported as Uncompressed to minimise such artefacts.
                const half E = 0.2h;

                // Mask colour definitions (must match atlas generator slot colours):
                //   0: #FF0000   1: #00FF00   2: #0000FF   3: #FFFF00
                //   4: #FF00FF   5: #00FFFF   6: #FF8000   7: #8000FF
                static const half3 MASK_COLS[8] =
                {
                    half3(1, 0, 0),        // 0 red
                    half3(0, 1, 0),        // 1 green
                    half3(0, 0, 1),        // 2 blue
                    half3(1, 1, 0),        // 3 yellow
                    half3(1, 0, 1),        // 4 magenta
                    half3(0, 1, 1),        // 5 cyan
                    half3(1, 0.502h, 0),   // 6 orange (#FF8000)
                    half3(0.502h, 0, 1),   // 7 violet (#8000FF)
                };

                // Skip tinting if mask pixel is black (uncoloured region).
                bool isBlack = (mask.r < E && mask.g < E && mask.b < E);
                if (!isBlack)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        half3 diff = abs(mask.rgb - MASK_COLS[i]);
                        if (diff.r < E && diff.g < E && diff.b < E)
                        {
                            // Multiply-blend tint over base colour, preserve alpha.
                            base.rgb *= _TintColors[i].rgb;
                            break;
                        }
                    }
                }
#endif // FEATURE_SHADER_RECOLOUR

                // Discard fully transparent pixels.
                clip(base.a - 0.001h);
                return base;
            }
            ENDHLSL
        }
    }

    // Fallback to legacy unlit transparent if URP is not available.
    FallBack "Sprites/Default"
}
