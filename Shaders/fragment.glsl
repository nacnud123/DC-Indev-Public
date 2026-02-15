#version 330 core

in vec3 fragColor; // r=skyLight, g=blockLight, b=faceShade
in vec3 fragNormal;
in vec2 texCoord;
in float fragDist;

out vec4 FragColor;

uniform vec3 lightDir;
uniform vec3 lightColor;
uniform float ambientStrength;
uniform float sunlightLevel;
uniform sampler2D blockTexture;
uniform int underwater;



uniform vec3 fogColor;
uniform float fogStart;
uniform float fogEnd;
uniform float alphaOverride;

void main()
{
    vec4 texColor = texture(blockTexture, texCoord);
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
    vec3 baseColor = texColor.rgb * lighting;

    // Fog
    float fogFactor = clamp((fogEnd - fragDist) / (fogEnd - fogStart), 0.0, 1.0);
    vec3 finalColor = mix(fogColor, baseColor, fogFactor);

    if(underwater == 1)
    {
        vec3 waterTint = vec3(0.2, 0.4, 0.9);
        finalColor = finalColor * waterTint;
    }

    float alpha = alphaOverride > 0.0 ? alphaOverride : texColor.a;


    FragColor = vec4(finalColor, alpha);
}
