using System.Text.Json;

namespace RemoteSigner.SeedCeremony;

/// <summary>
/// The public output of a seed ceremony: everything an operator needs to configure the lambda
/// (EnvName/EnvValue) and NodeGuard (MasterFingerprint/AccountXpub). Contains ciphertext and
/// public key material only — never the mnemonic
/// </summary>
public sealed class CeremonyManifest
{
    public required string EnvName { get; init; }

    /// <summary>Byte-exact MF_* env var value (serialized SignPSBTConfig)</summary>
    public required string EnvValue { get; init; }

    public required string MasterFingerprint { get; init; }

    public required string AccountXpub { get; init; }

    public required string DerivationPath { get; init; }

    public required string Network { get; init; }

    public required string CreatedAtUtc { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public static CeremonyManifest FromJson(string json)
    {
        return JsonSerializer.Deserialize<CeremonyManifest>(json)
               ?? throw new ArgumentException("The manifest could not be deserialized", nameof(json));
    }
}
