#version 330 core

in float vBrightness;
out vec4 FragColor;

void main()
{
    // Stars are plain white quads tinted by brightness.
    FragColor = vec4(vBrightness, vBrightness, vBrightness, vBrightness);
}
