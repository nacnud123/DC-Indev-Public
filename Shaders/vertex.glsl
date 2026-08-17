#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec2 aTexCoord;
// 1 on flowing water/lava, 0 on everything else. Flat because it is a per-face constant and
// interpolating it would leave a half-animated band along any edge between still and flowing.
layout (location = 4) in float aFluidAnim;

out vec3 fragColor;
out vec3 fragNormal;
out vec2 texCoord;
out float fragDist;
flat out float fragFluidAnim;
// View-space normal/position, only used for the fluid fresnel rim in fragment.glsl - view space
// because the camera sits at the origin there, so "view direction" is just -fragViewPos.
out vec3 fragViewNormal;
out vec3 fragViewPos;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
// Small world-space push along the normal, toward the camera-facing side, used only during the
// transparent pass to keep water from z-fighting with a flush shoreline block. 0 during the opaque
// pass. Deliberately NOT GL_POLYGON_OFFSET: that biases in depth-buffer units, which - thanks to the
// non-linear depth buffer - correspond to a growing world-space gap with distance, letting a fixed
// "-1 unit" bias punch water through solid terrain dozens of blocks away. A world-space nudge stays
// the same tiny fraction of a block no matter how far away the face is.
uniform float depthNudge;

void main()
{
    vec3 nudgedPosition = aPosition + aNormal * depthNudge;
    vec4 viewPos = view * model * vec4(nudgedPosition, 1.0);
    gl_Position = projection * viewPos;
    fragColor = aColor;
    fragNormal = aNormal;
    texCoord = aTexCoord;
    fragDist = length(viewPos.xyz);
    fragFluidAnim = aFluidAnim;
    fragViewNormal = mat3(view * model) * aNormal;
    fragViewPos = viewPos.xyz;
}
