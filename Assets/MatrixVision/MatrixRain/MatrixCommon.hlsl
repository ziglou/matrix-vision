// Fonctions partagées par la pluie plein écran (MatrixRain.shader) et le code
// qui épouse les objets (MatrixSurface.shader). Les paramètres sont des globales
// poussées une fois par frame par MatrixRainRendererFeature depuis le Volume.
#ifndef MATRIX_COMMON_INCLUDED
#define MATRIX_COMMON_INCLUDED

TEXTURE2D(_MatrixGlyphAtlas);
SAMPLER(sampler_MatrixGlyphAtlas);

float4 _MatrixColor;
float  _MatrixIntensity;
float  _MatrixSpeed;           // cellules par seconde
float  _MatrixTrailLength;     // longueur de la traînée en cellules
float  _MatrixGlyphChange;     // changements de glyphe par seconde
float  _MatrixGlow;            // halo SDF autour des glyphes
float  _MatrixFlicker;         // scintillement aléatoire
float  _MatrixHeadBrightness;  // luminosité HDR de la tête de colonne (> 1 : le Bloom la fait rayonner)
float  _MatrixTrailBrightness; // luminosité HDR de la traînée

#define MATRIX_ATLAS_GRID 8.0
#define MATRIX_GLYPH_COUNT 64.0

// Portion de chaque case de l'atlas utilisée (le glyphe est centré, entouré de vide).
static const float2 MatrixAtlasMin = float2(0.2, 0.1);
static const float2 MatrixAtlasMax = float2(0.8, 0.9);

float MatrixHash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float MatrixHash21(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Intensité du glyphe (corps + halo) dans une cellule.
// cellUV : position dans la cellule, (0,0) en bas à gauche, y vers le haut.
// dx, dy : dérivées écran de cellUV, calculées sur des coordonnées continues pour un
// filtrage mipmap + anisotrope correct (glyphes nets et stables même très loin).
float MatrixGlyph(float2 cellUV, float2 cellId, float2 dx, float2 dy)
{
    float rate = _MatrixGlyphChange * (0.3 + MatrixHash21(cellId));
    float seed = floor(_Time.y * rate + MatrixHash21(cellId + 5.1) * 10.0);
    float index = floor(MatrixHash21(cellId * 1.37 + seed * 0.618) * MATRIX_GLYPH_COUNT);

    // Glyphe n°i : colonne i % 8, ligne i / 8 depuis le haut de l'atlas.
    float2 atlasCell = float2(fmod(index, MATRIX_ATLAS_GRID), MATRIX_ATLAS_GRID - 1.0 - floor(index / MATRIX_ATLAS_GRID));
    float2 scale = (MatrixAtlasMax - MatrixAtlasMin) / MATRIX_ATLAS_GRID;
    float2 uv = (atlasCell + lerp(MatrixAtlasMin, MatrixAtlasMax, saturate(cellUV))) / MATRIX_ATLAS_GRID;
    float d = SAMPLE_TEXTURE2D_GRAD(_MatrixGlyphAtlas, sampler_MatrixGlyphAtlas, uv, dx * scale, dy * scale).r;

    // Largeur d'anticrénelage du bord SDF, déduite de la taille à l'écran de la cellule.
    float texels = max(length(dx * scale), length(dy * scale)) * 512.0;
    float aa = clamp(texels * 0.06, 0.01, 0.25);
    float body = smoothstep(0.5 - aa, 0.5 + aa, d);
    float halo = smoothstep(0.2, 0.5, d) * _MatrixGlow;

    float flicker = lerp(1.0, 0.35 + 0.65 * MatrixHash21(cellId + floor(_Time.y * 8.0)), _MatrixFlicker);
    return (body + halo * 0.5) * flicker;
}

// Une goutte par colonne et par couche : renvoie (traînée, tête).
// span : nombre de cellules avant que la goutte ne reboucle.
float2 MatrixDrop(float column, float row, float span, float layer)
{
    float rnd = MatrixHash11(column * 1.73 + layer * 57.1);
    float speed = _MatrixSpeed * (0.5 + rnd);
    float cycle = span + _MatrixTrailLength * 2.0;
    float head = fmod(_Time.y * speed + MatrixHash11(column * 3.1 + layer * 11.3) * cycle, cycle);

    row = row - cycle * floor(row / cycle); // ramène la ligne dans [0, cycle)
    float dist = head - row;                 // > 0 : au-dessus de la tête, donc dans la traînée
    if (dist < 0.0)
        return 0.0;

    float trail = saturate(1.0 - dist / _MatrixTrailLength);
    float isHead = step(dist, 1.0);
    return float2(trail * trail, isHead);
}

// Couleur d'une cellule : tête blanc-vert éblouissante, traînée verte qui s'éteint,
// quelques glyphes de la traînée qui s'illuminent au hasard.
float3 MatrixShade(float glyph, float2 drop, float2 cellId)
{
    float3 headColor = lerp(_MatrixColor.rgb, float3(1.0, 1.0, 1.0), 0.75) * _MatrixHeadBrightness;
    float sparkle = step(0.97, MatrixHash21(cellId + floor(_Time.y * 3.0) * 0.37)) * 2.0;
    float3 trailColor = _MatrixColor.rgb * _MatrixTrailBrightness * (1.0 + sparkle);
    return glyph * (drop.x * trailColor + drop.y * headColor);
}

// Pluie complète pour une grille de cellules. cellCoord : y vers le bas (sens de la chute).
// layers : 2 ou 3 gouttes par colonne (3 = plus dense, pour les petites surfaces).
// density : proportion de colonnes actives (1 = toutes).
float3 MatrixRainAt(float2 cellCoord, float span, float layers, float density)
{
    // Dérivées prises avant tout branchement, sur les coordonnées continues (y inversé comme cellUV).
    float2 dx = ddx(cellCoord) * float2(1.0, -1.0);
    float2 dy = ddy(cellCoord) * float2(1.0, -1.0);

    float2 cellId = floor(cellCoord);
    float2 cellUV = frac(cellCoord);
    cellUV.y = 1.0 - cellUV.y;

    if (MatrixHash11(cellId.x * 0.713 + 91.7) > density)
        return 0.0;

    float2 drop = max(MatrixDrop(cellId.x, cellId.y, span, 0.0), MatrixDrop(cellId.x, cellId.y, span, 1.0));
    if (layers > 2.0)
        drop = max(drop, MatrixDrop(cellId.x, cellId.y, span, 2.0));
    if (drop.x <= 0.0 && drop.y <= 0.0)
        return 0.0;

    return MatrixShade(MatrixGlyph(cellUV, cellId, dx, dy), drop, cellId);
}

#endif
