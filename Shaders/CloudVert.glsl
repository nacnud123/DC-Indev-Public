#version 330 core

layout(location = 0) in vec3 aPosition;

out vec2  vTexCoord;
out float vFragDist;

uniform mat4  view;
uniform mat4  projection;
uniform float cloudPlaneY;
uniform vec3  playerPos;
uniform float uvScrollU;

// World units per texture pixel. 256px texture × 16 = one full tile every 4096 units,
// matching the original visual scale where each pixel appeared as a ~16-unit cloud puff.
const float CLOUD_TILE_SIZE = 256.0 * 16.0;

void main()
{
    // Centre the mesh on the player XZ so clouds always cover the horizon.
    vec3 worldPos = vec3(aPosition.x + playerPos.x,
                         cloudPlaneY,
                         aPosition.z + playerPos.z);

    vec4 viewPos = view * vec4(worldPos, 1.0);
    gl_Position  = projection * viewPos;

    // Use XZ distance from player so fog matches the world shader (horizon-based, not 3D).
    vec2 xzOffset = worldPos.xz - playerPos.xz;
    vFragDist     = length(xzOffset);

    // Derive UVs from world XZ so the texture tiles naturally across the plane.
    // uvScrollU shifts the U axis over time for cloud movement.
    vTexCoord = vec2((worldPos.x + uvScrollU) / CLOUD_TILE_SIZE,
                      worldPos.z               / CLOUD_TILE_SIZE);
}
