#version 330 core

in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D celestialTex;
uniform float brightness;    // 0.0 at night, 1.0 at noon (use dayFactor for sun)

void main()
{
    vec4 color = texture(celestialTex, vTexCoord);

    // Discard transparent pixels (sun/moon textures have transparent backgrounds)
    if (color.a < 0.05)
        discard;

    // Scale by brightness — sun brightens at noon, dims at night
    FragColor = vec4(color.rgb * brightness, color.a);
}
