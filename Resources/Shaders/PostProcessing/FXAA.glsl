#shader vertex
#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 texCoord;

void main()
{
    texCoord = aTexCoord;
    gl_Position = vec4(aPosition.xy, 0.0, 1.0);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;

uniform sampler2D frameBufferTexture;
uniform vec2 texel;

uniform float EdgeThreshold = 0.166;
uniform float EdgeThresholdMin = 0.0833;
uniform float Subpixel = 0.3;

#define SPAN_MAX 8.0
#define REDUCE_MIN (1.0/128.0)
#define REDUCE_MUL (1.0/32.0)

// The frame is in linear color, so luma is brought back to a perceptual scale for edge detection
float luma(vec3 color)
{
    return sqrt(dot(color, vec3(0.299, 0.587, 0.114)));
}

void main()
{

    vec3 rgbCC = texture(frameBufferTexture, texCoord).rgb;
    float lumaCC = luma(rgbCC);

    vec3 rgbNW = texture(frameBufferTexture, texCoord + vec2(-1.0, -1.0) * texel).rgb;
    vec3 rgbNE = texture(frameBufferTexture, texCoord + vec2( 1.0, -1.0) * texel).rgb;
    vec3 rgbSW = texture(frameBufferTexture, texCoord + vec2(-1.0,  1.0) * texel).rgb;
    vec3 rgbSE = texture(frameBufferTexture, texCoord + vec2( 1.0,  1.0) * texel).rgb;

    vec3 rgbN  = texture(frameBufferTexture, texCoord + vec2(0.0, -1.0) * texel).rgb;
    vec3 rgbS  = texture(frameBufferTexture, texCoord + vec2(0.0,  1.0) * texel).rgb;
    vec3 rgbW  = texture(frameBufferTexture, texCoord + vec2(-1.0, 0.0) * texel).rgb;
    vec3 rgbE  = texture(frameBufferTexture, texCoord + vec2( 1.0, 0.0) * texel).rgb;

    float lumaNW = luma(rgbNW);
    float lumaNE = luma(rgbNE);
    float lumaSW = luma(rgbSW);
    float lumaSE = luma(rgbSE);
    float lumaN  = luma(rgbN);
    float lumaS  = luma(rgbS);
    float lumaW  = luma(rgbW);
    float lumaE  = luma(rgbE);

    float lumaMin = min(lumaCC, min(min(lumaNW,lumaNE), min(min(lumaSW,lumaSE), min(min(lumaN,lumaS), min(lumaW,lumaE)))));
    float lumaMax = max(lumaCC, max(max(lumaNW,lumaNE), max(max(lumaSW,lumaSE), max(max(lumaN,lumaS), max(lumaW,lumaE)))));

    // Early exit for low-contrast areas
    if(lumaMax - lumaMin < max(EdgeThresholdMin, lumaMax * EdgeThreshold)) {
        FragColor = vec4(rgbCC, 1.0);
        return;
    }

    // Edge gradient
    vec2 dir;
    dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
    dir.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));

    // Reduce length for weak edges
    float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * (0.25 * REDUCE_MUL), REDUCE_MIN);
    float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
    dir = min(vec2(SPAN_MAX, SPAN_MAX), max(vec2(-SPAN_MAX, -SPAN_MAX), dir * rcpDirMin)) * texel;

    vec3 rgbA = 0.5 * (
        texture(frameBufferTexture, texCoord + dir * (1.0 / 3.0 * Subpixel)).rgb +
        texture(frameBufferTexture, texCoord - dir * (1.0 / 3.0 * Subpixel)).rgb
    );
    vec3 rgbB = 0.5 * rgbA + 0.25 * (
        texture(frameBufferTexture, texCoord + dir * (0.5 * Subpixel)).rgb +
        texture(frameBufferTexture, texCoord - dir * (0.5 * Subpixel)).rgb
    );

    float lumaB = luma(rgbB);
    FragColor = vec4((lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB, 1.0);
}