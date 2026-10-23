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

uniform vec4 colorFilter;
uniform float hueShift;
uniform float saturation;
uniform float brightness;
uniform float contrast;
uniform float temperature;
uniform sampler2D frameBufferTexture;

vec3 rgb2hsv(vec3 c)
{
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

vec3 hsv2rgb(vec3 c)
{
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

vec4 applyColorFilter(vec4 color)
{
    return vec4(colorFilter.rgb * color.rgb, color.a);
}

vec4 applyHueShift(vec4 color) {
    float angle = (hueShift - 2) * 3.1415926538;
    const vec3 k = vec3(0.57735, 0.57735, 0.57735);
    float cosAngle = cos(angle);
    return vec4(color.rgb * cosAngle + cross(k, color.rgb) * sin(angle) + k * dot(k, color.rgb) * (1.0 - cosAngle), color.a);
}

vec4 applySaturation(vec4 color)
{
    vec3 hsv = rgb2hsv(color.rgb);
    hsv.y *= max(saturation + 1, 0);
    return vec4(hsv2rgb(hsv.rgb), color.a);
}

vec4 applyBrightness(vec4 color)
{
    return vec4(max(0, color.r + brightness),
        max(0, color.g + brightness),
        max(0, color.b + brightness),
        color.a);
}

vec4 applyContrast(vec4 color)
{
    return vec4(((color.rgb - 0.5f) * max(contrast + 1, 0)) + 0.5f, color.a);
}

vec4 applyTemperature(vec4 color)
{
    color.r += temperature * 0.08;
    color.b -= temperature * 0.08;
    return color;
}

void main()
{
    vec4 res = texture(frameBufferTexture, texCoord);
    // Grading works in sRGB so brightness and contrast act around perceptual mid grey
    res.rgb = pow(max(res.rgb, 0.0), vec3(1.0 / 2.2));
    res = applyBrightness(res);
    res = applyContrast(res);
    res = applySaturation(res);
    res = applyTemperature(res);
    res = applyHueShift(res);
    res.rgb = pow(max(res.rgb, 0.0), vec3(2.2));
    res = applyColorFilter(res);
    FragColor = res;
}