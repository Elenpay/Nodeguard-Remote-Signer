using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using NSpark.RemoteSigner;
using NSpark.Signer;

namespace RemoteSigner;

/// <summary>
/// The /spark/ routes of the signer, served with the NSpark.RemoteSigner contract: every response is
/// a JSON envelope (<c>{"result":...}</c> or <c>{"error":"..."}</c>) with a 200/400/403/404/500 status.
/// No seed is enabled for Spark yet, so every wallet is refused
/// </summary>
public class SparkRoute
{
    private static readonly SparkSignerPolicy NoPolicy = SparkSignerPolicy.Create([], 0, []);

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(APIGatewayHttpApiV2ProxyRequest request)
    {
        string? body;
        try
        {
            body = request.IsBase64Encoded && request.Body != null
                ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
                : request.Body;
        }
        catch (FormatException)
        {
            return Respond(400, JsonSerializer.Serialize(new { error = "request body is not valid base64" }, SparkSignerContract.Json));
        }

        var (status, envelope) = await SparkSignerHttpHandler.HandleAsync(request.RawPath, body, ResolveSigner, NoPolicy);

        if (status != 200)
        {
            await Console.Error.WriteLineAsync($"Spark request {request.RawPath} refused with {status}");
        }

        return Respond(status, envelope);
    }

    private static Task<ISparkSigner> ResolveSigner(string wallet, CancellationToken ct) =>
        Task.FromException<ISparkSigner>(new SparkSignerPolicyException("Spark signing is not enabled on this signer"));

    private static APIGatewayHttpApiV2ProxyResponse Respond(int status, string envelope) => new()
    {
        StatusCode = status,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = envelope,
        IsBase64Encoded = false
    };
}
