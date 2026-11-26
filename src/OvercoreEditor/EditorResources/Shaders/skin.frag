#version 330 core
#define MAX_POINT_LIGHTS 4
#define MAX_SPOT_LIGHTS 4

in vec4 fsin_Color;
in vec2 fsin_TexCoord;
in vec3 FragPos;
in vec3 Normal;

struct Material {
    sampler2D texture_diffuse;
    float shininess;
    float specularIntensity;
};

struct DirLight {
    vec3 direction;
    vec3 color;
    float intensity;
    float ambientStrength;
};

struct PointLight {
    vec3 position;
    vec3 color;
    float intensity;
    float range;
};

struct SpotLight {
    vec3 position;
    vec3 direction;
    vec3 color;
    float intensity;
    float range;
    float innerCutOff;
    float outerCutOff;
};

out vec4 FragColor;

// --- Uniforms ---
uniform vec3 uViewPos;
uniform vec3 uEmissive;

uniform Material uMaterial;
uniform DirLight uDirLight;
uniform bool uHasDirLight;

uniform PointLight uPointLights[MAX_POINT_LIGHTS];
uniform int uNumPointLights;

uniform SpotLight uSpotLights[MAX_SPOT_LIGHTS];
uniform int uNumSpotLights;


// --- Function Declarations ---
vec3 CalcDirLight(DirLight light, vec3 normal, vec3 viewDir, vec3 albedo);
vec3 CalcPointLight(PointLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo);
vec3 CalcSpotLight(SpotLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo);

void main()
{
    vec3 albedo = texture(uMaterial.texture_diffuse, fsin_TexCoord).rgb * fsin_Color.rgb;
    vec3 norm = normalize(Normal);
    vec3 viewDir = normalize(uViewPos - FragPos);

    // Start with the ambient term, which acts as a base light level.
    vec3 finalColor = vec3(0.0);
    if(uHasDirLight) {
        finalColor = uDirLight.ambientStrength * uDirLight.color * uDirLight.intensity * albedo;
    }

    // Additively blend the diffuse and specular contributions of each light.
    if(uHasDirLight) {
        finalColor += CalcDirLight(uDirLight, norm, viewDir, albedo);
    }

    for(int i = 0; i < uNumPointLights; i++) {
        finalColor += CalcPointLight(uPointLights[i], norm, FragPos, viewDir, albedo);
    }

    for(int i = 0; i < uNumSpotLights; i++) {
        finalColor += CalcSpotLight(uSpotLights[i], norm, FragPos, viewDir, albedo);
    }

    // If there are no lights at all, render with the base texture color.
    if (!uHasDirLight && uNumPointLights == 0 && uNumSpotLights == 0) {
        finalColor = albedo;
    }

    // Finally, add any emissive color.
    finalColor += uEmissive;

    FragColor = vec4(finalColor, texture(uMaterial.texture_diffuse, fsin_TexCoord).a * fsin_Color.a);
}

// Calculates only the diffuse and specular parts of a directional light.
vec3 CalcDirLight(DirLight light, vec3 normal, vec3 viewDir, vec3 albedo) {
    vec3 lightDir = normalize(light.direction);
    
    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * light.color * light.intensity;

    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), uMaterial.shininess);
    vec3 specular = uMaterial.specularIntensity * spec * light.color * light.intensity;

    return (diffuse * albedo) + specular;
}

// Calculates the diffuse and specular parts of a point light, including attenuation.
vec3 CalcPointLight(PointLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo) {
    vec3 lightDir = normalize(light.position - fragPos);

    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * light.color;

    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), uMaterial.shininess);
    vec3 specular = uMaterial.specularIntensity * spec * light.color;

    // Attenuation based on range
    float distance = length(light.position - fragPos);
    // Use a squared falloff for a more natural look (bright center, fades quickly at the edge)
    float falloff = pow(clamp(1.0 - (distance / light.range), 0.0, 1.0), 2.0);
    
    vec3 lightContribution = (diffuse * albedo + specular) * light.intensity * falloff;

    return lightContribution;
}

// Calculates the diffuse and specular parts of a spot light.
vec3 CalcSpotLight(SpotLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo) {
    vec3 lightDir = normalize(light.position - fragPos);

    // Spotlight intensity (cone)
    float theta = dot(lightDir, normalize(-light.direction));
    float epsilon = light.innerCutOff - light.outerCutOff;
    float spotIntensity = clamp((theta - light.outerCutOff) / epsilon, 0.0, 1.0);
    if (spotIntensity <= 0.0) {
        return vec3(0.0);
    }

    // Attenuation (range)
    float distance = length(light.position - fragPos);
    float falloff = pow(clamp(1.0 - (distance / light.range), 0.0, 1.0), 2.0);
    if (falloff <= 0.0) {
        return vec3(0.0);
    }

    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * light.color;

    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), uMaterial.shininess);
    vec3 specular = uMaterial.specularIntensity * spec * light.color;

    vec3 lightContribution = (diffuse * albedo + specular) * light.intensity * falloff * spotIntensity;

    return lightContribution;
}