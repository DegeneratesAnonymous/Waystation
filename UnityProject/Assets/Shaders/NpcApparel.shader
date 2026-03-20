Shader "Waystation/NpcApparel"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _MaskTex ("Mask Texture", 2D) = "black" {}
        [PerRendererData] _Color ("Tint", Color) = (1,1,1,1)
        _TintColor0 ("Tint Colour 0", Color) = (1,1,1,1)
        _TintColor1 ("Tint Colour 1", Color) = (1,1,1,1)
        _TintColor2 ("Tint Colour 2", Color) = (1,1,1,1)
        _TintColor3 ("Tint Colour 3", Color) = (1,1,1,1)
        _TintColor4 ("Tint Colour 4", Color) = (1,1,1,1)
        _TintColor5 ("Tint Colour 5", Color) = (1,1,1,1)
        _TintColor6 ("Tint Colour 6", Color) = (1,1,1,1)
        _TintColor7 ("Tint Colour 7", Color) = (1,1,1,1)
        _MaskKey0 ("Mask Key 0", Color) = (1,0,0,1)
        _MaskKey1 ("Mask Key 1", Color) = (0,1,0,1)
        _MaskKey2 ("Mask Key 2", Color) = (0,0,1,1)
        _MaskKey3 ("Mask Key 3", Color) = (1,1,0,1)
        _MaskKey4 ("Mask Key 4", Color) = (0,1,1,1)
        _MaskKey5 ("Mask Key 5", Color) = (1,0,1,1)
        _MaskKey6 ("Mask Key 6", Color) = (1,0.5,0,1)
        _MaskKey7 ("Mask Key 7", Color) = (0.5,0,1,1)
        _MaskMatchThreshold ("Mask Match Threshold", Float) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "NpcApparelPass"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FEATURE_SHADER_RECOLOUR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _TintColor0, _TintColor1, _TintColor2, _TintColor3;
                float4 _TintColor4, _TintColor5, _TintColor6, _TintColor7;
                float4 _MaskKey0, _MaskKey1, _MaskKey2, _MaskKey3;
                float4 _MaskKey4, _MaskKey5, _MaskKey6, _MaskKey7;
                float  _MaskMatchThreshold;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color;
                return OUT;
            }

            bool ColourMatch(float3 a, float3 b, float threshold)
            {
                return length(a - b) < threshold;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color * _Color;

#ifdef FEATURE_SHADER_RECOLOUR
                half4 maskCol = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, IN.uv);
                float3 mc     = maskCol.rgb;
                float  thresh = _MaskMatchThreshold;

                // Black mask region = no tint; pass base through
                if (length(mc) < thresh)
                {
                    return baseCol;
                }

                // Check each slot key — first match wins
                float4 tints[8]   = { _TintColor0,_TintColor1,_TintColor2,_TintColor3,
                                      _TintColor4,_TintColor5,_TintColor6,_TintColor7 };
                float4 keys[8]    = { _MaskKey0,_MaskKey1,_MaskKey2,_MaskKey3,
                                      _MaskKey4,_MaskKey5,_MaskKey6,_MaskKey7 };

                for (int i = 0; i < 8; i++)
                {
                    if (ColourMatch(mc, keys[i].rgb, thresh))
                    {
                        return float4(baseCol.rgb * tints[i].rgb, baseCol.a);
                    }
                }
#endif
                // No match / feature disabled — return base unmodified
                return baseCol;
            }
            ENDHLSL
        }
    }
    FallBack "Sprites/Default"
}
