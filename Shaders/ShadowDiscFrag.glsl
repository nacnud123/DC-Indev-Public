#version 330 core

in float vAlpha;

out vec4 frag;

void main()
{
    if (vAlpha < 0.01) 
        discard;

    frag = vec4(0.0, 0.0, 0.0, vAlpha);
}
