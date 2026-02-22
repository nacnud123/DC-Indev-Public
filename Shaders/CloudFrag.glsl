#version 330 core

in vec2  vTexCoord;
in float vFragDist;
out vec4 FragColor;

uniform sampler2D cloudTexture;
uniform vec3      cloudColor;

uniform vec3  fogColor;
uniform float fogStart;
uniform float fogEnd;

void main()
{
    vec4 texSample = texture(cloudTexture, vTexCoord);

    if (texSample.a < 0.1)
        discard;

    vec3 color = cloudColor * texSample.rgb;
    
    float fogFactor = clamp((fogEnd - vFragDist) / (fogEnd - fogStart), 0.0, 1.0);
    color = mix(fogColor, color, fogFactor);

    FragColor = vec4(color, texSample.a);
}
