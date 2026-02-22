#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2  vTexCoord;
out float vFragDist;

uniform mat4  view;
uniform mat4  projection;
uniform float cloudPlaneY;
uniform vec3  playerPos;
uniform float uvScrollU;

void main()
{
    vec3 worldPos = vec3((aPosition.x + playerPos.x) / 2,
                         cloudPlaneY,
                         (aPosition.z + playerPos.z) / 2);

    vec4 viewPos    = view * vec4(worldPos, 1.0);
    gl_Position     = projection * viewPos;
    vFragDist       = length(viewPos.xyz);

    vTexCoord = vec2(aTexCoord.x + uvScrollU, aTexCoord.y);
}
