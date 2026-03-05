#version 330 core

in float vBrightness;
out vec4 FragColor;

void main()
{
    // Stars are plain white quads tinted by brightness.
    // At vBrightness = 0.0 they are invisible (day).
    // At vBrightness = 0.5 they are half-bright (deepest night).
    FragColor = vec4(vBrightness, vBrightness, vBrightness, vBrightness);
}
