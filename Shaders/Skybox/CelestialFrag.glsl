#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D celestialTex;
uniform float brightness;

void main()
{
    vec4 color = texture(celestialTex, vTexCoord);

    if (color.a < 0.05)
        discard;
        
    FragColor = vec4(color.rgb * brightness, color.a);
}
