#version 330 core

in vec2 uv;
in float shade;

out vec4 FragColor;

uniform sampler2D tex;
uniform float lightLevel;

void main() {
    vec4 c = texture(tex, uv);
    
    if (c.a < 0.5)
        discard;
    
    FragColor = vec4(c.rgb * shade * lightLevel, c.a);
}
