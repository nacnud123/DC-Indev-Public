#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aTexCoord;

out vec3 fragColor;
out vec3 fragNormal;
out vec2 texCoord;
out float fragDist;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main()
{
    vec4 viewPos = view * model * vec4(aPosition, 1.0);
    gl_Position = projection * viewPos;
    fragColor = aColor;
    fragNormal = aNormal;
    texCoord = aTexCoord;
    fragDist = length(viewPos.xyz);
}
