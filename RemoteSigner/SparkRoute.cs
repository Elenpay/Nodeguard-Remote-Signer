using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using NSpark.RemoteSigner;

namespace RemoteSigner;

/// <summary>
/// The /spark/ routes of the signer, served with the NSpark.RemoteSigner contract: every response is
/// a JSON envelope (<c>{"result":...}</c> or <c>{"error":"..."}</c>) with a 200/400/403/404/500 status.
/// Only the operations in <see cref="ServedOperations"/> are served, whatever the contract defines
/// </summary>
public class SparkRoute
{
    /// <summary>
    /// Read-only operations: the signer's identity, so callers can pin it before using it
    /// </summary>
    public static readonly IReadOnlySet<string> ServedOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        SparkSignerContract.Ops.Info,
        SparkSignerContract.Ops.IdentityPublicKey
    };

    private static readonly SparkSignerPolicy NoPolicy = SparkSignerPolicy.Create([], 0, []);

    private readonly SparkSeed _seed;

    public SparkRoute(SparkSeed seed)
    {
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(APIGatewayHttpApiV2ProxyRequest request)
    {
        var op = OperationOf(request.RawPath);
        if (!ServedOperations.Contains(op))
        {
            return Refused(404, op, $"operation '{op}' is not served by this signer");
        }

        string? body;
        try
        {
            body = request.IsBase64Encoded && request.Body != null
                ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
                : request.Body;
        }
        catch (FormatException)
        {
            return Refused(400, op, "request body is not valid base64");
        }

        var (status, envelope) = await SparkSignerHttpHandler.HandleAsync(request.RawPath, body, _seed.ResolveAsync, NoPolicy);

        if (status != 200)
        {
            await Console.Error.WriteLineAsync($"Spark {op} refused with {status}: {Error(envelope)}");
        }

        return Respond(status, envelope);
    }

    /// <summary>
    /// The operation of a /spark/ path, with repeated slashes collapsed as the router does
    /// </summary>
    private static string OperationOf(string? path) =>
        string.Join('/', (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1));

    private static string? Error(string envelope)
    {
        try
        {
            return JsonSerializer.Deserialize<SparkSignerEnvelope>(envelope, SparkSignerContract.Json)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static APIGatewayHttpApiV2ProxyResponse Refused(int status, string op, string error)
    {
        Console.Error.WriteLine($"Spark {op} refused with {status}: {error}");
        return Respond(status, JsonSerializer.Serialize(new { error }, SparkSignerContract.Json));
    }

    private static APIGatewayHttpApiV2ProxyResponse Respond(int status, string envelope) => new()
    {
        StatusCode = status,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = envelope,
        IsBase64Encoded = false
    };
}
