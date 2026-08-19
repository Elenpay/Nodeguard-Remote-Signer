using System.Text.Json;
using NBitcoin;

namespace RemoteSigner.SeedCeremony.Commands;

/// <summary>
/// Preflight gate before touching the lambda: KMS-decrypts the manifest's env value through the
/// lambda's own decrypt path and re-derives fingerprint + xpub, asserting everything matches.
/// Never prints the seed
/// </summary>
public static class VerifyCommand
{
    public static async Task<int> Run(Options options)
    {
        var inPath = options.Require("--in");

        var manifest = CeremonyManifest.FromJson(await File.ReadAllTextAsync(inPath));

        var expectedXpub = options.Get("--xpub");
        if (expectedXpub != null && !string.Equals(expectedXpub, manifest.AccountXpub, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"VERIFY FAILED: the manifest xpub does not match --xpub, manifest has {manifest.AccountXpub}");
            return 1;
        }

        var config = JsonSerializer.Deserialize<SignPSBTConfig>(manifest.EnvValue);
        if (config == null)
        {
            Console.Error.WriteLine("VERIFY FAILED: the manifest EnvValue is not a valid SignPSBTConfig JSON");
            return 1;
        }

        var kmsClient = options.CreateKmsClient();
        var seed = await Function.DecryptSeedphrase(kmsClient, config);

        Mnemonic mnemonic;
        try
        {
            mnemonic = new Mnemonic(seed);
        }
        catch (Exception)
        {
            Console.Error.WriteLine("VERIFY FAILED: the decrypted seedphrase is not a valid BIP39 mnemonic");
            return 1;
        }

        try
        {
            Ceremony.VerifyManifest(manifest, mnemonic);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"VERIFY FAILED: {e.Message}");
            return 1;
        }

        Console.WriteLine($"VERIFY OK: {manifest.EnvName} decrypts and re-derives fingerprint {manifest.MasterFingerprint} and xpub {manifest.AccountXpub} ({manifest.DerivationPath} on {manifest.Network})");

        return 0;
    }
}
