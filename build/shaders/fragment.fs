#version 330 core

#include common.glsl

in Interpolator {
    vec2 uv;
    vec3 normal;
    vec3 worldPos;
    vec4 shadowCoords;
} i;
out vec4 FragColor;

// globals
uniform vec3 _dirlight0_dir;
uniform vec3 _dirlight0_color;
uniform vec3 _pointlight0_pos;
uniform vec3 _pointlight0_color;
uniform vec3 _camPos;

// PBR stuff
uniform samplerCube _irrMap;
uniform samplerCube _prefilterMap;
uniform sampler2D _brdfLUT;

// Shadow stuff
uniform sampler2D _shadowMap0;
uniform samplerCube _shadowCubeMap0;
uniform float _farPlane = 25.0f;

uniform float _smoothness = 0.1f;
uniform float _metallic = 0.0f;
uniform sampler2D _mainTex;
uniform sampler2D _detailTex;

struct Light {
    vec3 color;
    vec3 dir;
};

float CalculateDirShadow(vec4 shadowCoords, vec3 normal, vec3 lightDir) {
    vec3 coords = shadowCoords.xyz / shadowCoords.w;
    coords = coords * 0.5 + 0.5;
    float closeDepth = texture(_shadowMap0, coords.xy).r;
    float currentDepth = coords.z;
    float bias = max(0.05 * (1.0 - dot(normal, lightDir)), 0.005);  
    float shadow = currentDepth - bias > closeDepth ? 1.0 : 0.0;
    return shadow;
}

float CalculatePointShadow(vec3 lightPos) {
    vec3 lightVec = i.worldPos - lightPos;

    float closeDepth = texture(_shadowCubeMap0, lightVec).r;
    closeDepth *= _farPlane;

    float currentDepth = length(lightVec);

    float bias = 0.05; 
    float shadow = currentDepth -  bias > closeDepth ? 1.0 : 0.0;

    return shadow;
}  

vec3 BRDF_PBR(vec3 albedo, vec3 specularTint, float oneMinusReflectivity, float smoothness, vec3 normal, vec3 viewDir, Light light) {
    float roughness = 1 - smoothness;
    vec3 halfVector = normalize(light.dir + viewDir);
    float ndotl = DotClamped(normal, light.dir);
    float ndotv = DotClamped(normal, viewDir);

    // cook-torrance brdf
    float NDF = DistributionGGX(normal, halfVector, roughness);
    float G = GeometrySmith(normal, viewDir, light.dir, roughness);
    vec3 F = FresnelSchlick(DotClamped(halfVector, viewDir), specularTint);

    vec3 top = NDF * G * F;
    float bot = 4.0 * ndotv * ndotl + 0.0001;
    vec3 specular = top / bot;

    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - _metallic;	   

    vec3 color = (kD * albedo / PI + specular) * light.color * ndotl;
    
    return color;
}

Light CreateDirLight(vec3 dir, vec3 color) {
    Light light;
    light.dir = normalize(dir);
    light.color = color;
    return light;
}

Light CreatePointLight(vec3 pos, vec3 color) {
    vec3 lightVec = _pointlight0_pos - i.worldPos;
    float attenuation = 1 / (1 + length(lightVec));
    Light light;
    light.dir = normalize(lightVec);
    light.color = color * attenuation;
    return light;
}

void main() {
    float luminance = texture(_detailTex, i.uv).r;
    vec3 albedo = texture(_mainTex, i.uv).rgb;
    albedo = vec3(0.5, 0.5, 1.0);

    vec3 specularTint;
    float oneMinusReflectivity;
    albedo = DiffuesAndSpecullarFromMetallic(albedo, pow(_metallic, 1.0f / GAMMA), specularTint, oneMinusReflectivity);
    
    float roughness = 1.0 - _smoothness;
    vec3 normal = normalize(i.normal);
    vec3 viewDir = normalize(_camPos - i.worldPos);
    vec3 reflectDir = reflect(-viewDir, normal);

    // Apply light
    Light light = CreatePointLight(_pointlight0_pos, _pointlight0_color);
    vec3 color = BRDF_PBR(albedo, specularTint, oneMinusReflectivity, _smoothness, normal, viewDir, light)
                * (1.0 - CalculatePointShadow(_pointlight0_pos));
    light = CreateDirLight(_dirlight0_dir, _dirlight0_color);
    color += BRDF_PBR(albedo, specularTint, oneMinusReflectivity, _smoothness, normal, viewDir, light)
            * (1.0 - CalculateDirShadow(i.shadowCoords, normal, light.dir));
    
    // Apply ambient lighting from background
    vec3 F = FresnelSchlickRoughness(DotClamped(normal, viewDir), specularTint, roughness);
    vec3 kS = F;
    vec3 kD = 1.0 - kS;
    kD *= 1.0 - _metallic;	  

    vec3 irradiance = texture(_irrMap, normal).rgb;
    vec3 diffuse = albedo * irradiance;
    vec3 prefilteredColor = textureLod(_prefilterMap, reflectDir, roughness * MAX_LOD).rgb;
    vec2 brdf = texture(_brdfLUT, vec2(DotClamped(normal, viewDir), roughness)).rg;
    vec3 specular = prefilteredColor * (F * brdf.x + brdf.y);
    vec3 ambient = (kD * diffuse + specular) * 0.1;
    color += ambient;

    color = pow(color, vec3(1.0 / GAMMA));  
    FragColor = vec4(color, 1.0);
}
