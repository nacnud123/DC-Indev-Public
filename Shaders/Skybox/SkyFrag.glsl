#version 330 core

out vec4 FragColor;

uniform vec3 skyColor;   // pre-modulated sky color (computed in RenderSky)

void main()
{
    FragColor = vec4(skyColor, 1.0);
}
