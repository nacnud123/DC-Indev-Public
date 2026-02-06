#version 330 core
in vec2 texCoord;

uniform sampler2D blockTexture;

out vec4 FragColor;

void main() {
    vec4 texColor = texture(blockTexture, texCoord);
    if (texColor.a < 0.1) discard;
    FragColor = texColor;
}
