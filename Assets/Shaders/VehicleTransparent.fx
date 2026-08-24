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
float Roughness;
float SpecularStrength;
float ReflectionStrength;
float FresnelStrength;
float Opacity;
float3 EmissiveColor;
float EmissiveStrength;
float LensDetailStrength;

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

float3 CalculateSpecular(
    float3 normal,
    float3 viewDirection,
    float3 lightDirection,
    float3 specularColor,
    float glossPower,
    float strength)
{
    float3 lightVector = normalize(-lightDirection);
    float3 halfVector = normalize(lightVector + viewDirection);
    return specularColor * pow(saturate(dot(normal, halfVector)), glossPower) * strength;
}

float3 CalculateLensReflection(float3 normal, float3 viewDirection, float fresnel)
{
    float3 reflectionVector = reflect(-viewDirection, normal);
    float sky = saturate(reflectionVector.y * 0.5 + 0.5);
    float3 coolReflection = lerp(float3(0.08, 0.09, 0.10), float3(0.78, 0.86, 0.96), sky);
    float band = pow(saturate(1.0 - abs(frac(reflectionVector.x * 3.7 + reflectionVector.y * 2.1) - 0.5) * 4.0), 5.0);
    return coolReflection * 0.22 + float3(0.95, 0.98, 1.0) * band * (0.32 + fresnel);
}

float4 MainPS(PixelShaderInput input) : COLOR0
{
    float3 normal = normalize(input.WorldNormal);
    float3 viewDirection = normalize(CameraPosition - input.WorldPosition);
    float3 textureColor = tex2D(TextureSampler, input.TexCoord).rgb;
    float lensDetail = lerp(1.0, dot(textureColor, float3(0.299, 0.587, 0.114)), LensDetailStrength);
    float3 albedo = saturate(BaseColor * lerp(textureColor, float3(1.0, 1.0, 1.0), 1.0 - LensDetailStrength));

    float gloss = 1.0 - saturate(Roughness);
    float glossPower = lerp(18.0, 192.0, gloss * gloss);
    float facing = saturate(dot(normal, viewDirection));
    float fresnel = pow(1.0 - facing, 5.0) * FresnelStrength;

    float diffuse0 = saturate(dot(normal, normalize(-LightDirection0)));
    float diffuse1 = saturate(dot(normal, normalize(-LightDirection1)));
    float3 diffuse = albedo * (AmbientLightColor + LightColor0 * diffuse0 * 0.42 + LightColor1 * diffuse1 * 0.20);
    float3 specular =
        CalculateSpecular(normal, viewDirection, LightDirection0, LightSpecularColor0, glossPower, SpecularStrength) +
        CalculateSpecular(normal, viewDirection, LightDirection1, LightSpecularColor1, glossPower, SpecularStrength * 0.62);
    float3 reflection = CalculateLensReflection(normal, viewDirection, fresnel) * ReflectionStrength * (0.35 + fresnel);

    float3 color = diffuse * lensDetail + specular + reflection + EmissiveColor * EmissiveStrength;
    float alpha = saturate(Opacity + fresnel * 0.28 + LensDetailStrength * (lensDetail - 0.5) * 0.20);

    float fogAmount = saturate((distance(CameraPosition, input.WorldPosition) - FogStart) / max(0.001, FogEnd - FogStart));
    color = lerp(color, FogColor, fogAmount);
    alpha = lerp(alpha, 1.0, fogAmount * 0.25);
    return float4(saturate(color), alpha);
}

technique VehicleTransparent
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
