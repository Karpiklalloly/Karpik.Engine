#version 450

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 fragColor;

layout(set = 0, binding = 0) uniform texture2D Tex;
layout(set = 0, binding = 1) uniform sampler Samp;

float Median(float r, float g, float b)
{
    return max(min(r, g), min(max(r, g), b));
}

void main()
{
    vec3 sampleColor = texture(sampler2D(Tex, Samp), vTexCoord).rgb;
    float signedDistance = Median(sampleColor.r, sampleColor.g, sampleColor.b);
    float width = fwidth(signedDistance);
    float alpha = smoothstep(0.5 - width, 0.5 + width, signedDistance);
    fragColor = vec4(vColor.rgb, vColor.a * alpha);
}
