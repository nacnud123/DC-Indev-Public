#version 330 core

layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUV;
layout(location=2) in float aShade;

out vec2 uv;
out float shade;
uniform mat4 mvp;

void main() {
    gl_Position = mvp * vec4(aPos, 1.0);
    uv = aUV;
    shade = aShade;
}
