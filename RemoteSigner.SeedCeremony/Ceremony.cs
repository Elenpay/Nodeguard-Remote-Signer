using System.Text.Json;
using NBitcoin;

namespace RemoteSigner.SeedCeremony;

/// <summary>
/// Public identifiers derived from a mnemonic during a seed ceremony
/// </summary>
/// <param name="MasterFingerprint">8 lowercase hex chars, the MF_ env var suffix</param>
/// <param name="AccountXpub">The xpub at the account derivation path (NodeGuard's InternalWallets.XPUB)</param>
/// <param name="EnvName">The Lambda env var name, MF_{MasterFingerprint}</param>
public sealed record CeremonyResult(string MasterFingerprint, string AccountXpub, string EnvName);

/// <summary>
/// Pure derivation and output-assembly logic of the seed ceremony, kept free of console/AWS I/O so
/// it can be unit tested. Everything here must stay call-for-call compatible with the lambda
/// (Function.SignPSBT fingerprint handling) and with NodeGuard's InternalWallet.GetXPUB
/// </summary>
public static class Ceremony
{
    /// <summary>
    /// Generates a fresh 24-word english mnemonic without BIP39 passphrase (all consumers derive
    /// with Mnemonic.DeriveExtKey() and no passphrase)
    /// </summary>
    public static Mnemonic GenerateMnemonic()
    {
        return new Mnemonic(Wordlist.English, WordCount.TwentyFour);
    }

    /// <summary>
    /// Derives the public identifiers NodeGuard and the remote signer need from a mnemonic. The
    /// fingerprint mirrors Function.SignPSBT (extKey.GetWif(network).GetPublicKey().GetHDFingerPrint())
    /// and the account xpub mirrors NodeGuard's InternalWallet.GetXPUB (master derived at the
    /// account path, neutered)
    /// </summary>
    /// <param name="mnemonic"></param>
    /// <param name="network"></param>
    /// <param name="accountPath">Account-level derivation path, e.g. m/48'/1'</param>
    public static CeremonyResult Derive(Mnemonic mnemonic, Network network, KeyPath accountPath)
    {
        var masterKey = mnemonic.DeriveExtKey().GetWif(network);

        var masterFingerprint = masterKey.GetPublicKey().GetHDFingerPrint().ToString();

        var accountXpub = masterKey.Derive(accountPath).Neuter().ToWif();

        return new CeremonyResult(masterFingerprint, accountXpub, $"MF_{masterFingerprint}");
    }

    //Relaxed escaping keeps base64 plus signs literal instead of the default encoder's u002B
    //unicode escapes; JSON parsers decode both identically
    private static readonly JsonSerializerOptions EnvValueSerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Builds the MF_* env var value by serializing the lambda's own SignPSBTConfig DTO, so the
    /// JSON shape/casing can never drift from what Function.GetConfig deserializes
    /// </summary>
    /// <param name="encryptedSeedphraseBase64"></param>
    /// <param name="kmsKeyId"></param>
    public static string BuildEnvValue(string encryptedSeedphraseBase64, string kmsKeyId)
    {
        var config = new SignPSBTConfig
        {
            EncryptedSeedphrase = encryptedSeedphraseBase64,
            AwsKmsKeyId = kmsKeyId
        };

        return JsonSerializer.Serialize(config, EnvValueSerializerOptions);
    }

    /// <summary>
    /// Checks that a manifest is internally consistent with the (already decrypted) mnemonic it
    /// was produced from: env var name, master fingerprint and account xpub must all re-derive
    /// identically. Throws with a specific message on the first mismatch
    /// </summary>
    /// <param name="manifest"></param>
    /// <param name="mnemonic"></param>
    public static void VerifyManifest(CeremonyManifest manifest, Mnemonic mnemonic)
    {
        var result = Derive(mnemonic, Function.ParseNetwork(manifest.Network), KeyPath.Parse(manifest.DerivationPath));

        if (!string.Equals(manifest.MasterFingerprint, result.MasterFingerprint, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Master fingerprint mismatch: the manifest says {manifest.MasterFingerprint} but the decrypted seed derives {result.MasterFingerprint}");

        if (!string.Equals(manifest.EnvName, result.EnvName, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Env var name mismatch: the manifest says {manifest.EnvName} but the decrypted seed derives {result.EnvName}");

        if (!string.Equals(manifest.AccountXpub, result.AccountXpub, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Account xpub mismatch at {manifest.DerivationPath} on {manifest.Network}: the manifest says {manifest.AccountXpub} but the decrypted seed derives {result.AccountXpub}");
    }
}
