#version 330 core
in vec2 uv;
in vec3 norm;
uniform sampler2D tex;
out vec4 frag;
void main() {
    vec4 texColor = texture(tex, uv);
    if (texColor.a < 0.1) discard;
    vec3 light = normalize(vec3(-0.5, -1.0, -0.3));
    float diff = max(dot(norm, -light), 0.0) * 0.6;
    frag = vec4(texColor.rgb * (0.4 + diff), texColor.a);
}