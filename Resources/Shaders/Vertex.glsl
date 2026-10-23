#shader vertex
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec4 aColor;

out vec4 vertexColor;

uniform mat4 projection;
uniform mat4 model;

void main()
{
    vertexColor = vec4(pow(aColor.rgb, vec3(2.2)), aColor.a);
    gl_Position = projection * model * vec4(aPosition.xy, 0.0, 1.0);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec4 vertexColor;

void main()
{
    FragColor = vertexColor;
}
