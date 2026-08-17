#version 330 core

in vec3 fragColor;
in vec3 fragNormal;
in vec2 texCoord;
in float fragDist;
flat in float fragFluidAnim;
in vec3 fragViewNormal;
in vec3 fragViewPos;

out vec4 FragColor;

uniform vec3 lightDir;
uniform vec3 lightColor;
uniform float ambientStrength;
uniform float sunlightLevel;
uniform sampler2D blockTexture;
uniform int fluidType;



uniform vec3 fogColor;
uniform float fogStart;
uniform float fogEnd;
uniform float alphaOverride;

// Seconds since the world loaded, for the fluid texture animation below.
uniform float uTime;

// world.png is a 16x16 grid of tiles (UvHelper.TILE_COUNT).
const float TILE = 1.0 / 16.0;

// Only *flowing* fluid scrolls. A source block - a lake, an ocean - is still water and is left
// alone; animating it made the whole world look like it was drifting. fragFluidAnim is 1 only on
// cells with a flow level or fed from above (see Chunk.BuildFluidFaces), and 0 on everything else,
// so this costs one compare for every non-fluid fragment.
//
// The tile lookup is still needed to know which rect to wrap the scroll inside: water sits at tile
// (0,4) and lava at (7,4) - see BlockWater/BlockLava's TopTextureCoords.
vec2 AnimateFluidTile(vec2 uv)
{
    if (fragFluidAnim < 0.5)
        return uv;

    vec2 tile = floor(uv / TILE);

    if (tile.y != 4.0 || (tile.x != 0.0 && tile.x != 7.0))
        return uv;

    // Lava crawls; water runs along faster.
    float speed = (tile.x == 0.0) ? 0.4 : 0.12;

    vec2 local = uv / TILE - tile;          // position within the tile, 0..1
    local.y = fract(local.y + uTime * speed);
    return (tile + local) * TILE;
}

void main()
{
    // Tile lookup on the raw (pre-animation) coord: AnimateFluidTile only shifts within a tile,
    // so the integer tile index is the same either way, and this way still-water source blocks
    // (which never go through the animated path) are identified too.
    vec2 tile = floor(texCoord / TILE);
    bool isWater = (tile.y == 4.0 && tile.x == 0.0);
    bool isLava  = (tile.y == 4.0 && tile.x == 7.0);

    vec4 texColor = texture(blockTexture, AnimateFluidTile(texCoord));
    if (texColor.a < 0.1)
        discard;


    // Directional sun lighting
    vec3 norm = normalize(fragNormal);
    float diff = max(dot(norm, -lightDir), 0.0);
    vec3 sun = (ambientStrength + diff) * lightColor;

    // Sky light: modulated by sun direction and time of day
    float skyBright = (0.1 + fragColor.r * 0.9) * sunlightLevel;
    vec3 skyContrib = sun * skyBright;

    // Block light: self-illuminating, independent of sun
    float blockBright = 0.1 + fragColor.g * 0.9;
    vec3 blockContrib = vec3(blockBright);

    // Combine and apply face shading
    vec3 lighting = max(skyContrib, blockContrib) * fragColor.b;
    lighting = max(lighting, vec3(0.04));  // ← never go fully black
    vec3 baseColor = texColor.rgb * lighting;

    float alpha = alphaOverride > 0.0 ? alphaOverride : texColor.a;

    // The atlas tiles for water/lava are pale so the animated ripple pattern reads clearly; the
    // actual color comes from here instead, applied to the block face itself (as opposed to
    // `fluidType` below, which is a full-screen tint only active while the camera is submerged).
    // Lava is never see-through - it's molten rock, not a window - so it always wins the alpha
    // override and gets a hot glow instead of following normal shading.
    if (isWater)
    {
        vec3 viewDir = normalize(-fragViewPos);
        vec3 viewNorm = normalize(fragViewNormal);
        float fresnel = pow(1.0 - max(dot(viewNorm, viewDir), 0.0), 3.0);

        baseColor *= vec3(0.25, 0.5, 0.85);
        baseColor += vec3(0.5, 0.7, 0.9) * fresnel * 0.6;
        alpha = clamp(max(alpha, 0.8) + fresnel * 0.15, 0.0, 1.0);
    }
    else if (isLava)
    {
        baseColor = mix(baseColor, texColor.rgb, 0.5) * vec3(1.4, 0.55, 0.15);
        alpha = 1.0;
    }

    // Fog
    float fogFactor = clamp((fogEnd - fragDist) / (fogEnd - fogStart), 0.0, 1.0);
    vec3 finalColor = mix(fogColor, baseColor, fogFactor);

    if(fluidType == 1)
        finalColor *= vec3(0.2, 0.4, 0.9);
    else if(fluidType == 2)
        finalColor *= vec3(0.9, 0.2, 0.1);

    FragColor = vec4(finalColor, alpha);
}
