#version 330 core

in float vBrightness;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vBrightness, vBrightness, vBrightness, vBrightness);
}
