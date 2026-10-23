#shader vertex
#version 330 core
layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 texCoord;
out vec4 texColor;
out vec4 position;
out mat4 nModel;
    
uniform vec4 mainColor;

uniform mat4 projection;
uniform mat4 model;

void main() 
{
    texCoord = aTexCoord;
    texColor = mainColor;
    gl_Position = projection * model * vec4(aPosition.xy, 0.0, 1.0);
    nModel = model;

    position = model * vec4(aPosition.xy, 0.0, 1.0);
}

#shader fragment
#version 330 core
out vec4 FragColor;

in vec2 texCoord;
in vec4 texColor;
in vec4 position;
in mat4 nModel;

// Global Uniforms
uniform float time = 0.0;
uniform vec4 ambientColor = vec4(0.0);
uniform sampler2D mainTextureSampler;
uniform sampler2D normalTextureSampler;
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

#define DISTANCE_REF 512
#define MAX_POINT_LIGHTS 16
uniform PointLight pointLights[MAX_POINT_LIGHTS];

vec3 calcPointLight(PointLight light, vec2 fragPos, vec3 normal)
{
    if (light.initialized < 0.5) return vec3(0);
    vec3 lightPos = vec3(light.position, light.height);
    vec3 pos = vec3(fragPos, 0);
    vec3 lightDir = normalize(lightPos - pos);
    float diff = max(dot(normal, lightDir), 0.0);
    float distance = length(lightPos - pos);
    float attenuation = clamp(1.0 - distance * distance / (light.quadratic * light.quadratic), 0.0, 1.0);
    attenuation *= attenuation;
    return light.color * light.intensity * diff * attenuation;
} 

void main() 
{
    // Getting object color from texture
    vec4 objectColor = texture(mainTextureSampler, texCoord) * texColor;
    if (objectColor.a == 0.0)
    {
        FragColor = vec4(0);
        return;
    }

    // Getting normal from texture
    vec3 normal = vec3(texture(normalTextureSampler, texCoord));
    normal = normalize((normal * 2.0) - 1.0);

    // Transposing normal using model matrix
    normal = normalize(vec3(normal.x * normalScale, normal.y * normalScale, normal.z));
    vec3 normalTransposed = normalize(mat3(transpose(inverse(nModel))) * normal);

    // Setting result to ambient color
    vec3 result = vec3(ambientColor);

    // Looping through light array to add lighting
    for(int i = 0; i < MAX_POINT_LIGHTS; i++)
    {
        result += calcPointLight(pointLights[i], position.xy, normalTransposed);
    }

    // Multiplying the unlit object color with the light color
    vec3 res = objectColor.rgb * result;
    FragColor = vec4(res.rgb, objectColor.a);
}