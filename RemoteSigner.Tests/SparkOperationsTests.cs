using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using NBitcoin;
using NSpark.RemoteSigner;
using NSpark.Signer;
using Xunit;

namespace RemoteSigner.Tests;

/// <summary>
/// NSpark's RemoteSparkSigner (the client NodeGuard uses) against the whole Lambda handler, with the
/// dev seed and a fake KMS: every served operation answers exactly as the in-process signer would,
/// and the policy pinned in the environment refuses everything else
/// </summary>
[Collection(SignerEnvironment.Collection)]
public class SparkOperationsTests : IDisposable
{
    private const string DevMnemonic =
        "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

    private static readonly byte[][] OperatorKeys = [Pub(1), Pub(2), Pub(3)];
    private static readonly byte[] Ssp = Pub(9);

    private static readonly IReadOnlyList<SoTarget> SoTargets =
    [
        new("0000000000000000000000000000000000000000000000000000000000000001", 1, OperatorKeys[0]),
        new("0000000000000000000000000000000000000000000000000000000000000002", 2, OperatorKeys[1]),
        new("0000000000000000000000000000000000000000000000000000000000000003", 3, OperatorKeys[2])
    ];

    private static readonly string[] Variables =
    [
        "MF_ed0210c8", SparkSeed.FingerprintVariable, SparkSeed.AccountVariable, SparkRoute.OperatorKeysVariable,
        SparkRoute.ThresholdVariable, SparkRoute.AllowedReceiversVariable
    ];

    private readonly SparkSigner _local = SparkSigner.FromMnemonic(DevMnemonic, 0);
    private readonly FakeKmsClient _kms = new();
    private readonly Function _function;
    private readonly RemoteSparkSigner _remote;

    public SparkOperationsTests()
    {
        Environment.SetEnvironmentVariable("MF_ed0210c8", JsonSerializer.Serialize(new SignPSBTConfig
        {
            AwsKmsKeyId = "mrk-test",
            EncryptedSeedphrase = Convert.ToBase64String(Encoding.UTF8.GetBytes(DevMnemonic.Replace(" ", "@")))
        }));
        Environment.SetEnvironmentVariable(SparkSeed.FingerprintVariable, "ed0210c8");
        Environment.SetEnvironmentVariable(SparkSeed.AccountVariable, "0");
        Environment.SetEnvironmentVariable(SparkRoute.OperatorKeysVariable, string.Join(",", OperatorKeys.Select(Hex)));
        Environment.SetEnvironmentVariable(SparkRoute.ThresholdVariable, "2");
        Environment.SetEnvironmentVariable(SparkRoute.AllowedReceiversVariable, Hex(Ssp).ToUpperInvariant());

        _function = new Function(() => _kms);
        _remote = new RemoteSparkSigner(new LambdaTransport(_function), "ed0210c8");
    }

    public void Dispose()
    {
        foreach (var variable in Variables) Environment.SetEnvironmentVariable(variable, null);
    }

    [Fact]
    public async Task Info_ReportsThePinnedPolicy()
    {
        var info = await _remote.GetInfoAsync();

        info.IdentityPublicKey.Should().Be(Hex(await _local.GetIdentityPublicKeyAsync()));
        info.OperatorKeys.Should().Equal(OperatorKeys.Select(Hex).Order(StringComparer.Ordinal));
        info.Threshold.Should().Be(2);
        info.AllowedReceivers.Should().Equal(Hex(Ssp));
    }

    [Fact]
    public async Task PublicKeys_MatchTheInProcessSigner()
    {
        (await _remote.GetIdentityPublicKeyAsync()).Should().Equal(await _local.GetIdentityPublicKeyAsync());
        (await _remote.GetDepositPublicKeyAsync()).Should().Equal(await _local.GetDepositPublicKeyAsync());
        (await _remote.GetLeafPublicKeyAsync("leaf-1")).Should().Equal(await _local.GetLeafPublicKeyAsync("leaf-1"));
        (await _remote.GetStaticDepositPublicKeyAsync(2)).Should().Equal(await _local.GetStaticDepositPublicKeyAsync(2));
    }

    [Fact]
    public async Task IdentitySignatures_MatchTheInProcessSigner()
    {
        var hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        (await _remote.SignWithIdentityKeyAsync(hash)).Should().Equal(await _local.SignWithIdentityKeyAsync(hash));
        (await _remote.SignCompactWithIdentityKeyAsync(hash)).Should()
            .Equal(await _local.SignCompactWithIdentityKeyAsync(hash));
    }

    [Fact]
    public async Task SendTweaks_ToTheSsp_AreBuiltForEveryOperator()
    {
        var batch = await _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", Ssp)], SoTargets,
            "019a0000-0000-7000-8000-000000000001", 2);

        batch.EncryptedPackageBySoId.Keys.Should().BeEquivalentTo(SoTargets.Select(t => t.SoId));
    }

    [Fact]
    public async Task SendTweaks_BackToTheSignersOwnIdentity_AreBuilt()
    {
        var own = await _local.GetIdentityPublicKeyAsync();

        var batch = await _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", own)], SoTargets,
            "019a0000-0000-7000-8000-000000000002", 2);

        batch.EncryptedPackageBySoId.Should().HaveCount(3);
    }

    [Fact]
    public async Task SendTweaks_ToAnyOtherReceiver_AreRefused()
    {
        var act = () => _remote.BuildEncryptedSendTweaksAsync(
            [new SendTweakLeafDescriptor("leaf-1", Ssp), new SendTweakLeafDescriptor("leaf-2", Pub(42))], SoTargets,
            "019a0000-0000-7000-8000-000000000003", 2);

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Fact]
    public async Task TweaksAndShares_ForAnotherOperatorSet_AreRefused()
    {
        var foreign = SoTargets.Take(2).Append(new SoTarget(SoTargets[2].SoId, 3, Pub(7))).ToList();
        var subset = SoTargets.Take(2).ToList();

        var send = () => _remote.BuildEncryptedSendTweaksAsync([new SendTweakLeafDescriptor("leaf-1", Ssp)], foreign,
            "019a0000-0000-7000-8000-000000000004", 2);
        var claim = () => _remote.BuildEncryptedClaimTweaksAsync([], subset, 2);
        var preimage = () => _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000005", foreign, 2);

        (await send.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
        (await claim.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
        (await preimage.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(3u)]
    public async Task TweaksAndShares_AtAnotherThreshold_AreRefused(uint threshold)
    {
        var act = () => _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000006", SoTargets,
            threshold);

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Fact]
    public async Task PreimageShares_ForThePinnedOperators_AreBuilt()
    {
        var bundle = await _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000007", SoTargets, 2);

        bundle.PaymentHash.Should().HaveCount(32);
        bundle.EncryptedShareBySoId.Keys.Should().BeEquivalentTo(SoTargets.Select(t => t.SoId));
    }

    [Fact]
    public async Task AdaptorSignatures_AreRefused()
    {
        var act = () => _remote.SignLeafFrostAsync("leaf-1", new byte[32], Pub(5),
            new Dictionary<string, SigningCommitment>(), adaptorPublicKey: Pub(6));

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
    }

    [Fact]
    public async Task WithoutPinnedOperators_EveryTweakAndShareIsRefused()
    {
        Environment.SetEnvironmentVariable(SparkRoute.OperatorKeysVariable, null);
        Environment.SetEnvironmentVariable(SparkRoute.ThresholdVariable, null);

        var act = () => _remote.BuildEncryptedPreimageSharesAsync("019a0000-0000-7000-8000-000000000008", SoTargets, 2);

        (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which.Status.Should().Be(403);
        (await _remote.GetIdentityPublicKeyAsync()).Should().Equal(await _local.GetIdentityPublicKeyAsync());
    }

    [Theory]
    [InlineData(SparkRoute.ThresholdVariable, null)]
    [InlineData(SparkRoute.ThresholdVariable, "0")]
    [InlineData(SparkRoute.ThresholdVariable, "4")]
    [InlineData(SparkRoute.ThresholdVariable, "two")]
    [InlineData(SparkRoute.OperatorKeysVariable, "zz")]
    [InlineData(SparkRoute.OperatorKeysVariable, "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798,02")]
    [InlineData(SparkRoute.AllowedReceiversVariable, "0479be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798")]
    public async Task AMisconfiguredPolicy_RefusesEveryRequestWithoutDecryptingTheSeed(string variable, string? value)
    {
        Environment.SetEnvironmentVariable(variable, value);

        var act = () => _remote.GetIdentityPublicKeyAsync();

        var refusal = (await act.Should().ThrowAsync<SparkRemoteSignerException>()).Which;
        refusal.Status.Should().Be(500);
        refusal.Message.Should().Contain(variable);
        _kms.DecryptCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("decrypt-ecies-with-identity")]
    [InlineData("export-static-deposit-private-key")]
    [InlineData("generate-leaf-frost-nonce")]
    [InlineData("sign-leaf-frost-with-nonce")]
    [InlineData("generate-adaptor-key")]
    public async Task OperationsOutsideTheContract_Are404WithoutDecryptingTheSeed(string op)
    {
        var (status, _) = await new LambdaTransport(_function).InvokeAsync("/spark/" + op, """{"wallet":"ed0210c8"}""",
            CancellationToken.None);

        status.Should().Be(404);
        _kms.DecryptCalls.Should().Be(0);
    }

    public static IEnumerable<object[]> ContractFixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ContractFixtures"), "*.json")
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f) })
            .OrderBy(f => (string)f[0], StringComparer.Ordinal);

    [Fact]
    public void TheSigner_ServesExactlyTheOperationsOfTheContractFixtures() =>
        ContractFixtures().Select(f => (string)f[0]).Should().BeEquivalentTo(SparkRoute.ServedOperations);

    [Theory]
    [MemberData(nameof(ContractFixtures))]
    public async Task EveryContractRequestBody_IsUnderstood(string op)
    {
        var body = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ContractFixtures", op + ".json")))
            .TrimEnd('\n');

        var (status, response) = await new LambdaTransport(_function).InvokeAsync("/spark/" + op, body, CancellationToken.None);

        // The fixtures carry placeholder keys: operations on them may be refused (403) or fail (500),
        // but the body must never be rejected as malformed or the operation as unknown
        status.Should().NotBe(400, response).And.NotBe(404, response);
    }

    private static byte[] Pub(byte seed) => new Key(Enumerable.Repeat(seed, 32).ToArray()).PubKey.ToBytes();

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>
    /// What NodeGuard's transport does over the Function URL, minus SigV4 and HTTP
    /// </summary>
    private sealed class LambdaTransport(Function function) : ISparkSignerTransport
    {
        public async Task<(int Status, string Body)> InvokeAsync(string path, string jsonBody, CancellationToken ct)
        {
            var response = await function.FunctionHandler(
                new APIGatewayHttpApiV2ProxyRequest { RawPath = path, Body = jsonBody }, new TestLambdaContext());
            return (response.StatusCode, response.Body);
        }
    }
}
