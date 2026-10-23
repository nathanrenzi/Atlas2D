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

uniform sampler2D frameBufferTexture;

void main()
{
    vec3 color = clamp(texture(frameBufferTexture, texCoord).rgb, 0.0, 1.0);
    // Inverts in sRGB so mid grey stays mid grey, then converts back to linear
    vec3 inverted = 1.0 - pow(color, vec3(1.0 / 2.2));
    FragColor = vec4(pow(inverted, vec3(2.2)), 1.0);
}