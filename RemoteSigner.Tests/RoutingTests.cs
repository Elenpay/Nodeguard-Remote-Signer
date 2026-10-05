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
/// The whole Lambda handler with a fake KMS: only the /spark/ prefix may reach Spark code, and every
/// other path must keep signing PSBTs byte-for-byte as before
/// </summary>
[Collection(SignerEnvironment.Collection)]
public class RoutingTests : IDisposable
{
    private const string Mnemonic =
        "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

    private readonly FakeKmsClient _kms = new();
    private readonly Function _function;
    private int _kmsClientsCreated;

    public RoutingTests()
    {
        // The fake KMS decrypts to the ciphertext itself; seeds are stored with their words joined by @
        var config = new SignPSBTConfig
        {
            AwsKmsKeyId = "mrk-test",
            EncryptedSeedphrase = Convert.ToBase64String(Encoding.UTF8.GetBytes(Mnemonic.Replace(" ", "@")))
        };
        Environment.SetEnvironmentVariable("MF_ed0210c8", JsonSerializer.Serialize(config));

        _function = new Function(() =>
        {
            _kmsClientsCreated++;
            return _kms;
        });
    }

    public void Dispose() => Environment.SetEnvironmentVariable("MF_ed0210c8", null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/sign")]
    [InlineData("/spark")]
    [InlineData("/sparkle/info")]
    [InlineData("/SPARK/info")]
    public async Task NonSparkPaths_SignThePsbtExactlyAsBefore(string? path)
    {
        var golden = Golden("p2wsh-2of3-all");
        var body = JsonSerializer.Serialize(new SignPSBTRequest(golden.Psbt, Enum.Parse<SigHash>(golden.SigHash), golden.Network));

        var response = await Handle(path, body);

        response.StatusCode.Should().Be(200);
        JsonSerializer.Deserialize<SignPSBTResponse>(response.Body)!.Psbt.Should().Be(golden.Expected);
        _kms.LastDecryptRequest!.KeyId.Should().Be("mrk-test");
    }

    [Fact]
    public async Task PsbtErrors_KeepTheirPlainTextResponse()
    {
        var response = await Handle("/", "not json");

        response.StatusCode.Should().Be(500);
        response.Headers.Should().BeNull();
        response.Body.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("/spark/info")]
    [InlineData("//spark/info")]
    [InlineData("/spark/identity-public-key")]
    public async Task SparkPaths_WithoutASparkSeed_NeverReachTheSeeds(string path)
    {
        var response = await Handle(path, """{"wallet":"ed0210c8"}""");

        response.StatusCode.Should().Be(403);
        response.Headers["Content-Type"].Should().Be("application/json");
        Envelope(response).Error.Should().Contain("not enabled");
        _kmsClientsCreated.Should().Be(0);
        _kms.DecryptCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("/spark/sign-leaf-frost")]
    [InlineData("/spark/export-static-deposit-private-key")]
    [InlineData("/spark/")]
    [InlineData("/spark/info/extra")]
    public async Task OperationsTheSignerDoesNotServe_Are404WithoutTouchingTheSeeds(string path)
    {
        var response = await Handle(path, """{"wallet":"ed0210c8"}""");

        response.StatusCode.Should().Be(404);
        Envelope(response).Error.Should().Contain("not served");
        _kmsClientsCreated.Should().Be(0);
    }

    [Fact]
    public async Task SparkPath_WithAPsbtRequest_IsABadRequest()
    {
        var golden = Golden("p2wsh-2of3-all");
        var body = JsonSerializer.Serialize(new SignPSBTRequest(golden.Psbt, SigHash.All, golden.Network));

        var response = await Handle("/spark/info", body);

        response.StatusCode.Should().Be(400);
        Envelope(response).Error.Should().Contain("wallet");
        _kms.DecryptCalls.Should().Be(0);
    }

    [Fact]
    public async Task SparkPath_DecodesABase64Body()
    {
        var response = await _function.FunctionHandler(new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = "/spark/info",
            IsBase64Encoded = true,
            Body = Convert.ToBase64String("""{"wallet":"ed0210c8"}"""u8.ToArray())
        }, new TestLambdaContext());

        // Reaching the wallet check means the body was decoded and parsed
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SparkPath_WithAnInvalidBase64Body_IsABadRequest()
    {
        var response = await _function.FunctionHandler(new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = "/spark/info",
            IsBase64Encoded = true,
            Body = "not base64!"
        }, new TestLambdaContext());

        response.StatusCode.Should().Be(400);
        Envelope(response).Error.Should().Contain("base64");
    }

    private Task<APIGatewayHttpApiV2ProxyResponse> Handle(string? path, string body) =>
        _function.FunctionHandler(new APIGatewayHttpApiV2ProxyRequest { RawPath = path, Body = body },
            new TestLambdaContext());

    private static SparkSignerEnvelope Envelope(APIGatewayHttpApiV2ProxyResponse response) =>
        JsonSerializer.Deserialize<SparkSignerEnvelope>(response.Body, SparkSignerContract.Json)!;

    private static GoldenSignatureTests.GoldenCase Golden(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-psbt-signatures.json");
        return JsonSerializer.Deserialize<List<GoldenSignatureTests.GoldenCase>>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Single(c => c.Name == name);
    }
}
