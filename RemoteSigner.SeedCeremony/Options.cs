using Amazon;
using Amazon.KeyManagementService;
using Amazon.Runtime.CredentialManagement;

namespace RemoteSigner.SeedCeremony;

/// <summary>
/// Raised on invalid invocations; mapped to exit code 2 with the usage text
/// </summary>
public sealed class UsageException : Exception
{
    public UsageException(string message) : base(message)
    {
    }
}

/// <summary>
/// Minimal --flag value parser. No third-party CLI framework on purpose: this tool handles seed
/// material, so the whole argument-handling surface should be reviewable at a glance
/// </summary>
public sealed class Options
{
    private readonly Dictionary<string, string> _values = new();

    public static Options Parse(IReadOnlyList<string> args, params string[] allowedFlags)
    {
        var options = new Options();

        for (var i = 0; i < args.Count; i++)
        {
            var flag = args[i];

            if (!flag.StartsWith("--", StringComparison.Ordinal))
                throw new UsageException($"Unexpected argument '{flag}', flags start with --");

            if (!allowedFlags.Contains(flag))
                throw new UsageException($"Unknown flag '{flag}'");

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new UsageException($"Flag '{flag}' requires a value");

            options._values[flag] = args[++i];
        }

        return options;
    }

    public string Require(string flag)
    {
        return _values.TryGetValue(flag, out var value)
            ? value
            : throw new UsageException($"Missing required flag '{flag}'");
    }

    public string? Get(string flag)
    {
        return _values.GetValueOrDefault(flag);
    }

    public string GetOrDefault(string flag, string defaultValue)
    {
        return _values.GetValueOrDefault(flag, defaultValue);
    }

    /// <summary>
    /// Builds a KMS client from --profile/--region when given, otherwise the AWS SDK default
    /// credential chain (env vars, default profile, SSO). Never takes key material as arguments
    /// </summary>
    public IAmazonKeyManagementService CreateKmsClient()
    {
        var config = new AmazonKeyManagementServiceConfig();

        var region = Get("--region");
        if (region != null) config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

        var profile = Get("--profile");
        if (profile == null) return new AmazonKeyManagementServiceClient(config);

        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetAWSCredentials(profile, out var credentials))
            throw new UsageException($"AWS profile '{profile}' was not found");

        return new AmazonKeyManagementServiceClient(credentials, config);
    }
}
