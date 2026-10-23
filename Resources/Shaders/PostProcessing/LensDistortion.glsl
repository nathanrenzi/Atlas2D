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
uniform vec4 clearColor;
uniform sampler2D frameBufferTexture;

void main()
{                 
    vec2 position = texCoord - vec2(0.5);        
    float distance = length(position);
    float pixelIntensity = distance / 0.5 * -intensity;

    vec2 uv = position * (1 + pixelIntensity) + vec2(0.5);
    vec4 color = texture(frameBufferTexture, uv);
    if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
    {
        color = clearColor;
    }

    FragColor = color;
}
