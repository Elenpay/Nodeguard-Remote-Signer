using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using NBitcoin;
using NSpark.RemoteSigner;

namespace RemoteSigner;

/// <summary>
/// The /spark/ routes of the signer, served with the NSpark.RemoteSigner contract: every response is
/// a JSON envelope (<c>{"result":...}</c> or <c>{"error":"..."}</c>) with a 200/400/403/404/500 status.
/// Only the operations in <see cref="ServedOperations"/> are served, whatever the contract defines
/// </summary>
public class SparkRoute
{
    public const string OperatorKeysVariable = "SPARK_OPERATOR_KEYS";
    public const string ThresholdVariable = "SPARK_THRESHOLD";
    public const string AllowedReceiversVariable = "SPARK_ALLOWED_RECEIVERS";

    /// <summary>
    /// What a NodeGuard Spark wallet needs to receive over Lightning, claim and exit to an on-chain
    /// address. Not served at all: ECIES decryption, private key export, two-phase FROST nonces and
    /// adaptor keys (SSP leaf swaps); sign-leaf-frost also refuses adaptor signatures
    /// </summary>
    public static readonly IReadOnlySet<string> ServedOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        SparkSignerContract.Ops.Info,
        SparkSignerContract.Ops.IdentityPublicKey,
        SparkSignerContract.Ops.SignWithIdentity,
        SparkSignerContract.Ops.SignCompactWithIdentity,
        SparkSignerContract.Ops.DepositPublicKey,
        SparkSignerContract.Ops.LeafPublicKey,
        SparkSignerContract.Ops.StaticDepositPublicKey,
        SparkSignerContract.Ops.SignLeafFrost,
        SparkSignerContract.Ops.BuildSendTweaks,
        SparkSignerContract.Ops.BuildClaimTweaks,
        SparkSignerContract.Ops.BuildPreimageShares
    };

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

        SparkSignerPolicy policy;
        try
        {
            policy = PolicyFromEnvironment();
        }
        catch (InvalidOperationException e)
        {
            return Refused(500, op, e.Message);
        }

        var (status, envelope) = await SparkSignerHttpHandler.HandleAsync(request.RawPath, body, _seed.ResolveAsync, policy);

        if (status != 200)
        {
            await Console.Error.WriteLineAsync($"Spark {op} refused with {status}: {Error(envelope)}");
        }

        return Respond(status, envelope);
    }

    /// <summary>
    /// The operators that key tweaks and preimage shares may be built for (SPARK_OPERATOR_KEYS, with
    /// their SPARK_THRESHOLD) and the identities, besides the signer's own, that leaves may be sent to
    /// (SPARK_ALLOWED_RECEIVERS: the SSP). Without operators, every tweak and share is refused
    /// </summary>
    public static SparkSignerPolicy PolicyFromEnvironment()
    {
        var operators = PublicKeys(OperatorKeysVariable);
        var receivers = PublicKeys(AllowedReceiversVariable);
        if (operators.Count == 0)
        {
            return SparkSignerPolicy.Create([], 0, receivers);
        }

        var threshold = Environment.GetEnvironmentVariable(ThresholdVariable);
        if (!uint.TryParse(threshold, out var parsed) || parsed < 1 || parsed > operators.Count)
        {
            throw new InvalidOperationException(
                $"{ThresholdVariable} must be set to the operators' signing threshold (1 to {operators.Count})");
        }

        return SparkSignerPolicy.Create(operators, parsed, receivers);
    }

    private static List<string> PublicKeys(string variable)
    {
        var keys = (Environment.GetEnvironmentVariable(variable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var key in keys)
        {
            if (key.Length != 66 || !key.All(Uri.IsHexDigit) || !PubKey.TryCreatePubKey(Convert.FromHexString(key), out _))
            {
                throw new InvalidOperationException($"{variable} must be comma-separated compressed public keys (hex)");
            }
        }

        return keys.ToList();
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
