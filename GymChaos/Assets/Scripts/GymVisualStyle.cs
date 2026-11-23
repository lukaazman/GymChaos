using System.Collections.Generic;

/// <summary>
/// The remade (POTATO) character and vehicle assets and their names. There is no
/// game-wide style any more: each character and each vehicle is switched on its
/// own in the appearance menu (<see cref="GymVersionedLooks"/>), so a gym can
/// mix a DEFAULT Arnold with a POTATO bus. Everything here is a lookup over the file
/// names <c>Tools/export_authored_character_fbx.py</c> writes.
/// </summary>
public static partial class GymVisualStyle
{
    public const string OgName = "DEFAULT";
    public const string SlopName = "POTATO";
    public const string HomelessFile = "homeless_guy";
    public const string FixerFile = "jolly_dog";

    /// <summary>Light and grade values: the DEFAULT day/night look (multipliers on the base).</summary>
    public readonly struct Look
    {
        public readonly float Ambient;
        public readonly float Sun;
        public readonly float ShadowStrength;
        public readonly float Fog;
        public readonly float Exposure;
        public readonly float Saturation;

        public Look(float ambient, float sun, float shadowStrength, float fog, float exposure, float saturation)
        {
            Ambient = ambient;
            Sun = sun;
            ShadowStrength = shadowStrength;
            Fog = fog;
            Exposure = exposure;
            Saturation = saturation;
        }
    }

    public static readonly Look OgLook = new Look(1f, 1f, 0.78f, 1f, 0f, 0f);

    private static readonly Dictionary<BodybuilderIdentity, string> RemakeNames =
        new Dictionary<BodybuilderIdentity, string>
        {
            { BodybuilderIdentity.Arnold, "Arnold Schnitzel" },
            { BodybuilderIdentity.Cbum, "CBun" },
            { BodybuilderIdentity.Zyzz, "Zizzle" },
            { BodybuilderIdentity.Ronnie, "Big Ron" },
            { BodybuilderIdentity.JayCutler, "Jay Butler" },
            { BodybuilderIdentity.Goku, "Gokoo" },
            { BodybuilderIdentity.Policeman, "Officer Pudding" },
            // Remade under their own names.
            { BodybuilderIdentity.Mark, "Mark" },
            { BodybuilderIdentity.Manwithsuit1, "manwithsuit1" }
        };

    /// <summary>The POTATO model and texture for this identity, when its POTATO version is chosen.</summary>
    public static bool TryRemake(BodybuilderIdentity identity, out string modelPath, out string texturePath)
    {
        string file = GymVersionedLooks.MemberKey(identity);
        if (file != null && GymVersionedLooks.IsSlop(file))
        {
            modelPath = ModelPath(file);
            texturePath = TexturePath(file);
            return true;
        }
        modelPath = texturePath = null;
        return false;
    }

    public static string ModelPath(string file) => "Characters/Remake/" + file + "_authored";
    public static string TexturePath(string file) => "Characters/Remake/Textures/" + file;

    public static string DisplayName(BodybuilderIdentity identity, string ogName) =>
        GymVersionedLooks.IsSlop(identity) && RemakeNames.TryGetValue(identity, out string name) ? name : ogName;

    public static string HomelessName(string ogName) => GymVersionedLooks.IsSlop("hank") ? "Hank Tank" : ogName;

    public static string FixerName(string ogName) => GymVersionedLooks.IsSlop("larry") ? "Lollipup Larry" : ogName;

    /// <summary>Every identity with a remake (verifier and tooling).</summary>
    public static IEnumerable<BodybuilderIdentity> RemadeIdentities
    {
        get
        {
            foreach (BodybuilderIdentity identity in System.Enum.GetValues(typeof(BodybuilderIdentity)))
            {
                if (GymVersionedLooks.HasMemberVersion(identity)) yield return identity;
            }
        }
    }
}
