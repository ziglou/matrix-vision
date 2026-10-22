// Voile sombre teinté posé sur le passthrough du casque (Meta Quest) : l'application n'a pas accès
// à l'image caméra, la teinte passe donc par l'alpha de sa couche. Sortie prémultipliée :
// couleur finale = rgb + (1 - alpha) × passthrough. Le code, additif, reste lumineux par-dessus.
Shader "Hidden/MatrixVision/MatrixPassthroughDim"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "MatrixPassthroughDim"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _MatrixColor;
            float  _MatrixIntensity;
            float  _MatrixPassthroughDim;  // 0 = passthrough intact, 1 = noir complet
            float  _MatrixSceneBrightness; // part de vert ajoutée au voile

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float alpha = saturate(_MatrixPassthroughDim * _MatrixIntensity);
                float3 tint = _MatrixColor.rgb / max(max(_MatrixColor.r, _MatrixColor.g), max(_MatrixColor.b, 1e-4));
                return half4(tint * _MatrixSceneBrightness * 0.25 * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
