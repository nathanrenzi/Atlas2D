#shader vertex
#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 texCoord;

void main()
{
    texCoord = aTexCoord;
    gl_Position = vec4(aPosition.xy, 0.0f, 1.0f);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;

uniform float intensity;
uniform float redOffset;
uniform float greenOffset;
uniform float blueOffset;
uniform sampler2D frameBufferTexture;

void main()
{                 
    vec2 position = texCoord - vec2(0.5);        
    float distance = length(position);
    float pixelIntensity = distance / 0.5 * intensity;

    vec2 rUv = position * (1 + redOffset * pixelIntensity) + vec2(0.5);
    float r = texture(frameBufferTexture, rUv).r;

    vec2 gUv = position * (1 + greenOffset * pixelIntensity) + vec2(0.5);
    float g = texture(frameBufferTexture, gUv).g;

    vec2 bUv = position * (1 + blueOffset * pixelIntensity) + vec2(0.5);
    float b = texture(frameBufferTexture, bUv).b;

    FragColor = vec4(r, g, b, 1.0f);
}
