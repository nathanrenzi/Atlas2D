#shader vertex
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 texCoord;

uniform mat4 projection;
uniform mat4 uiMatrix;
uniform mat4 model;

void main()
{
    texCoord = aTexCoord;
    gl_Position = projection * uiMatrix * model * vec4(aPosition.xy, 0.0, 1.0);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;

uniform vec4 mainColor;
uniform sampler2D mainTextureSampler;

void main()
{
    FragColor = vec4(mainColor.rgb, texture(mainTextureSampler, texCoord).r * mainColor.a);
}
