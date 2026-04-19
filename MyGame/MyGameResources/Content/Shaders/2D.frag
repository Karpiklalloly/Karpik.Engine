#version 450

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 fragColor;

layout(set = 0, binding = 0) uniform texture2D Tex;
layout(set = 0, binding = 1) uniform sampler Samp;

void main()
{
    vec4 texColor = texture(sampler2D(Tex, Samp), vTexCoord);
    fragColor = texColor * vColor;
    fragColor = vColor;
}