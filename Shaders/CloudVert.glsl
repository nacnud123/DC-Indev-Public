#version 330 core

layout(location = 0) in vec3 aPosition;

out vec2  vTexCoord;
out float vFragDist;

uniform mat4  view;
uniform mat4  projection;
uniform float cloudPlaneY;
uniform vec3  playerPos;
uniform float uvScrollU;

const float CLOUD_TILE_SIZE = 256.0 * 16.0;

void main()
{
    vec3 worldPos = vec3(aPosition.x + playerPos.x, cloudPlaneY,
                         aPosition.z + playerPos.z);

    vec4 viewPos = view * vec4(worldPos, 1.0);
    gl_Position  = projection * viewPos;
    
    vec2 xzOffset = worldPos.xz - playerPos.xz;
    vFragDist = length(xzOffset);
    
    vTexCoord = vec2((worldPos.x + uvScrollU) / CLOUD_TILE_SIZE,
                      worldPos.z               / CLOUD_TILE_SIZE);
}
