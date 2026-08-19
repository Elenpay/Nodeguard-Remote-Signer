using Amazon.Runtime;
using RemoteSigner.SeedCeremony;
using RemoteSigner.SeedCeremony.Commands;

const int exitOk = 0;
const int exitFailure = 1;
const int exitUsage = 2;
const int exitAws = 3;

const string usage = """
    seed-ceremony — provision NodeGuard remote signer seeds (generate/encrypt/verify)

    Usage:
      seed-ceremony generate --kms-key-id <id> --network <mainnet|testnet|regtest>
                             [--derivation-path m/48'/1'] [--out <manifest.json>]
                             [--output text|json] [--profile <p>] [--region <r>]
          Generate a fresh 24-word mnemonic (interactive terminal required), back it up,
          KMS-encrypt it and emit the MF_* env var + NodeGuard xpub/fingerprint.

      seed-ceremony encrypt  --kms-key-id <id> --network <mainnet|testnet|regtest>
                             [--seed-file <path>] [--derivation-path m/48'/1']
                             [--out <manifest.json>] [--output text|json]
                             [--profile <p>] [--region <r>]
          Same for an EXISTING mnemonic, read from --seed-file or a hidden prompt.
          The mnemonic is never accepted as a command-line argument.

      seed-ceremony verify   --in <manifest.json> [--xpub <expected>]
                             [--profile <p>] [--region <r>]
          Preflight gate: KMS-decrypt the manifest's env value and check that the
          fingerprint, env var name and account xpub all re-derive identically.

    Exit codes: 0 ok, 1 verification/derivation failure, 2 usage error, 3 AWS/KMS error.
    AWS credentials come from the default chain (env vars, profiles, SSO) — never from flags.
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(usage);
    return exitUsage;
}

if (args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(usage);
    return exitOk;
}

var command = args[0];
var commandArgs = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "generate" => await GenerateCommand.Run(Options.Parse(commandArgs,
            "--kms-key-id", "--network", "--derivation-path", "--out", "--output", "--profile", "--region")),
        "encrypt" => await EncryptCommand.Run(Options.Parse(commandArgs,
            "--kms-key-id", "--network", "--derivation-path", "--seed-file", "--out", "--output", "--profile", "--region")),
        "verify" => await VerifyCommand.Run(Options.Parse(commandArgs,
            "--in", "--xpub", "--profile", "--region")),
        _ => throw new UsageException($"Unknown command '{command}'")
    };
}
catch (UsageException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(usage);
    return exitUsage;
}
catch (AmazonServiceException e)
{
    Console.Error.WriteLine($"AWS error: {e.Message}");
    return exitAws;
}
catch (Exception e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return exitFailure;
}
