// Code Matrix qui épouse la surface des objets : projection triplanaire en espace
// objet (le code suit la position, la rotation et l'échelle), colonnes qui tombent
// le long de l'axe Y local (ou Y monde), liseré vert sur la silhouette.
// Utilisé comme override material par MatrixRainRendererFeature, après le post-process :
// le test de profondeur se fait à la main contre _CameraDepthTexture.
Shader "Hidden/MatrixVision/MatrixSurface"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "MatrixCommon.hlsl"

        float _MatrixSurfaceIntensity;
        float _MatrixSurfaceCellSize; // en mètres
        float _MatrixSurfaceWorldFall; // 1 = code fixe dans le monde, 0 = attaché à l'objet
        float _MatrixSurfaceRim;       // liseré sur la silhouette
        float4 _MatrixRevealSphere;    // révélation : xyz centre monde, w rayon (négatif = tout est visible)
        float _MatrixRevealEdge;       // épaisseur du front lumineux de la révélation, en mètres

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0; // mis à l'échelle de l'objet, en mètres
            float3 normalOS   : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            float3 normalWS   : TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3x3 m = (float3x3)GetObjectToWorldMatrix();
            float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));

            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.positionOS = input.positionOS.xyz * scale;
            output.normalOS = input.normalOS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }

        // Faux si un autre objet est devant ce fragment.
        bool MatrixVisible(float4 positionCS)
        {
            float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
            float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
            float fragEye = LinearEyeDepth(positionCS.z, _ZBufferParams);
            return fragEye <= sceneEye * 1.01 + 0.01;
        }

        // Révélation progressive (MatrixReveal) : 0 hors de la sphère, 1 dedans.
        // front vaut 1 sur le bord intérieur de la sphère et retombe à 0 vers le centre.
        float MatrixRevealMask(float3 positionWS, out float front)
        {
            front = 0.0;
            if (_MatrixRevealSphere.w < 0.0)
                return 1.0;
            float d = distance(positionWS, _MatrixRevealSphere.xyz);
            if (d > _MatrixRevealSphere.w)
                return 0.0;
            front = saturate(1.0 - (_MatrixRevealSphere.w - d) / max(_MatrixRevealEdge, 1e-3));
            return 1.0;
        }

        // Code sur un plan de la projection triplanaire. uv.y = axe vertical (vers le haut).
        float3 MatrixPlane(float2 uv, float plane)
        {
            float2 cellCoord = float2(uv.x, -uv.y) / max(_MatrixSurfaceCellSize, 1e-3);
            cellCoord.x += plane * 1000.0; // décorrèle les colonnes des trois plans

            // Les glyphes restent de vrais glyphes à toute distance (atlas mipmappé + anisotrope).
            // Seule exception : à l'horizon, quand une colonne entière tient dans moins d'un tiers de pixel.
            float columnFade = saturate(4.0 - fwidth(cellCoord.x));
            // Cycle court et 3 gouttes par colonne : un objet est toujours couvert de code.
            return MatrixRainAt(cellCoord, 4.0, 3.0, 1.0) * columnFade;
        }

        float3 MatrixSurfaceColor(Varyings input)
        {
            float3 p = lerp(input.positionOS, input.positionWS, _MatrixSurfaceWorldFall);
            float3 n = normalize(lerp(input.normalOS, input.normalWS, _MatrixSurfaceWorldFall));

            float3 blend = pow(abs(n), 4.0);
            blend /= max(blend.x + blend.y + blend.z, 1e-4);

            // Faces latérales : le code tombe le long de Y. Faces du dessus/dessous : le long de Z.
            float3 code = blend.x * MatrixPlane(p.zy, 0.0)
                        + blend.z * MatrixPlane(p.xy, 1.0)
                        + blend.y * MatrixPlane(p.xz, 2.0);

            // Liseré sur la silhouette, sauf sur les surfaces horizontales : vu en rasant, un sol serait tout vert.
            float3 normalWS = normalize(input.normalWS);
            float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
            float rim = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0) * (1.0 - abs(normalWS.y));

            // Front de la révélation : une bande qui rayonne comme les têtes de colonne.
            float front;
            MatrixRevealMask(input.positionWS, front);
            float3 frontGlow = front * front * _MatrixHeadBrightness * _MatrixColor.rgb;

            return (code + rim * _MatrixSurfaceRim * _MatrixTrailBrightness * _MatrixColor.rgb + frontGlow) * _MatrixSurfaceIntensity * _MatrixIntensity;
        }
        ENDHLSL

        // Passe 0 : Overlay, le code s'ajoute à l'apparence de l'objet.
        Pass
        {
            Name "MatrixSurfaceOverlay"
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOverlay
            #pragma multi_compile_instancing

            half4 FragOverlay(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float front;
                if (!MatrixVisible(input.positionCS) || MatrixRevealMask(input.positionWS, front) <= 0.0)
                    discard;
                return half4(MatrixSurfaceColor(input), 0.0);
            }
            ENDHLSL
        }

        // Passe 1 : Replace, l'objet devient noir-vert, uniquement du code.
        Pass
        {
            Name "MatrixSurfaceReplace"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragReplace
            #pragma multi_compile_instancing

            half4 FragReplace(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float front;
                if (!MatrixVisible(input.positionCS) || MatrixRevealMask(input.positionWS, front) <= 0.0)
                    discard;
                float3 baseColor = 0.02 * _MatrixColor.rgb;
                return half4(baseColor + MatrixSurfaceColor(input), 1.0);
            }
            ENDHLSL
        }
    }
}
