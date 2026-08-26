namespace RType.Data;

internal static class VehiclePathMigration
{
    public const string StockEk9PurchaseCarPath = "Data/PurchaseCars/2000_Ek9_Stock.json";
    public const string LegacyStockEk9VehicleBuildPath = "Data/VehicleBuilds/ek9_showroom_stock.json";
    public const string LegacyStockEk9VehicleDefinitionPath = "Data/Vehicles/ek9_reference_2000.json";

    public static string ResolveLegacyBuildPath(string path)
    {
        return Matches(path, LegacyStockEk9VehicleBuildPath) ? StockEk9PurchaseCarPath : path;
    }

    public static string ResolveLegacyRuntimeVehiclePath(string path)
    {
        return Matches(path, LegacyStockEk9VehicleDefinitionPath) ? StockEk9PurchaseCarPath : ResolveLegacyBuildPath(path);
    }

    public static bool IsLegacyStockEk9VehicleDefinitionPath(string path)
    {
        return Matches(path, LegacyStockEk9VehicleDefinitionPath);
    }

    private static bool Matches(string path, string relativePath)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Equals(relativePath, StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/" + relativePath, StringComparison.OrdinalIgnoreCase);
    }
}
