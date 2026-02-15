#version 330 core
in vec2 uv;
in vec3 norm;
uniform sampler2D tex;
uniform vec3 lightDir;
uniform float ambientStrength;
uniform float sunlightLevel;
uniform float skyLight;
uniform float blockLight;
uniform float uHitFlash;

out vec4 frag;
void main() {
    vec4 texColor = texture(tex, uv);
    if (texColor.a < 0.1) discard;

    vec3 n = normalize(norm);
    float diff = max(dot(n, -lightDir), 0.0) * 0.6;
    vec3 sun = vec3(ambientStrength + diff);

    float skyBright = (0.1 + skyLight * 0.9) * sunlightLevel;
    vec3 skyContrib = sun * skyBright;

    float blockBright = 0.1 + blockLight * 0.9;
    vec3 blockContrib = vec3(blockBright);

    vec3 lighting = max(skyContrib, blockContrib);
    frag = vec4(texColor.rgb * lighting, texColor.a);

    frag = mix(frag, vec4(1.0), uHitFlash);
}
