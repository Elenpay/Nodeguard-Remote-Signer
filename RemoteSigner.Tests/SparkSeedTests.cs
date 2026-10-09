using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using NBitcoin;
using NSpark.RemoteSigner;
using Xunit;

namespace RemoteSigner.Tests;

/// <summary>
/// Which seed may sign for Spark, and how it is loaded: only MF_{SPARK_SEED_FINGERPRINT}, never a
/// Compromised one, at SPARK_ACCOUNT, with only the derived keys cached
/// </summary>
[Collection(SignerEnvironment.Collection)]
public class SparkSeedTests : IDisposable
{
    /// <summary>
    /// The public dev vector (fingerprint ed0210c8) used across these tests; never fund it
    /// </summary>
    private const string DevMnemonic =
        "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

    /// <summary>
    /// Spark identity of the dev vector at accounts 0 and 1 (m/8797555'/account'/0')
    /// </summary>
    private const string DevIdentityAccount0 = "032986ec06431b7b80a4171836174ac10c07e1cb1dff4af93082ee5f3ebca64a0a";
    private const string DevIdentityAccount1 = "0346c1d72f7bdb4db20210eaa328361abe53f3144730c5a7f38846bf653c40a992";

    private const string AbandonMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly string[] Variables =
    [
        "MF_ed0210c8", "MF_aabbccdd", SparkSeed.FingerprintVariable, SparkSeed.AccountVariable,
        SparkSeed.CacheSecondsVariable
    ];

    private readonly FakeKmsClient _kms = new();
    private readonly ManualTime _time = new();
    private readonly Function _function;

    public SparkSeedTests()
    {
        SetSeed("MF_ed0210c8", DevMnemonic);
        Environment.SetEnvironmentVariable(SparkSeed.FingerprintVariable, "ed0210c8");
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, "0");
        _function = new Function(() => _kms, _time);
    }

    public void Dispose()
    {
        foreach (var variable in Variables) Environment.SetEnvironmentVariable(variable, null);
    }

    [Fact]
    public async Task Info_ReportsTheSparkIdentityOfTheAllowlistedSeed()
    {
        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(200);
        var info = envelope.Result!.Value.Deserialize<SparkSignerInfo>(SparkSignerContract.Json)!;
        info.ContractVersion.Should().Be(SparkSignerContract.Version);
        info.IdentityPublicKey.Should().Be(DevIdentityAccount0);
        _kms.LastDecryptRequest!.KeyId.Should().Be("mrk-test");
    }

    [Fact]
    public async Task IdentityPublicKey_IsTheSameKeyAsInfo()
    {
        var (status, envelope) = await Spark("identity-public-key", "ed0210c8");

        status.Should().Be(200);
        Convert.ToHexString(envelope.Result!.Value.Deserialize<byte[]>(SparkSignerContract.Json)!).ToLowerInvariant()
            .Should().Be(DevIdentityAccount0);
    }

    [Fact]
    public async Task TheAccount_SelectsADifferentSparkIdentity()
    {
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, "1");

        var (_, envelope) = await Spark("info", "ed0210c8");

        envelope.Result!.Value.Deserialize<SparkSignerInfo>(SparkSignerContract.Json)!.IdentityPublicKey
            .Should().Be(DevIdentityAccount1);
    }

    [Theory]
    [InlineData("1fcce4de")]
    [InlineData("ed0210c9")]
    [InlineData("../../ed0210c8")]
    [InlineData("ed0210c8ed0210c8")]
    public async Task AnyOtherWallet_IsRefusedWithoutDecryptingAnything(string wallet)
    {
        var (status, envelope) = await Spark("info", wallet);

        status.Should().Be(403);
        envelope.Error.Should().Contain("not enabled for Spark");
        _kms.DecryptCalls.Should().Be(0);
    }

    [Fact]
    public async Task WithoutASparkSeedFingerprint_SparkIsNotEnabled()
    {
        Environment.SetEnvironmentVariable(SparkSeed.FingerprintVariable, null);

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(403);
        envelope.Error.Should().Contain("not enabled on this signer");
        _kms.DecryptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ACompromisedSeed_IsRefusedWithoutDecryptingIt()
    {
        SetSeed("MF_ed0210c8", DevMnemonic, compromised: true);

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(403);
        envelope.Error.Should().Contain("Compromised");
        _kms.DecryptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ASeedWhoseFingerprintDoesNotMatchItsMFEntry_IsRefused()
    {
        SetSeed("MF_ed0210c8", AbandonMnemonic);

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(500);
        envelope.Error.Should().Contain("master fingerprint").And.NotContain("abandon");
        envelope.Result.Should().BeNull();
    }

    [Fact]
    public async Task AnInvalidMnemonic_IsReportedWithoutEchoingIt()
    {
        SetSeed("MF_ed0210c8", "middle teach digital prefer fiscal theory syrup enter crash muffin easily zzzz");

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(500);
        envelope.Error.Should().Contain("not a valid BIP-39 mnemonic").And.NotContain("middle");
    }

    [Fact]
    public async Task RepeatedWhitespace_InTheStoredSeed_DerivesTheSameIdentity()
    {
        SetSeed("MF_ed0210c8", DevMnemonic.Replace(" ", "  ") + " ");

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(200);
        envelope.Result!.Value.Deserialize<SparkSignerInfo>(SparkSignerContract.Json)!.IdentityPublicKey
            .Should().Be(DevIdentityAccount0);
    }

    [Fact]
    public async Task ASparkSeedFingerprintWithoutAnMFEntry_IsAConfigurationError()
    {
        Environment.SetEnvironmentVariable(SparkSeed.FingerprintVariable, "aabbccdd");

        var (status, envelope) = await Spark("info", "aabbccdd");

        status.Should().Be(500);
        envelope.Error.Should().Contain("MF_aabbccdd is not configured");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutASparkAccount_AccountZeroIsUsed(string? account)
    {
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, account);

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(200);
        envelope.Result!.Value.Deserialize<SparkSignerInfo>(SparkSignerContract.Json)!.IdentityPublicKey
            .Should().Be(DevIdentityAccount0);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("one")]
    public async Task AnInvalidSparkAccount_IsAConfigurationError(string? account)
    {
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, account);

        var (status, envelope) = await Spark("info", "ed0210c8");

        status.Should().Be(500);
        envelope.Error.Should().Contain(SparkSeed.AccountVariable);
        _kms.DecryptCalls.Should().Be(0);
    }

    [Fact]
    public async Task DerivedKeys_AreCachedForSixtySecondsByDefault()
    {
        await Spark("info", "ed0210c8");
        _time.Advance(TimeSpan.FromSeconds(59));
        await Spark("identity-public-key", "ed0210c8");

        _kms.DecryptCalls.Should().Be(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Spark("info", "ed0210c8");

        _kms.DecryptCalls.Should().Be(2);
    }

    [Fact]
    public async Task ACacheOfZeroSeconds_DecryptsOnEveryRequest()
    {
        Environment.SetEnvironmentVariable(SparkSeed.CacheSecondsVariable, "0");

        await Spark("info", "ed0210c8");
        await Spark("info", "ed0210c8");

        _kms.DecryptCalls.Should().Be(2);
    }

    [Fact]
    public async Task ChangingTheAccount_DoesNotServeTheCachedKeys()
    {
        await Spark("info", "ed0210c8");
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, "1");

        var (_, envelope) = await Spark("info", "ed0210c8");

        envelope.Result!.Value.Deserialize<SparkSignerInfo>(SparkSignerContract.Json)!.IdentityPublicKey
            .Should().Be(DevIdentityAccount1);
    }

    [Fact]
    public async Task FailedLoads_AreNotCached()
    {
        SetSeed("MF_ed0210c8", AbandonMnemonic);
        (await Spark("info", "ed0210c8")).Status.Should().Be(500);

        SetSeed("MF_ed0210c8", DevMnemonic);

        (await Spark("info", "ed0210c8")).Status.Should().Be(200);
    }

    [Theory]
    [InlineData(0, DevIdentityAccount0)]
    [InlineData(1, DevIdentityAccount1)]
    public void TheDevIdentities_AreThePlainBip32SparkIdentityKeys(int account, string identity) =>
        new Mnemonic(DevMnemonic).DeriveExtKey().Derive(KeyPath.Parse($"m/8797555'/{account}'/0'")).GetPublicKey().ToHex()
            .Should().Be(identity);

    [Theory]
    [InlineData("a  b\tc\n d ", "a b c d")]
    [InlineData("a b", "a b")]
    public void NormalizeMnemonic_CollapsesWhitespace(string stored, string expected) =>
        SparkSeed.NormalizeMnemonic(stored).Should().Be(expected);

    private async Task<(int Status, SparkSignerEnvelope Envelope)> Spark(string op, string wallet)
    {
        var response = await _function.FunctionHandler(new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = "/spark/" + op,
            Body = JsonSerializer.Serialize(new { wallet })
        }, new TestLambdaContext());

        return (response.StatusCode,
            JsonSerializer.Deserialize<SparkSignerEnvelope>(response.Body, SparkSignerContract.Json)!);
    }

    /// <summary>
    /// The fake KMS decrypts to the ciphertext itself; seeds are stored with their words joined by @
    /// </summary>
    private static void SetSeed(string variable, string mnemonic, bool compromised = false) =>
        Environment.SetEnvironmentVariable(variable, JsonSerializer.Serialize(new SignPSBTConfig
        {
            AwsKmsKeyId = "mrk-test",
            EncryptedSeedphrase = Convert.ToBase64String(Encoding.UTF8.GetBytes(mnemonic.Replace(" ", "@"))),
            Compromised = compromised
        }));

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
