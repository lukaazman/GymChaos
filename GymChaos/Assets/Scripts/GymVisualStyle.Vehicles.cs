/// <summary>
/// Remade vehicles (Tools/build_vehicle_*.py): one GLB each under
/// StreamingAssets/BodyBuilders/vehicles/remake, used only in the Remake
/// style. The DEFAULT style keeps its own vehicle assets untouched.
/// </summary>
public static partial class GymVisualStyle
{
    public const string RemakeVehicleFolder = "BodyBuilders/vehicles/remake/";

    /// <summary>The remake GLB id of a visitor's vehicle, or null when it has none (Ronnie, members).</summary>
    public static string RemakeVehicleId(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold: return "arnold";
            case BodybuilderIdentity.Cbum: return "cbum";
            case BodybuilderIdentity.JayCutler: return "jaycutler";
            case BodybuilderIdentity.Zyzz: return "zyzz";
            case BodybuilderIdentity.Davie: return "bus";
            case BodybuilderIdentity.Goku: return "cloud";
            case BodybuilderIdentity.Policeman: return "police";
            default: return null;
        }
    }

    /// <summary>The remake GLB path for <paramref name="identity"/> when that vehicle uses POTATO, else null.</summary>
    public static string RemakeVehicleAsset(BodybuilderIdentity identity)
    {
        string id = RemakeVehicleId(identity);
        return id != null && GymVersionedLooks.IsSlopVehicle(id) ? RemakeVehicleFolder + id + ".glb" : null;
    }

    /// <summary><paramref name="ogAsset"/> in the DEFAULT style, the remake GLB in the Remake style.</summary>
    public static string VehicleAsset(BodybuilderIdentity identity, string ogAsset) =>
        RemakeVehicleAsset(identity) ?? ogAsset;
}
