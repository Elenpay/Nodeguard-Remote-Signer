using NBitcoin;

namespace RemoteSigner.SeedCeremony.Commands;

/// <summary>
/// Encrypts an EXISTING mnemonic. The mnemonic is read from --seed-file or a hidden interactive
/// prompt — never from command-line arguments, which leak via shell history and process listings
/// </summary>
public static class EncryptCommand
{
    public static async Task<int> Run(Options options)
    {
        var seedFile = options.Get("--seed-file");

        string mnemonicString;
        if (seedFile != null)
        {
            mnemonicString = (await File.ReadAllTextAsync(seedFile)).Trim();
        }
        else if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("warning: reading the mnemonic from redirected stdin; prefer an interactive prompt or --seed-file");
            mnemonicString = (Console.In.ReadLine() ?? string.Empty).Trim();
        }
        else
        {
            mnemonicString = ConsoleSafety.ReadSecretLine("Enter the mnemonic (input hidden): ");
        }

        if (string.IsNullOrWhiteSpace(mnemonicString))
            throw new UsageException("No mnemonic was provided");

        Mnemonic mnemonic;
        try
        {
            mnemonic = new Mnemonic(mnemonicString);
        }
        catch (Exception)
        {
            throw new ArgumentException("The provided mnemonic is not a valid BIP39 mnemonic");
        }

        return await EncryptAndEmit.Run(mnemonic, options);
    }
}
