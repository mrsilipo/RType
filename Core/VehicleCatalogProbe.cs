using RType.Data;
using RType.Vehicle;

namespace RType.Core;

internal static class VehicleCatalogProbe
{
    private static readonly string[] BuildRoots =
    [
        "Data/PurchaseCars",
        "Data/Garage/OwnedVehicles"
    ];

    public static void Run()
    {
        string[] buildPaths = [.. BuildRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

        if (buildPaths.Length == 0)
        {
            throw new InvalidOperationException("Vehicle catalog probe failed: no purchase or owned vehicle JSON files were found.");
        }

        int purchaseCars = 0;
        int ownedVehicles = 0;
        int infos = 0;
        int audioFallbacks = 0;

        Console.WriteLine("Vehicle catalog probe");
        foreach (string buildPath in buildPaths)
        {
            ResolvedVehicleAssembly assembly = VehicleAssemblyResolver.Resolve(buildPath);
            string role = assembly.Classification;
            bool isPurchaseCar = role.Equals("purchase_car_stock", StringComparison.OrdinalIgnoreCase);
            bool isOwnedVehicle = role.Equals("owned_vehicle", StringComparison.OrdinalIgnoreCase);

            if (isPurchaseCar)
            {
                purchaseCars++;
                Require(!assembly.PlayerOwned, $"{assembly.BuildId} is a purchase template but is marked player-owned");
            }
            else if (isOwnedVehicle)
            {
                ownedVehicles++;
                Require(assembly.PlayerOwned, $"{assembly.BuildId} is an owned vehicle but is not marked player-owned");
                Require(!string.IsNullOrWhiteSpace(assembly.SourcePurchaseCarPath), $"{assembly.BuildId} does not record its source purchase-car path");
                Require(!string.IsNullOrWhiteSpace(assembly.PurchaseCarId), $"{assembly.BuildId} does not record its source purchase-car id");
            }
            else
            {
                throw new InvalidOperationException($"Vehicle catalog probe failed: {assembly.BuildId} has unsupported role '{role}'.");
            }

            Require(string.IsNullOrWhiteSpace(assembly.VehicleDefinitionPath), $"{assembly.BuildId} still declares legacy vehicleDefinitionPath metadata");
            Require(assembly.RuntimeBuild.Drivetrain.ForwardGearRatios.Length > 0, $"{assembly.BuildId} resolved with no forward gears");
            Require(assembly.Engine.TorqueCurve.Length > 0, $"{assembly.BuildId} resolved with no engine torque curve");
            Require(assembly.Engine.EngineBrakeTorqueCurve.Length > 0, $"{assembly.BuildId} resolved with no engine-brake curve");
            Require(assembly.Engine.PowerComposition.BaselinePeakTorqueNm > 0f, $"{assembly.BuildId} resolved without a baseline peak torque trace");
            Require(Math.Abs(assembly.Engine.PowerComposition.ResolvedPeakTorqueNm - assembly.Engine.TorqueCurve.Max(point => point.TorqueNm)) < 0.01f,
                $"{assembly.BuildId} composition trace peak torque does not match resolved torque curve");
            Require(Math.Abs(assembly.Engine.PowerComposition.ResolvedPeakEngineBrakeTorqueNm - assembly.Engine.EngineBrakeTorqueCurve.Max(point => point.TorqueNm)) < 0.01f,
                $"{assembly.BuildId} composition trace peak engine-brake torque does not match resolved engine-brake curve");
            Require(assembly.MassProperties.TotalMassKg > 0f, $"{assembly.BuildId} resolved invalid total mass");
            Require(assembly.MassProperties.Trace.ComponentCount == assembly.MassProperties.Components.Count, $"{assembly.BuildId} mass trace component count does not match resolved components");
            Require(Math.Abs(assembly.MassProperties.Trace.TotalMassKg - assembly.MassProperties.TotalMassKg) < 0.01f, $"{assembly.BuildId} mass trace total does not match resolved mass");
            Require(Math.Abs(assembly.MassProperties.Trace.FinalYawInertiaKgM2 - assembly.MassProperties.YawInertiaKgM2) < 0.01f, $"{assembly.BuildId} mass trace yaw inertia does not match resolved yaw inertia");
            Require(assembly.MassProperties.Trace.CatalogMassKg > 0f, $"{assembly.BuildId} mass trace catalog mass is empty");
            Require(assembly.MassProperties.Trace.RawYawInertiaKgM2 > 0f, $"{assembly.BuildId} mass trace raw yaw inertia is empty");
            Require(!string.IsNullOrWhiteSpace(assembly.Engine.EngineAudioProfilePath), $"{assembly.BuildId} resolved without an engine audio profile");
            VehicleAudioParameters audio = VehicleRaceSampleAudioBuilder.Build(
                assembly.Engine,
                assembly.RuntimeBuild.Drivetrain,
                buildPath);
            Require(!string.IsNullOrWhiteSpace(audio.EngineAudioSampleGenerationKey), $"{assembly.BuildId} resolved without an engine audio sample generation key");
            Require(audio.EngineAudioEngineId.Equals(assembly.Engine.EngineId, StringComparison.OrdinalIgnoreCase), $"{assembly.BuildId} audio engine identity does not match resolved engine");
            Require(audio.EngineAudioBlockId.Equals(assembly.Engine.BlockId, StringComparison.OrdinalIgnoreCase), $"{assembly.BuildId} audio block identity does not match resolved engine");
            Require(audio.EngineAudioHeadId.Equals(assembly.Engine.HeadId, StringComparison.OrdinalIgnoreCase), $"{assembly.BuildId} audio head identity does not match resolved engine");
            Require(audio.EngineAudioTuneId.Equals(assembly.Engine.TuneId, StringComparison.OrdinalIgnoreCase), $"{assembly.BuildId} audio tune identity does not match resolved engine");
            Require(audio.EngineAudioFuelId.Equals(assembly.Engine.FuelId, StringComparison.OrdinalIgnoreCase), $"{assembly.BuildId} audio fuel identity does not match resolved engine");
            Require(!string.IsNullOrWhiteSpace(audio.EngineAudioDspId), $"{assembly.BuildId} resolved without an engine audio DSP id");
            Require(!string.IsNullOrWhiteSpace(audio.EngineAudioGenerationMethod), $"{assembly.BuildId} resolved without an engine audio generation method");
            Require(!string.IsNullOrWhiteSpace(audio.EngineAudioGeneratedSampleSetPath), $"{assembly.BuildId} resolved without a generated sample set path");
            if (!string.IsNullOrWhiteSpace(audio.EngineAudioProfileEngineId) &&
                !audio.EngineAudioProfileEngineId.Equals(audio.EngineAudioEngineId, StringComparison.OrdinalIgnoreCase))
            {
                Require(audio.EngineAudioFallbackAllowed, $"{assembly.BuildId} uses mismatched engine audio profile without fallbackAllowed");
                audioFallbacks++;
            }

            VehicleAssemblyValidationMessage[] vehicleWarnings = [.. assembly.Validation.Where(message => message.Severity == VehicleAssemblyValidationSeverity.Warning)];
            EngineAssemblyValidationMessage[] engineWarnings = [.. assembly.Engine.Validation.Where(message => message.Severity == EngineAssemblyValidationSeverity.Warning)];
            if (vehicleWarnings.Length > 0 || engineWarnings.Length > 0)
            {
                foreach (VehicleAssemblyValidationMessage warning in vehicleWarnings)
                {
                    Console.WriteLine($"  warning {assembly.BuildId}: {warning.Code} - {warning.Message}");
                }

                foreach (EngineAssemblyValidationMessage warning in engineWarnings)
                {
                    Console.WriteLine($"  warning {assembly.BuildId}: {warning.Code} - {warning.Message}");
                }

                throw new InvalidOperationException($"Vehicle catalog probe failed: {assembly.BuildId} produced validation warnings.");
            }

            infos += assembly.Validation.Count(message => message.Severity == VehicleAssemblyValidationSeverity.Info);
            infos += assembly.Engine.Validation.Count(message => message.Severity == EngineAssemblyValidationSeverity.Info);

            Console.WriteLine($"  {assembly.BuildId}: {role}, {assembly.Engine.EngineCode}, {assembly.Engine.DisplacementCc:0}cc, {assembly.MassProperties.TotalMassKg:0.0}kg, warnings 0");
        }

        Require(purchaseCars > 0, "no purchase-car templates were found");
        Require(ownedVehicles > 0, "no owned-vehicle records were found");

        Console.WriteLine($"  result: PASS ({purchaseCars} purchase, {ownedVehicles} owned, {infos} info messages, {audioFallbacks} audio fallbacks)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Vehicle catalog probe failed: {message}.");
        }
    }
}
