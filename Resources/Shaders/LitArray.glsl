#shader vertex
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in float aLayer;

out vec2 texCoord;
out vec2 worldPosition;
out mat3 normalMatrix;
flat out float layer;

uniform mat4 projection;
uniform mat4 model;

void main()
{
    texCoord = aTexCoord;
    layer = aLayer;
    vec4 world = model * vec4(aPosition.xy, 0.0, 1.0);
    worldPosition = world.xy;
    normalMatrix = mat3(transpose(inverse(model)));
    gl_Position = projection * world;
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;
in vec2 worldPosition;
in mat3 normalMatrix;
flat in float layer;

uniform vec4 mainColor;
uniform vec4 ambientColor = vec4(0.0);
uniform sampler2DArray mainTextureSampler;
uniform sampler2DArray normalTextureSampler;
uniform bool hasNormalTexture = false;
uniform float normalScale = 1.0;

struct PointLight {
    float initialized;
    vec2 position;
    float height;
    float quadratic;
    float constant;
    float intensity;
    vec3 color;
};

#define MAX_POINT_LIGHTS 16
uniform PointLight pointLights[MAX_POINT_LIGHTS];

vec3 calcPointLight(PointLight light, vec2 fragPos, vec3 normal)
{
    if (light.initialized < 0.5) return vec3(0);
    vec3 toLight = vec3(light.position, light.height) - vec3(fragPos, 0.0);
    float diff = max(dot(normal, normalize(toLight)), 0.0);
    float distance = length(toLight);
    float attenuation = clamp(1.0 - distance * distance / (light.quadratic * light.quadratic), 0.0, 1.0);
    attenuation *= attenuation;
    return light.color * light.intensity * diff * attenuation;
}

void main()
{
    vec3 uvw = vec3(texCoord, layer);
    vec4 objectColor = texture(mainTextureSampler, uvw) * mainColor;
    if (objectColor.a == 0.0)
    {
        FragColor = vec4(0);
        return;
    }

    vec3 normal = vec3(0.0, 0.0, 1.0);
    if (hasNormalTexture)
    {
        normal = normalize(texture(normalTextureSampler, uvw).rgb * 2.0 - 1.0);
        normal = normalize(vec3(normal.xy * normalScale, normal.z));
    }
    normal = normalize(normalMatrix * normal);

    vec3 light = ambientColor.rgb;
    for (int i = 0; i < MAX_POINT_LIGHTS; i++)
    {
        light += calcPointLight(pointLights[i], worldPosition, normal);
    }

    FragColor = vec4(objectColor.rgb * light, objectColor.a);
}
