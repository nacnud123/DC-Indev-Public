#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUV;

layout(location = 2) in mat4 instanceModel;
layout(location = 6) in vec4 instanceUV;

uniform mat4 view;
uniform mat4 projection;

out vec2 texCoord;

void main() {
    vec3 cameraRight = vec3(view[0][0], view[1][0], view[2][0]);
    vec3 cameraUp = vec3(view[0][1], view[1][1], view[2][1]);

    vec3 worldPos = vec3(instanceModel[3]);
    float scale = length(vec3(instanceModel[0]));

    vec3 vertexPos = worldPos
        + cameraRight * aPos.x * scale
        + cameraUp * aPos.y * scale;

    gl_Position = projection * view * vec4(vertexPos, 1.0);
    texCoord = instanceUV.xy + (aUV * instanceUV.zw);
}

/*
// cubes
void main_cube() {
    gl_Position = projection * view * instanceModel * vec4(aPos, 1.0);
    texCoord = instanceUV.xy + (aUV * instanceUV.zw);
}
*/
