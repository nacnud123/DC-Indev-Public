#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 celestialMVP;
uniform float starBrightness;

out float vBrightness;

void main()
{
    gl_Position = celestialMVP * vec4(aPosition, 1.0);
    vBrightness = starBrightness;
}
