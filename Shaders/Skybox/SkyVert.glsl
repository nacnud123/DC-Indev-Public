#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 view;
uniform mat4 projection;
uniform vec3 playerPos;

void main()
{
    vec3 worldPos = vec3(aPosition.x + playerPos.x,
                         aPosition.y,
                         aPosition.z + playerPos.z);
    gl_Position = projection * view * vec4(worldPos, 1.0);
}
