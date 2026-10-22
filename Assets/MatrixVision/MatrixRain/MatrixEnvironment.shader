// Code Matrix sur l'environnement réel (AR) : chaque pixel est replacé dans le monde grâce à la
// profondeur ARCore, puis reçoit le code triplanaire en espace monde (ancré sur les murs et les
// meubles, il ne glisse pas quand le téléphone bouge). Les bords des objets (sauts de profondeur)
// sont soulignés d'un liseré lumineux. Additif, par-dessus la pluie plein écran.
Shader "Hidden/MatrixVision/MatrixEnvironment"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend One One

        Pass
        {
            Name "MatrixEnvironment"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "MatrixCommon.hlsl"
            #include "MatrixEnvironmentDepth.hlsl"

            float _MatrixSurfaceIntensity;
            float _MatrixSurfaceCellSize; // en mètres
            float _MatrixSurfaceRim;      // force du liseré sur les bords

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float depth = MatrixEnvDepth(uv);
                if (!MatrixEnvHasSurface(depth))
                    return 0.0;

                // Voisins à quelques pixels : la carte de profondeur ARCore est basse résolution.
                float2 step = 6.0 / _ScreenParams.xy;
                float dl = MatrixEnvDepth(uv - float2(step.x, 0.0));
                float dr = MatrixEnvDepth(uv + float2(step.x, 0.0));
                float dd = MatrixEnvDepth(uv - float2(0.0, step.y));
                float du = MatrixEnvDepth(uv + float2(0.0, step.y));

                float3 p = MatrixEnvWorldPosition(uv, depth);

                // Normale : différences du côté le plus continu, pour ne pas mélanger deux objets sur un bord.
                float3 px = abs(dr - depth) < abs(depth - dl)
                    ? MatrixEnvWorldPosition(uv + float2(step.x, 0.0), dr) - p
                    : p - MatrixEnvWorldPosition(uv - float2(step.x, 0.0), dl);
                float3 py = abs(du - depth) < abs(depth - dd)
                    ? MatrixEnvWorldPosition(uv + float2(0.0, step.y), du) - p
                    : p - MatrixEnvWorldPosition(uv - float2(0.0, step.y), dd);
                float3 n = normalize(cross(px, py));
                float3 cameraPosition = _MatrixCamToWorld._m03_m13_m23;
                if (dot(n, cameraPosition - p) < 0.0)
                    n = -n;

                float3 code = MatrixTriplanar(p, n, _MatrixSurfaceCellSize);

                // Liseré sur les contours : fort saut de profondeur relatif entre voisins.
                float jump = max(abs(dr - dl), abs(du - dd)) / depth;
                float edge = smoothstep(0.08, 0.3, jump) * _MatrixSurfaceRim;
                float3 edgeColor = lerp(_MatrixColor.rgb, 1.0, 0.3) * _MatrixHeadBrightness * 0.5;

                float fade = 1.0 - smoothstep(_MatrixEnvMaxDistance * 0.7, _MatrixEnvMaxDistance, depth);
                float3 color = (code + edge * edgeColor) * fade * _MatrixSurfaceIntensity * _MatrixIntensity;
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
}
