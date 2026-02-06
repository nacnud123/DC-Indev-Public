#version 330 core

in vec3 fragColor;
in vec3 fragNormal;
in vec2 texCoord;
in float fragDist;

out vec4 FragColor;

uniform vec3 lightDir;
uniform vec3 lightColor;
uniform float ambientStrength;
uniform sampler2D blockTexture;

uniform vec3 fogColor;
uniform float fogStart;
uniform float fogEnd;

void main()
{
    vec4 texColor = texture(blockTexture, texCoord);

    if (texColor.a < 0.1)
        discard;

    vec3 ambient = ambientStrength * lightColor;
    vec3 norm = normalize(fragNormal);
    float diff = max(dot(norm, -lightDir), 0.0);
    vec3 diffuse = diff * lightColor;

    vec3 lighting = (ambient + diffuse) * fragColor;
    vec3 baseColor = texColor.rgb * lighting;

    // Fog
    float fogFactor = clamp((fogEnd - fragDist) / (fogEnd - fogStart), 0.0, 1.0);
    vec3 finalColor = mix(fogColor, baseColor, fogFactor);

    FragColor = vec4(finalColor, texColor.a);
}
