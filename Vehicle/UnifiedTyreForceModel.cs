using Microsoft.Xna.Framework;

namespace RType.Vehicle;

public static class UnifiedTyreForceModel
{
    public static UnifiedTyreForceResult CalculateFromRequest(
        TyreForceRequest request,
        float tyreShape = 1f,
        float slidingCurveFloor = 0f,
        float lateralLongitudinalGripCoupling = 1f)
    {
        UnifiedTyreForceDiagnostics diagnostics = CalculateFromGripBudget(
            request.GripBudgetN,
            request.RelaxedLongitudinalSlipRatio,
            request.RelaxedLateralSlip,
            request.LongitudinalPeakSlipRatio,
            request.LateralPeakSlip,
            tyreShape,
            slidingCurveFloor);

        float requestedLongitudinalForceN = request.RequestedLongitudinalForceN;
        float lateralForceN = CalculatePlateauLateralForce(request, tyreShape, slidingCurveFloor);
        float longitudinalForceN;
        float coupling = MathHelper.Clamp(lateralLongitudinalGripCoupling, 0f, 1f);

        if (MathF.Abs(requestedLongitudinalForceN) <= 0.001f)
        {
            longitudinalForceN = 0f;
        }
        else if (requestedLongitudinalForceN < 0f)
        {
            (longitudinalForceN, lateralForceN) = ConstrainCombinedForceToGrip(
                requestedLongitudinalForceN,
                lateralForceN,
                request.GripBudgetN,
                coupling);
        }
        else
        {
            longitudinalForceN = ConstrainLongitudinalRequestToGrip(
                requestedLongitudinalForceN,
                lateralForceN,
                request.GripBudgetN,
                coupling);
        }

        float gripUsage = CalculateGripUsage(longitudinalForceN, lateralForceN * coupling, request.GripBudgetN);
        return new UnifiedTyreForceResult(
            longitudinalForceN,
            lateralForceN,
            gripUsage,
            diagnostics);
    }

    public static UnifiedTyreForceDiagnostics Calculate(
        float normalLoadN,
        float activeSurfaceMu,
        float tyrePeakFriction,
        float relaxedLongitudinalSlipRatio,
        float relaxedLateralSlip,
        float longitudinalPeakSlipRatio,
        float lateralPeakSlip,
        float tyreShape = 1f,
        float slidingCurveFloor = 0f)
    {
        float peakGripForceN =
            MathF.Max(0f, normalLoadN) *
            MathF.Max(0.01f, activeSurfaceMu) *
            MathF.Max(0.01f, tyrePeakFriction);
        float safeLongPeak = MathF.Max(0.01f, longitudinalPeakSlipRatio);
        float safeLatPeak = MathF.Max(0.01f, lateralPeakSlip);
        float normalizedLong = relaxedLongitudinalSlipRatio / safeLongPeak;
        float normalizedLat = relaxedLateralSlip / safeLatPeak;
        float totalSlip = MathF.Sqrt(normalizedLong * normalizedLong + normalizedLat * normalizedLat);

        if (peakGripForceN <= 0f || totalSlip <= 0.0001f)
        {
            return new UnifiedTyreForceDiagnostics(
                totalSlip,
                peakGripForceN,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f);
        }

        float shape = MathF.Max(0.05f, tyreShape);
        float shapedSlip = totalSlip * shape;
        float curve = MathF.Sin(2f * MathF.Atan(shapedSlip));
        if (shapedSlip > 1f)
        {
            curve = MathF.Max(curve, MathHelper.Clamp(slidingCurveFloor, 0f, 1f));
        }

        curve = MathHelper.Clamp(curve, 0f, 1f);
        float totalTyreForceN = peakGripForceN * curve;
        float longShare = MathF.Abs(normalizedLong) / totalSlip;
        float latShare = MathF.Abs(normalizedLat) / totalSlip;
        float longForceN = totalTyreForceN * longShare * -MathF.Sign(normalizedLong);
        float latForceN = totalTyreForceN * latShare * -MathF.Sign(normalizedLat);
        float usage = totalTyreForceN / MathF.Max(1f, peakGripForceN);

        return new UnifiedTyreForceDiagnostics(
            totalSlip,
            peakGripForceN,
            longShare,
            latShare,
            longForceN,
            latForceN,
            totalTyreForceN,
            usage);
    }

    public static UnifiedTyreForceDiagnostics CalculateFromGripBudget(
        float gripBudgetN,
        float relaxedLongitudinalSlipRatio,
        float relaxedLateralSlip,
        float longitudinalPeakSlipRatio,
        float lateralPeakSlip,
        float tyreShape = 1f,
        float slidingCurveFloor = 0f)
    {
        return Calculate(
            normalLoadN: 1f,
            activeSurfaceMu: 1f,
            tyrePeakFriction: gripBudgetN,
            relaxedLongitudinalSlipRatio,
            relaxedLateralSlip,
            longitudinalPeakSlipRatio,
            lateralPeakSlip,
            tyreShape,
            slidingCurveFloor);
    }

    private static float CalculatePlateauLateralForce(
        TyreForceRequest request,
        float tyreShape,
        float slidingCurveFloor)
    {
        float gripBudgetN = MathF.Max(0f, request.GripBudgetN);
        if (gripBudgetN <= 0f)
        {
            return 0f;
        }

        float normalizedLat = request.RelaxedLateralSlip / MathF.Max(0.01f, request.LateralPeakSlip);
        float absNormalizedLat = MathF.Abs(normalizedLat);
        if (absNormalizedLat <= 0.0001f)
        {
            return 0f;
        }

        float curve = CalculatePlateauCurve(absNormalizedLat, tyreShape, slidingCurveFloor);
        return gripBudgetN * curve * MathF.Sign(normalizedLat);
    }

    private static float CalculatePlateauCurve(float normalizedSlip, float tyreShape, float slidingCurveFloor)
    {
        float shape = MathF.Max(0.05f, tyreShape);
        float shapedSlip = normalizedSlip * shape;
        float curve = MathF.Sin(2f * MathF.Atan(shapedSlip));
        if (shapedSlip > 1f)
        {
            curve = MathF.Max(curve, MathHelper.Clamp(slidingCurveFloor, 0f, 1f));
        }

        return MathHelper.Clamp(curve, 0f, 1f);
    }

    private static float ConstrainLongitudinalRequestToGrip(
        float requestedLongitudinalForceN,
        float lateralForceN,
        float gripBudgetN,
        float lateralLongitudinalGripCoupling)
    {
        float safeGripBudget = MathF.Max(0f, gripBudgetN);
        float lateralDemand = MathF.Abs(lateralForceN) * MathHelper.Clamp(lateralLongitudinalGripCoupling, 0f, 1f);
        if (safeGripBudget <= 0.01f || lateralDemand >= safeGripBudget)
        {
            return 0f;
        }

        float longitudinalBudget = MathF.Sqrt(safeGripBudget * safeGripBudget - lateralDemand * lateralDemand);
        return MathHelper.Clamp(requestedLongitudinalForceN, -longitudinalBudget, longitudinalBudget);
    }

    private static (float LongitudinalForceN, float LateralForceN) ConstrainCombinedForceToGrip(
        float longitudinalForceN,
        float lateralForceN,
        float gripBudgetN,
        float lateralLongitudinalGripCoupling)
    {
        float safeGripBudget = MathF.Max(0f, gripBudgetN);
        float coupling = MathHelper.Clamp(lateralLongitudinalGripCoupling, 0f, 1f);
        float coupledLateralForceN = lateralForceN * coupling;
        float combinedDemand = MathF.Sqrt(longitudinalForceN * longitudinalForceN + coupledLateralForceN * coupledLateralForceN);
        if (safeGripBudget <= 0.01f || combinedDemand <= safeGripBudget)
        {
            return (longitudinalForceN, lateralForceN);
        }

        float scale = safeGripBudget / MathF.Max(0.001f, combinedDemand);
        return (longitudinalForceN * scale, lateralForceN);
    }

    private static float CalculateGripUsage(float longitudinalForceN, float lateralForceN, float gripBudgetN)
    {
        if (gripBudgetN <= 1f)
        {
            return 0f;
        }

        return MathF.Sqrt(longitudinalForceN * longitudinalForceN + lateralForceN * lateralForceN) / gripBudgetN;
    }
}

public readonly record struct UnifiedTyreForceDiagnostics(
    float TotalSlip,
    float GripBudgetN,
    float LongitudinalShare,
    float LateralShare,
    float LongitudinalForceN,
    float LateralForceN,
    float TotalForceN,
    float GripUsage);

public readonly record struct TyreForceRequest(
    float GripBudgetN,
    float RequestedLongitudinalForceN,
    float RelaxedLongitudinalSlipRatio,
    float RelaxedLateralSlip,
    float LongitudinalPeakSlipRatio = 1f,
    float LateralPeakSlip = 1f);

public readonly record struct UnifiedTyreForceResult(
    float LongitudinalForceN,
    float LateralForceN,
    float GripUsage,
    UnifiedTyreForceDiagnostics Diagnostics);
