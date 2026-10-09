using System.Text.Json;
using FluentAssertions;
using NBitcoin;
using Xunit;

namespace RemoteSigner.Tests;

/// <summary>
/// Byte-for-byte pins of the PSBT signer's output. ECDSA signatures are deterministic (RFC 6979),
/// so a dependency bump (NBitcoin) or a refactor of the request path must reproduce exactly these
/// PSBTs. Regenerate only on purpose: <c>GOLDEN_UPDATE=1 dotnet test --filter GoldenSignatureTests</c>.
/// </summary>
[Collection(SignerEnvironment.Collection)]
public class GoldenSignatureTests : IDisposable
{
    // SignPSBT only signs inputs whose fingerprint has an MF_ entry; the seed comes from the delegate.
    public GoldenSignatureTests() => Environment.SetEnvironmentVariable("MF_ed0210c8", "{}");

    public void Dispose() => Environment.SetEnvironmentVariable("MF_ed0210c8", null);

    private const string Mnemonic =
        "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-psbt-signatures.json");

    public static IEnumerable<object[]> Cases() =>
        Load().Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SignPSBT_IsByteIdenticalToTheGoldenOutput(string name)
    {
        var all = Load();
        var golden = all.Single(c => c.Name == name);

        var result = await new Function().SignPSBT(golden.Psbt, golden.Network, Enum.Parse<SigHash>(golden.SigHash),
            _ => Task.FromResult<string?>(Mnemonic));

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            golden.Expected = result!.Psbt;
            var source = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "golden-psbt-signatures.json");
            var onDisk = JsonSerializer.Deserialize<List<GoldenCase>>(await File.ReadAllTextAsync(source), Json)!;
            onDisk.Single(c => c.Name == name).Expected = result.Psbt;
            await File.WriteAllTextAsync(source, JsonSerializer.Serialize(onDisk, Json) + "\n");
            return;
        }

        golden.Expected.Should().NotBeNullOrEmpty("the golden output must be recorded");
        result!.Psbt.Should().Be(golden.Expected);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static List<GoldenCase> Load() =>
        JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(FixturePath), Json)!;

    public sealed class GoldenCase
    {
        public string Name { get; set; } = string.Empty;
        public string Psbt { get; set; } = string.Empty;
        public string SigHash { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string? Expected { get; set; }
    }
}

/// <summary>Tests that set process-wide MF_ env vars must not run in parallel with each other.</summary>
[CollectionDefinition(Collection)]
public sealed class SignerEnvironment
{
    public const string Collection = "Signer environment variables";
}
