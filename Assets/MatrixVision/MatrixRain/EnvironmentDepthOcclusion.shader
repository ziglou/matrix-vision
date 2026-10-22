// Occlusion par le monde réel (Meta Quest) : écrit la profondeur d'environnement du casque dans le tampon de
// profondeur, avant les objets opaques. Pour chaque pixel, on cherche le point réel sur le rayon de l'œil :
// le point est reprojeté dans la caméra de profondeur, dont la distance lue corrige sa position (les deux caméras
// sont presque confondues, quelques itérations suffisent). Sa profondeur écran est ensuite écrite (SV_Depth).
Shader "Hidden/MatrixVision/EnvironmentDepthOcclusion"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite On ZTest Always Cull Off ColorMask 0

        Pass
        {
            Name "EnvironmentDepthOcclusion"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Publiés par ARShaderOcclusion (AR Foundation) à chaque frame de profondeur.
            TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
            SAMPLER(sampler_mv_point_clamp);
            float4x4 _EnvironmentDepthProjectionMatrices[2];
            float4 _NdcLinearConversionParameters;
            // Publiés par EnvironmentDepthOcclusion.
            float4x4 _MV_WorldToTracking;
            float4 _MV_OcclusionBias; // x : recul en mètres, y : recul relatif à la distance

            static const float MaxDistance = 20.0;

            // Profondeur linéaire (mètres) le long de l'axe de la caméra de profondeur ; < 0 si indisponible.
            float SampleEnvironmentLinearDepth(float3 positionWS, uint eye, out float viewDepth)
            {
                float4 positionTS = mul(_MV_WorldToTracking, float4(positionWS, 1.0));
                float4 clip = mul(_EnvironmentDepthProjectionMatrices[eye], positionTS);
                viewDepth = clip.w;
                if (clip.w <= 1e-4)
                    return -1.0;
                float2 uv = clip.xy / clip.w * 0.5 + 0.5;
                if (any(uv < 0.0) || any(uv > 1.0))
                    return -1.0;
                float depthNdc = SAMPLE_TEXTURE2D_ARRAY_LOD(_EnvironmentDepthTexture, sampler_mv_point_clamp, uv, eye, 0).r;
                if (depthNdc >= 0.9999)
                    return -1.0;
                return _NdcLinearConversionParameters.x / (depthNdc * 2.0 - 1.0 + _NdcLinearConversionParameters.y);
            }

            half4 Frag(Varyings input, out float outDepth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(USING_STEREO_MATRICES)
                uint eye = unity_StereoEyeIndex;
            #else
                uint eye = 0;
            #endif

                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float3 cameraWS = _WorldSpaceCameraPos;
                float3 onRayWS = ComputeWorldSpacePosition(screenUV, 0.5, UNITY_MATRIX_I_VP);
                float3 direction = normalize(onRayWS - cameraWS);

                float distance = 1.5;
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float viewDepth;
                    float linearDepth = SampleEnvironmentLinearDepth(cameraWS + direction * distance, eye, viewDepth);
                    if (linearDepth <= 0.0 || linearDepth > MaxDistance)
                        discard;
                    distance *= linearDepth / viewDepth;
                }

                distance += _MV_OcclusionBias.x + distance * _MV_OcclusionBias.y;
                float4 clipEye = mul(UNITY_MATRIX_VP, float4(cameraWS + direction * distance, 1.0));
                outDepth = clipEye.z / clipEye.w;
                return 0;
            }
            ENDHLSL
        }
    }
}
