#version 330 core
in float vAlpha;

out vec4 FragColor;

void main() {
    FragColor = vec4(0.1, 0.1, 0.1, vAlpha * 0.6);
}
