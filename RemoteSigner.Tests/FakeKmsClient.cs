using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;

namespace RemoteSigner.Tests;

/// <summary>
/// Fake KMS whose Encrypt/Decrypt are identity transforms, capturing requests, so the whole
/// encrypt -> decrypt -> re-derive pipeline runs without AWS credentials
/// </summary>
internal class FakeKmsClient : AmazonKeyManagementServiceClient
{
    public EncryptRequest? LastEncryptRequest;
    public DecryptRequest? LastDecryptRequest;
    public int DecryptCalls;

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
        Interlocked.Increment(ref DecryptCalls);
        return Task.FromResult(new DecryptResponse
        {
            Plaintext = new MemoryStream(request.CiphertextBlob.ToArray())
        });
    }
}
