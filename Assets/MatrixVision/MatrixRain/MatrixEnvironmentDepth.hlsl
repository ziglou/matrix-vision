// Profondeur de l'environnement réel (AR) : fournie par MatrixARController à partir
// de l'AROcclusionManager d'AR Foundation. Distance en mètres le long de l'axe de visée.
#ifndef MATRIX_ENVIRONMENT_DEPTH_INCLUDED
#define MATRIX_ENVIRONMENT_DEPTH_INCLUDED

TEXTURE2D(_MatrixEnvDepth);
// Échantillonneur « inline » (point + clamp) : les textures flottantes ne sont pas filtrables partout (OpenGL ES).
SAMPLER(s_matrix_env_point_clamp_sampler);
float    _MatrixEnvDepthAvailable; // 1 = profondeur de l'environnement disponible cette frame
float    _MatrixEnvMaxDistance;    // au-delà, la profondeur n'est plus fiable
float4x4 _MatrixDisplayTransform;  // matrice d'affichage ARCore (convention « vecteur ligne »)
float4x4 _MatrixCamInvProjection;  // inverse de Camera.projectionMatrix (convention OpenGL)
float4x4 _MatrixCamToWorld;        // Camera.cameraToWorldMatrix

// Distance du réel au pixel d'écran uv ((0,0) en bas à gauche). 0 = inconnue.
float MatrixEnvDepth(float2 uv)
{
    float2 depthUV = mul(float4(uv, 1.0, 0.0), _MatrixDisplayTransform).xy;
    return SAMPLE_TEXTURE2D_LOD(_MatrixEnvDepth, s_matrix_env_point_clamp_sampler, depthUV, 0).r;
}

// Vrai si le pixel montre une surface réelle à portée fiable.
bool MatrixEnvHasSurface(float depth)
{
    return _MatrixEnvDepthAvailable > 0.5 && depth > 0.05 && depth < _MatrixEnvMaxDistance;
}

float _MatrixEnvOcclusionEnabled; // 1 = les objets réels masquent le code des plans

// Occlusion AR douce, de 0 (visible) à 1 (masqué) : un objet réel nettement devant le point virtuel
// situé à la distance eyeDepth le masque. Les plans sont collés aux surfaces réelles et la profondeur
// ARCore est bruitée : marge large (20 cm + 8 %) et transition progressive, jamais de clignotement.
float MatrixEnvOcclusion(float2 uv, float eyeDepth)
{
    if (_MatrixEnvDepthAvailable < 0.5 || _MatrixEnvOcclusionEnabled < 0.5)
        return 0.0;
    float depth = MatrixEnvDepth(uv);
    if (depth <= 0.05)
        return 0.0;
    float margin = 0.2 + 0.08 * eyeDepth;
    return smoothstep(margin, margin * 2.0, eyeDepth - depth);
}

// Position monde du point réel vu au pixel uv, à la distance depth.
float3 MatrixEnvWorldPosition(float2 uv, float depth)
{
    float4 view = mul(_MatrixCamInvProjection, float4(uv * 2.0 - 1.0, 1.0, 1.0));
    float3 ray = view.xyz / view.w;
    float3 positionVS = ray * (depth / max(-ray.z, 1e-4)); // vue OpenGL : la caméra regarde vers -Z
    return mul(_MatrixCamToWorld, float4(positionVS, 1.0)).xyz;
}

#endif
