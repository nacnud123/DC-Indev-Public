#version 330 core

layout(location = 0) in vec3 aPosition;    // pre-rotated star quad vertex

uniform mat4 celestialMVP;        // same celestial matrix as sun/moon
uniform float starBrightness;     // 0.0 (day) to 0.5 (full night)

out float vBrightness;

void main()
{
    gl_Position = celestialMVP * vec4(aPosition, 1.0);
    vBrightness = starBrightness;
}
