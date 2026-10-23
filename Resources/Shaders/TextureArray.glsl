#shader vertex
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in float aLayer;

out vec2 texCoord;
flat out float layer;

uniform mat4 projection;
uniform mat4 model;

void main()
{
    texCoord = aTexCoord;
    layer = aLayer;
    gl_Position = projection * model * vec4(aPosition.xy, 0.0, 1.0);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;
flat in float layer;

uniform vec4 mainColor;
uniform sampler2DArray mainTextureSampler;

void main()
{
    FragColor = texture(mainTextureSampler, vec3(texCoord, layer)) * mainColor;
}