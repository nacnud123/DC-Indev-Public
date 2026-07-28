#version 330 core

layout(location = 0) in vec2 aPos;

uniform mat4 mvp;
uniform float uAlpha;

out float vAlpha;

void main()
{
    gl_Position = mvp * vec4(aPos.x, 0.0, aPos.y, 1.0);

    vAlpha = uAlpha * (1.0 - length(aPos));
}
