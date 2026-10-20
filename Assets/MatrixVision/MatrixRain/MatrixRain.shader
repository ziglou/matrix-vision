// Post-process "pluie de code" façon Matrix en plein écran : glyphes issus de
// l'atlas (katakana en miroir), scène teintée en vert. Sur la géométrie, la pluie
// est atténuée pour laisser la place au code des objets (MatrixSurface.shader).
Shader "Hidden/MatrixVision/MatrixRain"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "MatrixRain"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "MatrixCommon.hlsl"

            float _MatrixCellSize;            // taille d'une cellule en pixels
            float _MatrixSceneTint;           // 0 = scène d'origine, 1 = scène monochrome verte
            float _MatrixBackgroundOnGeometry; // pluie écran conservée sur la géométrie
            float _MatrixScreenRain;           // 0 = pas de lettres au premier plan (teinte seule)
            float _MatrixDepthLayers;          // 1 = plans lointain et proche en plus du plan principal
            float _MatrixSceneBrightness;      // luminosité de la scène teintée

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float cellPx = max(_MatrixCellSize, 4.0);
                float2 pixel = float2(uv.x, 1.0 - uv.y) * _ScreenParams.xy; // y vers le bas

                // Trois plans de profondeur : petits glyphes lointains et sombres, plan principal,
                // gros glyphes épars tout près. Chaque plan a ses propres colonnes (décalage en x).
                float3 rain = 0.0;
                float farPx = cellPx * 0.6;
                rain += MatrixRainAt(pixel / farPx + float2(500.0, 0.0), _ScreenParams.y / farPx, 2.0, 1.0) * 0.35 * _MatrixDepthLayers;
                rain += MatrixRainAt(pixel / cellPx, _ScreenParams.y / cellPx, 2.0, 1.0);
                float nearPx = cellPx * 2.2;
                rain += MatrixRainAt(pixel / nearPx + float2(1500.0, 0.0), _ScreenParams.y / nearPx, 2.0, 0.25) * 1.3 * _MatrixDepthLayers;

                #if UNITY_REVERSED_Z
                bool isSky = SampleSceneDepth(uv) <= 0.0;
                #else
                bool isSky = SampleSceneDepth(uv) >= 1.0;
                #endif
                rain *= (isSky ? 1.0 : _MatrixBackgroundOnGeometry) * _MatrixScreenRain;

                // Scène monochrome verte et assombrie, pour que la pluie ressorte.
                float luma = dot(scene.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 tinted = _MatrixSceneBrightness * luma * _MatrixColor.rgb / max(max(_MatrixColor.r, _MatrixColor.g), max(_MatrixColor.b, 1e-4));
                float3 color = lerp(scene.rgb, tinted, _MatrixSceneTint * _MatrixIntensity) + rain * _MatrixIntensity;

                // En réalité mixte, le fond est transparent (alpha 0) et laisse voir le passthrough :
                // la pluie y rend l'image opaque pour rester visible par-dessus la pièce réelle.
                float rainAlpha = saturate(max(max(rain.r, rain.g), rain.b) * _MatrixIntensity);
                return half4(color, max(scene.a, rainAlpha));
            }
            ENDHLSL
        }
    }
}
