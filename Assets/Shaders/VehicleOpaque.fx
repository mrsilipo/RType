#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 World;
float4x4 WorldViewProjection;
float3 CameraPosition;

float3 BaseColor;
float Metallic;
float Roughness;
float SpecularStrength;
float ReflectionStrength;
float FresnelStrength;
float3 EmissiveColor;
float EmissiveStrength;

float3 AmbientLightColor;
float3 LightDirection0;
float3 LightColor0;
float3 LightSpecularColor0;
float3 LightDirection1;
float3 LightColor1;
float3 LightSpecularColor1;

float3 FogColor;
float FogStart;
float FogEnd;

Texture2D Texture0;

sampler2D TextureSampler = sampler_state
{
    Texture = <Texture0>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : POSITION0;
    float3 WorldPosition : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
    float2 TexCoord : TEXCOORD2;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(input.Position, WorldViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = input.TexCoord;
    return output;
}

float3 CalculateLight(
    float3 normal,
    float3 viewDirection,
    float3 lightDirection,
    float3 lightColor,
    float3 specularColor,
    float glossPower,
    float3 specularTint,
    float specularStrength)
{
    float3 lightVector = normalize(-lightDirection);
    float diffuse = saturate(dot(normal, lightVector));
    float3 halfVector = normalize(lightVector + viewDirection);
    float specular = pow(saturate(dot(normal, halfVector)), glossPower) * specularStrength;
    return lightColor * diffuse + specularColor * specularTint * specular;
}

float3 CalculateReflection(float3 normal, float3 viewDirection, float fresnel)
{
    float3 reflectionVector = reflect(-viewDirection, normal);
    float skyBlend = saturate(reflectionVector.y * 0.5 + 0.5);
    float3 skyColor = lerp(float3(0.18, 0.20, 0.22), float3(0.88, 0.93, 1.0), skyBlend);
    float band = pow(saturate(1.0 - abs(frac(reflectionVector.x * 3.2 + reflectionVector.y * 1.8) - 0.5) * 3.6), 4.0);
    float3 showroomBand = float3(0.92, 0.96, 1.0) * band;
    return (skyColor * 0.18 + showroomBand * 0.55) * (0.22 + fresnel * 0.78);
}

float4 MainPS(PixelShaderInput input) : COLOR0
{
    float3 normal = normalize(input.WorldNormal);
    float3 viewDirection = normalize(CameraPosition - input.WorldPosition);
    float3 textureColor = tex2D(TextureSampler, input.TexCoord).rgb;
    float3 albedo = saturate(textureColor * BaseColor);

    float roughness = saturate(Roughness);
    float gloss = 1.0 - roughness;
    float glossPower = lerp(12.0, 160.0, gloss * gloss);
    float facing = saturate(dot(normal, viewDirection));
    float fresnel = pow(1.0 - facing, 5.0) * FresnelStrength;
    float3 specularTint = lerp(float3(1.0, 1.0, 1.0), BaseColor, Metallic * 0.55);

    float diffuseRetention = lerp(1.0, 0.42, Metallic);
    float3 lighting = AmbientLightColor +
        CalculateLight(normal, viewDirection, LightDirection0, LightColor0, LightSpecularColor0, glossPower, specularTint, SpecularStrength) +
        CalculateLight(normal, viewDirection, LightDirection1, LightColor1, LightSpecularColor1, glossPower, specularTint, SpecularStrength * 0.72);

    float3 reflection = CalculateReflection(normal, viewDirection, fresnel) * ReflectionStrength;
    float3 color = albedo * lighting * diffuseRetention + reflection + EmissiveColor * EmissiveStrength;

    float fogAmount = saturate((distance(CameraPosition, input.WorldPosition) - FogStart) / max(0.001, FogEnd - FogStart));
    color = lerp(color, FogColor, fogAmount);
    return float4(saturate(color), 1.0);
}

technique VehicleOpaque
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
