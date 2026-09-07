#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 InverseViewProjection;
float3 SunDirection;
float Exposure = 0.78;
float MieG = 0.76;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = input.Position;
    output.TexCoord = input.TexCoord;
    return output;
}

float SmoothStep01(float edge0, float edge1, float value)
{
    float t = saturate((value - edge0) / max(0.0001, edge1 - edge0));
    return t * t * (3.0 - 2.0 * t);
}

float HenyeyGreenstein(float mu, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / pow(max(0.001, 1.0 + g2 - 2.0 * g * mu), 1.5);
}

float WaveNoise(float3 ray, float scale, float phase)
{
    float a = sin(ray.x * scale + ray.z * (scale * 0.57) + ray.y * (scale * 0.23) + phase);
    float b = sin(ray.x * (scale * -0.41) + ray.z * (scale * 0.83) + ray.y * (scale * 0.31) + phase * 1.73);
    float c = sin(ray.x * (scale * 0.19) + ray.z * (scale * -1.11) + ray.y * (scale * 0.47) + phase * 0.61);
    return a * 0.52 + b * 0.33 + c * 0.15;
}

float3 CalculateRayDirection(float2 uv)
{
    float2 clip = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 nearPoint = mul(float4(clip, 0.0, 1.0), InverseViewProjection);
    float4 farPoint = mul(float4(clip, 1.0, 1.0), InverseViewProjection);
    nearPoint.xyz /= max(0.0001, nearPoint.w);
    farPoint.xyz /= max(0.0001, farPoint.w);
    return normalize(farPoint.xyz - nearPoint.xyz);
}

float3 Gamma(float3 color)
{
    return pow(saturate(color), 1.0 / 2.2);
}

float4 MainPS(PixelShaderInput input) : COLOR0
{
    float3 viewRay = CalculateRayDirection(input.TexCoord);
    float3 sunToLight = normalize(-SunDirection);

    float horizon = SmoothStep01(-0.20, 0.28, viewRay.y);
    float zenith = saturate(viewRay.y * 0.5 + 0.5);
    float mu = clamp(dot(viewRay, sunToLight), -1.0, 1.0);

    float rayleighPhase = 0.75 * (1.0 + mu * mu);
    float miePhase = HenyeyGreenstein(mu, MieG);
    float airMass = 1.0 / max(0.10, viewRay.y + 0.18);
    float opticalDepth = clamp(airMass, 0.15, 9.5);

    float3 betaRayleigh = float3(0.22, 0.54, 1.42);
    float3 betaMie = float3(0.92, 0.80, 0.60);
    float3 rayleigh = betaRayleigh * rayleighPhase * 1.02 * exp(-opticalDepth * 0.070);
    float3 mie = betaMie * miePhase * 0.032 * exp(-opticalDepth * 0.032);

    float3 highSky = float3(0.020, 0.145, 0.66) * (0.86 + zenith * 0.82);
    float3 midSky = float3(0.15, 0.40, 0.78) * (0.72 + zenith * 0.18);
    float3 horizonHaze = float3(1.00, 0.965, 0.78) * pow(saturate(1.0 - horizon), 0.66);
    float3 baseSky = lerp(horizonHaze, highSky, horizon);
    baseSky = lerp(baseSky, midSky, smoothstep(0.16, 0.70, viewRay.y) * 0.31);

    float sunCore = pow(saturate(mu), 2400.0) * 22.0;
    float sunHalo = pow(saturate(mu), 58.0) * 1.05;
    float broadGlare = pow(saturate(mu), 9.0) * 0.18;
    float sunSideWarmth = pow(saturate(mu), 3.2) * SmoothStep01(-0.10, 0.46, viewRay.y) * 0.18;
    float shaftBand = pow(saturate(mu), 18.0) *
        (0.65 + 0.35 * sin((viewRay.x * 34.0 + viewRay.y * 11.0) * 1.7));

    float3 sunColor = float3(1.0, 0.88, 0.58);
    float highNoise = WaveNoise(viewRay, 17.0, 0.8);
    float fineNoise = WaveNoise(viewRay, 39.0, 2.4);
    float cirrusMask = smoothstep(0.32, 0.74, viewRay.y) * (1.0 - smoothstep(0.86, 1.0, viewRay.y));
    float cirrusStreak = smoothstep(0.38, 0.76, highNoise + fineNoise * 0.28) * cirrusMask;
    cirrusStreak *= 0.055;
    float lowAirBand = smoothstep(-0.05, 0.16, viewRay.y) * (1.0 - smoothstep(0.18, 0.44, viewRay.y));
    lowAirBand *= smoothstep(0.18, 0.74, WaveNoise(viewRay, 9.0, 4.1) * 0.55 + 0.48);
    float altitudeBand = 0.018 * sin((viewRay.x * 2.2 + viewRay.z * 1.4) + viewRay.y * 5.0);
    float domeDepth = SmoothStep01(0.30, 0.92, viewRay.y);
    float horizonCoolBreakup = SmoothStep01(-0.12, 0.18, viewRay.y) * (1.0 - SmoothStep01(0.14, 0.46, viewRay.y));
    horizonCoolBreakup *= SmoothStep01(0.18, 0.80, WaveNoise(viewRay, 5.2, 7.1) * 0.45 + 0.50);
    float3 color = baseSky * (0.66 + altitudeBand) + rayleigh * 0.34 + mie +
        sunColor * (sunCore + sunHalo + broadGlare + shaftBand * 0.055 + sunSideWarmth);
    color += float3(0.86, 0.93, 1.0) * cirrusStreak;
    color = lerp(color, float3(0.88, 0.91, 0.80), lowAirBand * 0.12);
    color = lerp(color, float3(0.12, 0.32, 0.74), domeDepth * 0.08);
    color = lerp(color, float3(0.70, 0.78, 0.78), horizonCoolBreakup * 0.07);

    float hazeWeight = pow(saturate(1.0 - horizon), 1.55);
    color = lerp(color, float3(1.00, 0.955, 0.78), hazeWeight * 0.36);
    color += float3(1.00, 0.74, 0.30) * hazeWeight * 0.10;
    float screenHaze = smoothstep(0.10, 0.36, input.TexCoord.y) * (1.0 - smoothstep(0.45, 0.82, input.TexCoord.y));
    color = lerp(color, float3(0.96, 0.94, 0.80), screenHaze * 0.24);
    color += float3(0.98, 0.75, 0.32) * screenHaze * 0.025;
    color = 1.0 - exp(-color * Exposure);
    return float4(Gamma(color), 1.0);
}

technique ProceduralAtmosphere
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
