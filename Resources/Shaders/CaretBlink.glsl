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
uniform float time = 0.0;
uniform float startTime = 0.0;
uniform sampler2D mainTextureSampler;

const float PI = 3.14159265;
const float CARET_BLINK_SPEED = 2.0;
const float CARET_BLINK_SHARPNESS = 0.2;

float getAlpha()
{
    float wave = sin(PI * (time - startTime + 0.2) * CARET_BLINK_SPEED);
    return pow(max(wave, 0.0), CARET_BLINK_SHARPNESS);
}

void main()
{
    vec4 color = texture(mainTextureSampler, texCoord) * mainColor;
    FragColor = vec4(color.rgb, color.a * getAlpha());
}
