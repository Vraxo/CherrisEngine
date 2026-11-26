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

// ---------------------------------------------------------------- shadows
//
// One depth texture with a layer per cascade, not four textures: GLSL 330 indexes a sampler array only by a constant
// expression, so a run-time cascade index can only be a texture layer. The comparison is done here rather than through
// sampler2DArrayShadow, because the bias has to be applied before the filter and a PCF tent has to be built around it.
uniform sampler2DArray uShadowMap;
uniform mat4 uShadowMatrices[4];
uniform vec4 uCascadeSplits;
uniform int uCascadeCount;
uniform float uShadowDistance;
uniform float uShadowNearPlane;
uniform float uShadowBias;
uniform float uShadowNormalBias;
uniform float uShadowStrength;
uniform float uShadowSoftness;
uniform float uShadowBlendBand;
uniform vec4 uShadowTexelSize;
uniform int uReceiveShadows;
uniform vec3 uCameraForward;


// --- Uniforms ---
uniform vec3 uViewPos;
uniform vec3 uEmissive;

uniform Material uMaterial;

// The cutout test's threshold, beside the rest of the material's uniforms rather than in the shadow block: it belongs
// to the surface, and the depth pass reads the same value for the same material.
uniform float uAlphaCutoff;

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

// How much of the directional light reaches this fragment: 1 is fully lit, 0 is fully shadowed.
//
// Stated in terms of what it is not: it does not touch the ambient, emissive or skybox terms, and it multiplies the
// directional light alone. A shadow that dimmed the ambient would be a scene that goes black in a corner rather than
// one that is in shade.
float CascadeVisibility(int cascade, vec3 worldPos, vec3 normal, float ndotl)
{
    vec4 lightSpace = uShadowMatrices[cascade] * vec4(worldPos, 1.0);
    vec3 projected = lightSpace.xyz / lightSpace.w;

    // The depth the depth buffer holds for this fragment, in the space it is actually stored in.
    //
    // System.Numerics.CreateOrthographicOffCenter emits NDC depth in [0, 1] — it is the Direct3D convention — and
    // OpenGL's viewport transform then maps that to [(0 + 1) / 2, (1 + 1) / 2]. So a caster's stored depth spans
    // [0.5, 1], and comparing it against the raw NDC value is comparing two different encodings: for a floor at 60 m
    // behind a caster at 30 m the raw comparison reports "lit" and no shadow appears anywhere, on any object, ever.
    // Remapping here rather than in the matrix keeps the matrix in one convention, which is what makes the fit's tests
    // meaningful — they assert the box's extents and the snapping, not OpenGL's clip convention.
    float storedDepth = (projected.z + 1.0) * 0.5;

    // Outside the box in x or y: nothing was drawn here, so nothing occludes. z above 1 is past the light's far plane,
    // where likewise nothing was drawn.
    if (projected.x < 0.0 || projected.x > 1.0 || projected.y < 0.0 || projected.y > 1.0 || storedDepth > 1.0)
    {
        return 1.0;
    }

    // Slope-scaled bias: grows toward a grazing angle, where one texel spans more depth, and is clamped because past a
    // point a floor seen edge-on flickers between lit and unlit. The clamp is tan(acos(0.1)) worth of slope.
    float slope = min(tan(acos(clamp(ndotl, 0.0, 1.0))), 6.0);
    float bias = uShadowBias * max(slope, 0.2);

    vec3 coord = vec3(projected.xy, float(cascade));
    float texel = uShadowSoftness;

    if (texel <= 0.0)
    {
        return step(storedDepth - bias, texture(uShadowMap, coord).r);
    }

    // A 3x3 tent, weighted [1 2 1] per axis. Separable in weight, not in taps: nine fetches, and the shape is what
    // makes an edge read as soft rather than noisy.
    ivec2 size = textureSize(uShadowMap, 0).xy;
    vec2 step_uv = vec2(texel) / vec2(size);

    float sum = 0.0;
    float total = 0.0;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float weight = (x == 0 ? 2.0 : 1.0) * (y == 0 ? 2.0 : 1.0);
            float depth = texture(uShadowMap, coord + vec3(vec2(x, y) * step_uv, 0.0)).r;

            sum += weight * step(storedDepth - bias, depth);
            total += weight;
        }
    }

    return sum / total;
}

// Which cascade a fragment belongs to, by view depth. Not by gl_FragCoord.z: clip-space depth is non-linear in a
// perspective projection, so comparing it against an orthographic shadow map's linear depth is meaningless.
float ShadowFactor(vec3 worldPos, vec3 normal)
{
    if (uReceiveShadows == 0 || uCascadeCount == 0)
    {
        return 1.0;
    }

    // The eye's position is already a uniform, and the forward axis is what turns it into a view depth.
    float depth = dot(worldPos - uViewPos, uCameraForward);

    // Nearer than the light's own near plane, or past the last cascade: nothing to compare against. The first
    // condition is a per-light setting and the second is the cascade schedule's, so both are honoured rather than
    // either — a light with a large near plane is asked not to shadow what is right under the eye.
    if (depth < uShadowNearPlane || depth > uShadowDistance)
    {
        return 1.0;
    }

    float ndotl = max(dot(normal, -normalize(uDirLight.direction)), 0.0);

    // The normal offset: move the sample off the surface along its own normal before projecting it. This is the bias
    // that fixes acne on a curved surface without detaching the shadow at the contact point.
    //
    // Per cascade, because one texel is a different number of metres in each. At the defaults the innermost cascade's
    // texel is 0.0125 m and the outermost's is 0.177 m, so an offset sized for the first is 14× too small in the last
    // and the outer cascade is where acne shows. Indexing by cascade is a constant expression, so the lookup is legal
    // here even though the sampler array would not be.
    float texelWorld = uShadowTexelSize[cascade];

    vec3 offsetPos = worldPos + normal * (uShadowNormalBias * texelWorld);

    int cascade = 0;

    while (cascade < uCascadeCount - 1 && depth > uCascadeSplits[cascade])
    {
        cascade++;
    }

    float visibility = CascadeVisibility(cascade, offsetPos, normal, ndotl);

    // Cross-fade into the next cascade over the last stretch of this one, so the seam where two maps meet is not a
    // line across the one surface every outdoor scene has: a floor running away from the camera.
    float inner = cascade == 0 ? uShadowNearPlane : uCascadeSplits[cascade - 1];
    float outer = uCascadeSplits[cascade];
    float band = (outer - inner) * uShadowBlendBand;

    if (cascade + 1 < uCascadeCount && band > 0.0 && depth > outer - band)
    {
        float blend = smoothstep(outer - band, outer, depth);
        float next = CascadeVisibility(cascade + 1, offsetPos, normal, ndotl);

        visibility = mix(visibility, next, blend);
    }

    // Strength interpolates toward 1 rather than toward 0, so 0 means "no shadow" and 1 means "black".
    return mix(1.0, visibility, uShadowStrength);
}


void main()
{
    vec4 diffuse = texture(uMaterial.texture_diffuse, fsin_TexCoord);

    // The material's alpha cutoff, the same value the depth pass tests: a fence that is cut out here and solid in its
    // shadow is worse than one that is solid in both, because it looks like a bug rather than like missing coverage.
    // 0 disables the test, and `alpha < 0.0` is false for every alpha a texture can hold.
    if (diffuse.a * fsin_Color.a < uAlphaCutoff)
    {
        discard;
    }

    vec3 albedo = diffuse.rgb * fsin_Color.rgb;
    vec3 norm = normalize(Normal);
    vec3 viewDir = normalize(uViewPos - FragPos);

    // Once, before the light loops: the shadow is the directional light's, and computing it inside the loop would be
    // the same nine texture fetches per light per fragment.
    float shadow = ShadowFactor(FragPos, norm);

    // Start with the ambient term, which acts as a base light level. It is deliberately not shadowed: a shadow that
    // dimmed the ambient too would make a corner go black instead of merely shaded.
    vec3 finalColor = vec3(0.0);
    if(uHasDirLight) {
        finalColor = uDirLight.ambientStrength * uDirLight.color * uDirLight.intensity * albedo;
    }

    // Additively blend the diffuse and specular contributions of each light. Only the directional one is shadowed;
    // point and spot lights do not cast yet, which docs/shadows.md §8 says rather than pretends.
    if(uHasDirLight) {
        finalColor += CalcDirLight(uDirLight, norm, viewDir, albedo) * shadow;
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