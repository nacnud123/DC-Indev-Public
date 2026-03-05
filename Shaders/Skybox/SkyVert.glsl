#version 330 core

layout(location = 0) in vec3 aPosition;    // vertex in local plane space

uniform mat4 view;
uniform mat4 projection;
uniform vec3 playerPos;     // camera world position — sky plane follows XZ

void main()
{
    // Sky plane stays at a fixed world-Y but follows player in XZ
    // so the dome always covers the visible horizon no matter where the player is.
    vec3 worldPos = vec3(aPosition.x + playerPos.x,
                         aPosition.y,               // already world-Y = HEIGHT+10
                         aPosition.z + playerPos.z);
    gl_Position = projection * view * vec4(worldPos, 1.0);
}
