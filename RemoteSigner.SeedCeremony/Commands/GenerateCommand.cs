namespace RemoteSigner.SeedCeremony.Commands;

/// <summary>
/// Generates a fresh 24-word mnemonic, shows it exactly once on an interactive terminal for the
/// paper/steel backup, quizzes the operator, wipes the screen and then encrypts + emits
/// </summary>
public static class GenerateCommand
{
    public static async Task<int> Run(Options options)
    {
        ConsoleSafety.RequireInteractiveConsole("generate");

        var mnemonic = Ceremony.GenerateMnemonic();
        var words = mnemonic.Words;

        Console.WriteLine();
        Console.WriteLine("Write down the following 24 words IN ORDER on paper/steel. They are shown only once");
        Console.WriteLine("and must never exist digitally outside this ceremony.");
        Console.WriteLine();

        for (var i = 0; i < words.Length; i++)
        {
            Console.WriteLine($"  {i + 1,2}. {words[i]}");
        }

        Console.WriteLine();
        Console.Error.Write("Press Enter when the backup is written down...");
        Console.ReadLine();

        ConsoleSafety.RunBackupQuiz(words);

        ConsoleSafety.ClearScreenAndScrollback();

        return await EncryptAndEmit.Run(mnemonic, options);
    }
}
