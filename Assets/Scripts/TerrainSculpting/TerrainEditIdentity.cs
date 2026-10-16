using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

/// <summary>One local slot, independent of seed. Configuration changes invalidate its fingerprint.</summary>
public sealed class TerrainEditStorageConfiguration
{
    public readonly string Root, Slot;
    public TerrainEditStorageConfiguration(string persistentRoot, string slot = "development-default")
    {
        if (string.IsNullOrWhiteSpace(persistentRoot)) throw new ArgumentException("A persistent root is required.");
        if (string.IsNullOrEmpty(slot) || slot.Length > 64 || slot == "." || slot == "..") throw new ArgumentException("Invalid terrain slot.");
        foreach (char c in slot) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_'))
            throw new ArgumentException("Terrain slots use lowercase ASCII letters, numbers, hyphens and underscores.");
        Root = Path.GetFullPath(Path.Combine(persistentRoot, "Worlds", slot, "TerrainEdits")); Slot = slot;
    }
}

public sealed class TerrainEditIdentity
{
    // Bump when canonical generation, cave policy or baseline interpolation changes.
    public const string GeneratorVersion = "firstsettlers-native-heightfield-solid-v1/triangle-ac-v1";
    public string WorldId { get; }
    public string Fingerprint { get; }
    public TerrainDensityProfile Profile { get; }
    public TerrainEditIdentity(string worldId, string fingerprint, TerrainDensityProfile profile)
    {
        if (!IsDigest(worldId) || !IsDigest(fingerprint)) throw new ArgumentException("Identity requires SHA-256 hex digests.");
        profile.Validate(); WorldId = worldId; Fingerprint = fingerprint; Profile = profile;
    }
    public static TerrainEditIdentity Create(string slot, WorldGenerationConfiguration generation, TerrainDensityProfile profile)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(TerrainEditStore.FormatVersion); writer.Write(GeneratorVersion);
            writer.Write(profile.Spacing); writer.Write(profile.Band);
            writer.Write(TerrainDensityProfile.BrickEdge); writer.Write("negative-stone/zero-air/int16-symmetric-away-from-zero-v1");
            WriteFields(writer, generation);
        }
        return new TerrainEditIdentity(Digest(Encoding.UTF8.GetBytes("firstsettlers-world-slot/" + slot)), Digest(stream.ToArray()), profile);
    }
    // Stable named and typed fields, including the private erosion schema version.
    private static void WriteFields(BinaryWriter writer, object value)
    {
        Type type = value.GetType();
        if (type == typeof(int)) { writer.Write((int)value); return; }
        if (type == typeof(float)) { writer.Write((float)value); return; }
        if (type == typeof(bool)) { writer.Write((bool)value); return; }
        if (type.IsEnum) { writer.Write(Convert.ToInt32(value)); return; }
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name,b.Name));
        writer.Write(fields.Length);
        foreach (var field in fields) { writer.Write(field.Name); writer.Write(field.FieldType.FullName); WriteFields(writer,field.GetValue(value)); }
    }
    internal static string Digest(byte[] bytes)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    internal static bool IsDigest(string text)
    {
        if (text == null || text.Length != 64) return false;
        foreach (char c in text) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
        return true;
    }
}
