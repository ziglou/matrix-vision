// Code Matrix qui épouse la surface des objets : projection triplanaire en espace
// objet (le code suit la position, la rotation et l'échelle), colonnes qui tombent
// le long de l'axe Y local (ou Y monde), liseré vert sur la silhouette.
// Utilisé comme override material par MatrixRainRendererFeature, après le post-process :
// le test de profondeur se fait à la main contre _CameraDepthTexture.
//
// En AR (plans détectés) : occlusion douce par les objets réels (profondeur ARCore), fondu
// d'apparition et bord adouci par plan (MaterialPropertyBlock posé par MatrixARController et
// vertex color de MatrixARPlaneMesh), vague de balayage qui révèle le code.
Shader "Hidden/MatrixVision/MatrixSurface"
{
    Properties
    {
        // Always (8) : test de profondeur manuel contre _CameraDepthTexture.
        // LEqual (4) : mode XR léger, test matériel contre la profondeur écrite par les surfaces elles-mêmes.
        [Enum(UnityEngine.Rendering.CompareFunction)] _MatrixZTest ("ZTest", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        // Cull Off : les plans AR n'ont qu'une face, dont l'orientation dépend de l'appareil.
        // Offset : le code repasse exactement sur la géométrie qui a écrit la profondeur.
        ZWrite Off ZTest [_MatrixZTest] Cull Off Offset -1, -1

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "MatrixCommon.hlsl"
        #include "MatrixEnvironmentDepth.hlsl"

        float _MatrixSurfaceIntensity;
        float _MatrixSurfaceCellSize;  // en mètres
        float _MatrixSurfaceWorldFall; // 1 = code fixe dans le monde, 0 = attaché à l'objet
        float _MatrixSurfaceRim;       // liseré sur la silhouette

        // Vague de balayage (globales, MatrixARController)
        float4 _MatrixRevealOrigin;    // xyz = centre de la vague, en mètres
        float  _MatrixRevealRadius;    // rayon actuel du front de la vague
        float  _MatrixRevealMask;      // 1 = seul l'intérieur de la vague est révélé (premier balayage)
        float  _MatrixRevealGlow;      // 1 = front lumineux visible

        // Par objet (MaterialPropertyBlock) ; 0 par défaut, donc sans effet sur les objets ordinaires.
        float _MatrixPlaneHide;        // 0 = visible, 1 = caché (fondu d'apparition des plans)
        float _MatrixEdgeFade;         // 1 = bords adoucis par l'alpha du vertex color
        float _MatrixSkipSceneDepth;   // 1 = mode XR léger : pas de test contre _CameraDepthTexture
        float _MatrixForceObjectSpace; // 1 = code attaché à l'objet même si World Fall est actif (personnage animé)
        float _MatrixClipEnabled;      // 1 = matérialisation : code visible seulement sous _MatrixClipHeight
        float _MatrixClipHeight;       // hauteur monde de la ligne de matérialisation

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0; // mis à l'échelle de l'objet, en mètres
            float3 normalOS   : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            float3 normalWS   : TEXCOORD3;
            float  edge       : TEXCOORD4; // 1 au cœur de la surface, 0 sur un bord adouci
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
            output.edge = lerp(1.0, input.color.a, _MatrixEdgeFade);
            return output;
        }

        // Visibilité de 0 à 1 : 0 si un objet virtuel est devant, progressive si un objet réel l'est (AR).
        float MatrixVisibility(float4 positionCS)
        {
            float fragEye = LinearEyeDepth(positionCS.z, _ZBufferParams);
            // Mode XR léger (casque) : pas de copie de profondeur, et pas d'objet virtuel opaque à respecter.
            if (_MatrixSkipSceneDepth > 0.5)
                return 1.0;

            float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
            float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
            if (fragEye > sceneEye * 1.01 + 0.01)
                return 0.0;

            // Pixel écran avec l'origine en bas à gauche, comme les UV de la profondeur ARCore.
            float2 envUV = positionCS.xy / _ScaledScreenParams.xy;
            #if UNITY_UV_STARTS_AT_TOP
            envUV.y = 1.0 - envUV.y;
            #endif
            return 1.0 - MatrixEnvOcclusion(envUV, fragEye);
        }

        float3 MatrixSurfaceColor(Varyings input)
        {
            // Maillages sans normales (maillage de pièce du Quest) : normale de la face, déduite des dérivées.
            float3 faceNormalWS = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
            float hasNormals = step(0.25, dot(input.normalWS, input.normalWS));
            input.normalWS = lerp(faceNormalWS, input.normalWS, hasNormals);
            input.normalOS = lerp(TransformWorldToObjectDir(faceNormalWS), input.normalOS, hasNormals);

            float worldFall = _MatrixSurfaceWorldFall * (1.0 - _MatrixForceObjectSpace);
            float3 p = lerp(input.positionOS, input.positionWS, worldFall);
            float3 n = normalize(lerp(input.normalOS, input.normalWS, worldFall));
            float3 code = MatrixTriplanar(p, n, _MatrixSurfaceCellSize);

            // Vague de balayage : le code naît derrière le front, d'abord blanc puis vert.
            float dist = distance(input.positionWS, _MatrixRevealOrigin.xyz);
            float behind = _MatrixRevealRadius - dist; // > 0 : déjà traversé par la vague
            float revealed = lerp(1.0, smoothstep(-0.1, 0.25, behind), _MatrixRevealMask);
            float freshness = saturate(1.0 - behind / 1.2) * step(0.0, behind) * _MatrixRevealGlow;
            float luma = dot(code, float3(0.3, 0.59, 0.11));
            code = lerp(code, luma * 1.6, freshness * 0.7) * (1.0 + freshness);
            float front = exp(-pow(behind / 0.12, 2.0)) * _MatrixRevealGlow;
            float3 frontColor = lerp(_MatrixColor.rgb, 1.0, 0.4) * _MatrixHeadBrightness * 0.35 * front;

            // Liseré sur la silhouette, sauf sur les surfaces horizontales : vu en rasant, un sol serait tout vert.
            float3 normalWS = normalize(input.normalWS);
            float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
            float rim = pow(1.0 - abs(dot(normalWS, viewDir)), 3.0) * (1.0 - abs(normalWS.y));

            float3 color = code * revealed + rim * revealed * _MatrixSurfaceRim * _MatrixTrailBrightness * _MatrixColor.rgb + frontColor;

            // Matérialisation (personnage) : code sous la ligne, rien au-dessus, ligne de balayage lumineuse.
            float below = _MatrixClipHeight - input.positionWS.y;
            float scanLine = exp(-pow(below / 0.03, 2.0)) * _MatrixClipEnabled;
            color = color * lerp(1.0, smoothstep(-0.01, 0.04, below), _MatrixClipEnabled)
                  + scanLine * lerp(_MatrixColor.rgb, 1.0, 0.5) * max(_MatrixHeadBrightness, 1.0);
            // Tout près de la caméra (< 30 cm), les glyphes deviennent énormes et flous : on les estompe.
            float nearFade = smoothstep(0.15, 0.45, length(GetWorldSpaceViewDir(input.positionWS)));
            float fade = input.edge * (1.0 - _MatrixPlaneHide) * nearFade;
            return color * fade * _MatrixSurfaceIntensity * _MatrixIntensity;
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
                float visibility = MatrixVisibility(input.positionCS);
                if (visibility <= 0.0)
                    discard;
                return half4(MatrixSurfaceColor(input) * visibility, 0.0);
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
                if (MatrixVisibility(input.positionCS) < 0.5)
                    discard;
                float3 baseColor = 0.02 * _MatrixColor.rgb;
                return half4(baseColor + MatrixSurfaceColor(input), 1.0);
            }
            ENDHLSL
        }
    }
}
