using System.Text;

namespace RemoteSigner.SeedCeremony;

/// <summary>
/// Console handling rules that keep the mnemonic off shells, pipes and scrollback: interactive-TTY
/// enforcement, no-echo input, a written-it-down quiz and screen wiping. Prompts go to stderr so
/// stdout stays clean for machine-readable output
/// </summary>
public static class ConsoleSafety
{
    /// <summary>
    /// Refuses to run when stdin or stdout is redirected — commands that display or read a
    /// mnemonic must only ever talk to a live terminal
    /// </summary>
    /// <param name="command"></param>
    public static void RequireInteractiveConsole(string command)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException(
                $"'{command}' displays or reads a mnemonic and requires an interactive terminal; refusing to run with redirected stdin/stdout");
    }

    /// <summary>
    /// Reads one line without echoing it to the terminal
    /// </summary>
    /// <param name="prompt"></param>
    public static string ReadSecretLine(string prompt)
    {
        Console.Error.Write(prompt);

        var buffer = new StringBuilder();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key == ConsoleKey.Enter) break;

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (keyInfo.KeyChar != '\0') buffer.Append(keyInfo.KeyChar);
        }

        Console.Error.WriteLine();

        return buffer.ToString().Trim();
    }

    /// <summary>
    /// Quizzes the operator on randomly chosen word positions (input hidden) to prove the backup
    /// was actually written down. Throws when an answer does not match
    /// </summary>
    /// <param name="words"></param>
    /// <param name="wordsToAsk"></param>
    public static void RunBackupQuiz(string[] words, int wordsToAsk = 3)
    {
        var positions = Enumerable.Range(0, words.Length)
            .OrderBy(_ => Random.Shared.Next())
            .Take(wordsToAsk)
            .OrderBy(x => x)
            .ToList();

        Console.Error.WriteLine();
        Console.Error.WriteLine("Backup check: re-enter the requested words (input hidden).");

        foreach (var position in positions)
        {
            var answer = ReadSecretLine($"  Word #{position + 1}: ");

            if (!string.Equals(answer, words[position], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Backup check failed on word #{position + 1}. Run the ceremony again and write down all 24 words before continuing");
        }

        Console.Error.WriteLine("Backup check passed.");
    }

    /// <summary>
    /// Clears the visible screen and asks the terminal to wipe its scrollback so the mnemonic
    /// cannot be recovered by scrolling up
    /// </summary>
    public static void ClearScreenAndScrollback()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Console.Clear can throw when no real terminal is attached; the TTY requirement
            // makes this unlikely, but wiping must never crash the ceremony at this point
        }

        //ANSI "erase saved lines" — wipes scrollback on terminals that support it
        Console.Write("\x1b[3J");
    }
}
