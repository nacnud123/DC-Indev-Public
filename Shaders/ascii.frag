#version 330 core

in vec2 texCoord;

out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uAsciiAtlas;
uniform vec2 uScreenSize;
uniform vec2 uCharSize;
uniform int  uCharCount;

void main()
{
    vec2 cellIndex    = floor(texCoord * uScreenSize / uCharSize);
    vec2 cellOriginUV = cellIndex * uCharSize / uScreenSize;
    vec2 cellSizeUV   = uCharSize / uScreenSize;

    vec3 avgColor = vec3(0.0);
    avgColor += texture(uScene, cellOriginUV + vec2(0.25, 0.25) * cellSizeUV).rgb;
    avgColor += texture(uScene, cellOriginUV + vec2(0.75, 0.25) * cellSizeUV).rgb;
    avgColor += texture(uScene, cellOriginUV + vec2(0.25, 0.75) * cellSizeUV).rgb;
    avgColor += texture(uScene, cellOriginUV + vec2(0.75, 0.75) * cellSizeUV).rgb;
    avgColor *= 0.25;

    float lum = dot(avgColor, vec3(0.299, 0.587, 0.114));
    lum = pow(lum, 0.65);
    lum = clamp(lum, 0.0, 1.0);

    int charIdx = clamp(int(lum * float(uCharCount)), 0, uCharCount - 1);

    vec2 withinCell = fract(texCoord * uScreenSize / uCharSize);
    float charW  = 1.0 / float(uCharCount);
    vec2 atlasUV = vec2((float(charIdx) + withinCell.x) * charW, withinCell.y);
    float glyph  = texture(uAsciiAtlas, atlasUV).r;

    vec3 fg = clamp(avgColor * 1.05, 0.0, 1.0);
    vec3 bg = avgColor * 0.28;

    FragColor = vec4(mix(bg, fg, glyph), 1.0);
}
