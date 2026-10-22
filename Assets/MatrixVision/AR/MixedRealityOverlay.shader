// Visualisation discrète des plans et du maillage de la pièce sur le passthrough (Meta Quest) : voile
// translucide et grille en espace monde. Sortie prémultipliée, alpha conservé pour le compositeur :
// couleur finale = rgb × alpha + (1 - alpha) × passthrough.
Shader "MatrixVision/MixedRealityOverlay"
{
    Properties
    {
        _Color ("Voile", Color) = (0.1, 0.8, 1, 0.12)
        _GridColor ("Grille", Color) = (0.2, 1, 1, 0.6)
        _GridSize ("Pas de la grille (m)", Float) = 0.25
        _GridWidth ("Épaisseur de la grille (m)", Float) = 0.006
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            Name "MixedRealityOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _GridColor;
                float _GridSize;
                float _GridWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 cell = abs(frac(input.positionWS / _GridSize + 0.5) - 0.5) * _GridSize;
                float3 width = max(fwidth(input.positionWS), _GridWidth);
                float3 lines = 1.0 - smoothstep(0.0, width, cell);
                float grid = saturate(max(lines.x, max(lines.y, lines.z)));
                return lerp(_Color, _GridColor, grid * _GridColor.a);
            }
            ENDHLSL
        }
    }
}
