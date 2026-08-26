namespace RType.Data;

internal static class GarageModSlotMap
{
    public static readonly IReadOnlyDictionary<string, string> EngineCatalogSlotToInstalledSlot =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blockUpgrade"] = "blockUpgrade",
            ["headUpgrade"] = "headUpgrade",
            ["cams"] = "cams",
            ["rotatingAssembly"] = "displacement",
            ["headWork"] = "portPolishing",
            ["throttleBody"] = "throttleBody",
            ["intake"] = "intake",
            ["intakeRunnerLength"] = "intakeRunnerLength",
            ["valveSpringSet"] = "valveSprings",
            ["header"] = "headers",
            ["exhaust"] = "exhaust",
            ["flywheel"] = "flywheel",
            ["clutch"] = "clutch",
            ["engineAudioDsp"] = "engineAudioDsp"
        };

    public static readonly ISet<string> EngineInstalledSlots =
        new HashSet<string>(EngineCatalogSlotToInstalledSlot.Values, StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string[]> VehicleSlotPaths =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gearbox"] = ["drivetrain", "gearbox"],
            ["finalDrive"] = ["drivetrain", "finalDrive"],
            ["differential"] = ["drivetrain", "differential"],
            ["frontSuspension"] = ["suspension", "front"],
            ["rearSuspension"] = ["suspension", "rear"],
            ["alignment"] = ["suspension", "alignment"],
            ["frontBrakes"] = ["brakes", "front"],
            ["rearBrakes"] = ["brakes", "rear"],
            ["brakeSystem"] = ["brakes", "system"],
            ["frontWheels"] = ["wheels", "front"],
            ["rearWheels"] = ["wheels", "rear"],
            ["frontTyres"] = ["tyres", "frontCompound"],
            ["rearTyres"] = ["tyres", "rearCompound"],
            ["aeroPackage"] = ["aero", "package"]
        };

    public static readonly IReadOnlyDictionary<string, string[]> VehicleCatalogSlotTargets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gearbox"] = ["gearbox"],
            ["finalDrive"] = ["finalDrive"],
            ["differential"] = ["differential"],
            ["suspension"] = ["frontSuspension", "rearSuspension"],
            ["alignment"] = ["alignment"],
            ["brakes"] = ["frontBrakes", "rearBrakes"],
            ["brakeSystem"] = ["brakeSystem"],
            ["wheels"] = ["frontWheels", "rearWheels"],
            ["tyres"] = ["frontTyres", "rearTyres"],
            ["tyrePackage"] = ["tyrePackage"],
            ["aeroPackage"] = ["aeroPackage"]
        };
}
