#version 330 core

layout(location = 0) in vec3 aPosition;    // position in celestial local space
layout(location = 1) in vec2 aTexCoord;    // UV for sun/moon texture

out vec2 vTexCoord;

// Combined projection * view * translate(playerPos) * rotateX(celestialAngle)
// (see STEP 10 for how to compute this in C#)
uniform mat4 celestialMVP;

void main()
{
    gl_Position = celestialMVP * vec4(aPosition, 1.0);
    vTexCoord = aTexCoord;
}
