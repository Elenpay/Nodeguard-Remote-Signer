using NBitcoin;

namespace RemoteSigner.SeedCeremony.Commands;

/// <summary>
/// Shared tail of the generate/encrypt commands: derive the public identifiers, KMS-encrypt the
/// mnemonic through the lambda's own EncryptSeedphrase, assemble the manifest and emit it
/// </summary>
public static class EncryptAndEmit
{
    public const string DefaultDerivationPath = "m/48'/1'";

    public static async Task<int> Run(Mnemonic mnemonic, Options options)
    {
        var kmsKeyId = options.Require("--kms-key-id");
        var networkArg = options.Require("--network");
        var derivationPath = options.GetOrDefault("--derivation-path", DefaultDerivationPath);
        var outputFormat = options.GetOrDefault("--output", "text");
        var outPath = options.Get("--out");

        if (outputFormat is not ("text" or "json"))
            throw new UsageException($"--output must be 'text' or 'json', got '{outputFormat}'");

        var network = Function.ParseNetwork(networkArg);
        var accountPath = KeyPath.Parse(derivationPath);

        var derived = Ceremony.Derive(mnemonic, network, accountPath);

        var kmsClient = options.CreateKmsClient();
        var encryptedSeedphrase = await new Function().EncryptSeedphrase(mnemonic.ToString(), kmsKeyId, kmsClient);

        var manifest = new CeremonyManifest
        {
            EnvName = derived.EnvName,
            EnvValue = Ceremony.BuildEnvValue(encryptedSeedphrase, kmsKeyId),
            MasterFingerprint = derived.MasterFingerprint,
            AccountXpub = derived.AccountXpub,
            DerivationPath = derivationPath,
            Network = networkArg.ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow.ToString("O")
        };

        if (outPath != null)
        {
            await File.WriteAllTextAsync(outPath, manifest.ToJson());
            Console.Error.WriteLine($"Manifest written to {outPath}");
        }

        if (outputFormat == "json")
        {
            Console.WriteLine(manifest.ToJson());
        }
        else
        {
            Console.WriteLine($"Env var name       : {manifest.EnvName}");
            Console.WriteLine($"Master fingerprint : {manifest.MasterFingerprint}");
            Console.WriteLine($"Account xpub       : {manifest.AccountXpub}");
            Console.WriteLine($"Derivation path    : {manifest.DerivationPath}");
            Console.WriteLine($"Network            : {manifest.Network}");
            Console.WriteLine(outPath != null
                ? "Env var value      : (in the manifest file, keep it for the lambda env merge)"
                : $"Env var value      : {manifest.EnvValue}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Next steps: run 'seed-ceremony verify --in <manifest>' as preflight, merge the env");
            Console.Error.WriteLine("var into the lambda configuration (snapshot -> jq merge -> apply, see README), and");
            Console.Error.WriteLine("insert the fingerprint + xpub into NodeGuard.");
        }

        return 0;
    }
}
