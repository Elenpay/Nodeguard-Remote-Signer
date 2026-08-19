using System.Text;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using FluentAssertions;
using NBitcoin;
using RemoteSigner.SeedCeremony;
using Xunit;

namespace RemoteSigner.Tests;

public class SeedCeremonyTest
{
    /// <summary>
    /// The dev vector already committed in FunctionTest: fingerprint ed0210c8
    /// </summary>
    private const string DevMnemonic =
        "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

    /// <summary>
    /// Fake KMS whose Encrypt/Decrypt are identity transforms, capturing requests, so the whole
    /// encrypt -> decrypt -> re-derive pipeline runs without AWS credentials
    /// </summary>
    private class FakeKmsClient : AmazonKeyManagementServiceClient
    {
        public EncryptRequest? LastEncryptRequest;
        public DecryptRequest? LastDecryptRequest;

        public FakeKmsClient() : base(new AnonymousAWSCredentials(),
            new AmazonKeyManagementServiceConfig { RegionEndpoint = Amazon.RegionEndpoint.EUCentral1 })
        {
        }

        public override Task<EncryptResponse> EncryptAsync(EncryptRequest request,
            CancellationToken cancellationToken = default)
        {
            LastEncryptRequest = request;
            return Task.FromResult(new EncryptResponse
            {
                CiphertextBlob = new MemoryStream(request.Plaintext.ToArray())
            });
        }

        public override Task<DecryptResponse> DecryptAsync(DecryptRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDecryptRequest = request;
            return Task.FromResult(new DecryptResponse
            {
                Plaintext = new MemoryStream(request.CiphertextBlob.ToArray())
            });
        }
    }

    [Theory]
    [InlineData("regtest", "tpub")]
    [InlineData("testnet", "tpub")]
    [InlineData("mainnet", "xpub")]
    public void Derive_DevMnemonic_ProducesExpectedIdentifiers(string network, string expectedXpubPrefix)
    {
        // Act
        var result = Ceremony.Derive(new Mnemonic(DevMnemonic), Function.ParseNetwork(network),
            KeyPath.Parse("m/48'/1'"));

        // Assert
        result.MasterFingerprint.Should().Be("ed0210c8");
        result.EnvName.Should().Be("MF_ed0210c8");
        result.AccountXpub.Should().StartWith(expectedXpubPrefix);
    }

    [Fact]
    public void Derive_MatchesNodeGuardInternalWalletDerivation()
    {
        // Arrange: NodeGuard's InternalWallet.GetXPUB algorithm, computed inline
        var network = Network.RegTest;
        var expectedXpub = new Mnemonic(DevMnemonic).DeriveExtKey().GetWif(network)
            .Derive(new KeyPath("m/48'/1'")).Neuter().ToWif();

        // Act
        var result = Ceremony.Derive(new Mnemonic(DevMnemonic), network, KeyPath.Parse("m/48'/1'"));

        // Assert
        result.AccountXpub.Should().Be(expectedXpub);
        var act = () => new BitcoinExtPubKey(result.AccountXpub, network);
        act.Should().NotThrow();
    }

    [Fact]
    public void BuildEnvValue_RoundTripsThroughLambdaConfigDeserialization()
    {
        // Act
        var envValue = Ceremony.BuildEnvValue("AQIC-ciphertext", "mrk-123");

        // Assert: the lambda deserializes MF_* values with default options and case-sensitive names
        envValue.Should().Contain("\"EncryptedSeedphrase\"").And.Contain("\"AwsKmsKeyId\"");

        var config = JsonSerializer.Deserialize<SignPSBTConfig>(envValue);
        config.Should().NotBeNull();
        config!.EncryptedSeedphrase.Should().Be("AQIC-ciphertext");
        config.AwsKmsKeyId.Should().Be("mrk-123");
        config.Compromised.Should().BeFalse();
    }

    [Fact]
    public void GenerateMnemonic_Produces24UniqueEnglishWordMnemonics()
    {
        // Act
        var first = Ceremony.GenerateMnemonic();
        var second = Ceremony.GenerateMnemonic();

        // Assert
        first.Words.Should().HaveCount(24);
        var act = () => new Mnemonic(first.ToString());
        act.Should().NotThrow();
        first.ToString().Should().NotBe(second.ToString());
    }

    [Fact]
    public async Task Encrypt_ReplacesWhitespacesAndUsesSymmetricDefault()
    {
        // Arrange
        var fakeKms = new FakeKmsClient();

        // Act
        var encryptedBase64 = await new Function().EncryptSeedphrase(DevMnemonic, "mrk-123", fakeKms);

        // Assert: the KMS plaintext must be the @-joined mnemonic (KMS strips whitespaces)
        fakeKms.LastEncryptRequest.Should().NotBeNull();
        fakeKms.LastEncryptRequest!.EncryptionAlgorithm.Should().Be(EncryptionAlgorithmSpec.SYMMETRIC_DEFAULT);
        fakeKms.LastEncryptRequest.KeyId.Should().Be("mrk-123");
        Encoding.UTF8.GetString(fakeKms.LastEncryptRequest.Plaintext.ToArray())
            .Should().Be(DevMnemonic.Replace(" ", "@"));

        Convert.FromBase64String(encryptedBase64).Should()
            .BeEquivalentTo(Encoding.UTF8.GetBytes(DevMnemonic.Replace(" ", "@")));
    }

    [Fact]
    public async Task EncryptThenDecrypt_RoundTripsTheMnemonic()
    {
        // Arrange
        var fakeKms = new FakeKmsClient();
        var encryptedBase64 = await new Function().EncryptSeedphrase(DevMnemonic, "mrk-123", fakeKms);
        var config = JsonSerializer.Deserialize<SignPSBTConfig>(Ceremony.BuildEnvValue(encryptedBase64, "mrk-123"));

        // Act: the lambda's own decrypt path
        var seed = await Function.DecryptSeedphrase(fakeKms, config!);

        // Assert
        seed.Should().Be(DevMnemonic);
        fakeKms.LastDecryptRequest!.KeyId.Should().Be("mrk-123");
        fakeKms.LastDecryptRequest.EncryptionAlgorithm.Should().Be(EncryptionAlgorithmSpec.SYMMETRIC_DEFAULT);
    }

    private static CeremonyManifest BuildManifest(string network = "regtest",
        string derivationPath = "m/48'/1'", string? fingerprint = null, string? xpub = null)
    {
        var derived = Ceremony.Derive(new Mnemonic(DevMnemonic), Function.ParseNetwork(network),
            KeyPath.Parse(derivationPath));

        return new CeremonyManifest
        {
            EnvName = $"MF_{fingerprint ?? derived.MasterFingerprint}",
            EnvValue = Ceremony.BuildEnvValue("AQIC", "mrk-123"),
            MasterFingerprint = fingerprint ?? derived.MasterFingerprint,
            AccountXpub = xpub ?? derived.AccountXpub,
            DerivationPath = derivationPath,
            Network = network,
            CreatedAtUtc = "2026-01-01T00:00:00.0000000Z"
        };
    }

    [Fact]
    public void VerifyManifest_ConsistentManifest_DoesNotThrow()
    {
        var act = () => Ceremony.VerifyManifest(BuildManifest(), new Mnemonic(DevMnemonic));
        act.Should().NotThrow();
    }

    [Fact]
    public void VerifyManifest_FingerprintMismatch_Throws()
    {
        var act = () => Ceremony.VerifyManifest(BuildManifest(fingerprint: "00000000"), new Mnemonic(DevMnemonic));
        act.Should().Throw<ArgumentException>().WithMessage("*fingerprint mismatch*");
    }

    [Fact]
    public void VerifyManifest_XpubMismatch_Throws()
    {
        var wrongXpub = Ceremony.Derive(new Mnemonic(DevMnemonic), Network.RegTest, KeyPath.Parse("m/48'/0'"))
            .AccountXpub;
        var act = () => Ceremony.VerifyManifest(BuildManifest(xpub: wrongXpub), new Mnemonic(DevMnemonic));
        act.Should().Throw<ArgumentException>().WithMessage("*xpub mismatch*");
    }

    [Fact]
    public void VerifyManifest_WrongNetworkXpub_Throws()
    {
        // A manifest claiming mainnet while carrying the regtest tpub must fail on the xpub check
        var regtestXpub = Ceremony.Derive(new Mnemonic(DevMnemonic), Network.RegTest, KeyPath.Parse("m/48'/1'"))
            .AccountXpub;
        var act = () => Ceremony.VerifyManifest(BuildManifest(network: "mainnet", xpub: regtestXpub),
            new Mnemonic(DevMnemonic));
        act.Should().Throw<ArgumentException>().WithMessage("*xpub mismatch*");
    }

    [Fact]
    public void Manifest_JsonRoundTrips()
    {
        var manifest = BuildManifest();
        var roundTripped = CeremonyManifest.FromJson(manifest.ToJson());

        roundTripped.EnvName.Should().Be(manifest.EnvName);
        roundTripped.EnvValue.Should().Be(manifest.EnvValue);
        roundTripped.MasterFingerprint.Should().Be(manifest.MasterFingerprint);
        roundTripped.AccountXpub.Should().Be(manifest.AccountXpub);
        roundTripped.DerivationPath.Should().Be(manifest.DerivationPath);
        roundTripped.Network.Should().Be(manifest.Network);
    }
}
