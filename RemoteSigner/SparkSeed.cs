using Amazon.KeyManagementService;
using NBitcoin;
using NSpark.RemoteSigner;
using NSpark.Signer;

namespace RemoteSigner;

/// <summary>
/// Selects and loads the one seed allowed to sign for Spark: the MF_ seed named by
/// SPARK_SEED_FINGERPRINT, at the SPARK_ACCOUNT Spark account (0 by default). Any other wallet, and a seed marked
/// Compromised, is refused. Only the derived Spark account keys are cached, for
/// SPARK_SIGNER_CACHE_SECONDS (60 by default, 0 disables); the mnemonic is never kept
/// </summary>
public class SparkSeed
{
    public const string FingerprintVariable = "SPARK_SEED_FINGERPRINT";
    public const string AccountVariable = "SPARK_ACCOUNT";
    public const string CacheSecondsVariable = "SPARK_SIGNER_CACHE_SECONDS";

    private const int DefaultCacheSeconds = 60;

    private readonly Func<IAmazonKeyManagementService> _kmsClientFactory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (string Key, ISparkSigner Signer, DateTimeOffset LoadedAt)? _cached;

    public SparkSeed(Func<IAmazonKeyManagementService> kmsClientFactory, TimeProvider time)
    {
        _kmsClientFactory = kmsClientFactory ?? throw new ArgumentNullException(nameof(kmsClientFactory));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// The signer for <paramref name="wallet"/>, which must be the master fingerprint in
    /// SPARK_SEED_FINGERPRINT. Throws <see cref="SparkSignerPolicyException"/> (403) to refuse it
    /// </summary>
    public async Task<ISparkSigner> ResolveAsync(string wallet, CancellationToken ct)
    {
        var fingerprint = Environment.GetEnvironmentVariable(FingerprintVariable);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new SparkSignerPolicyException("Spark signing is not enabled on this signer");
        }

        var allowed = ParseFingerprint(fingerprint);
        if (!HDFingerprint.TryParse(wallet, out var requested) || requested != allowed)
        {
            throw new SparkSignerPolicyException("this wallet is not enabled for Spark signing");
        }

        var account = ParseAccount(Environment.GetEnvironmentVariable(AccountVariable));
        var ttl = TimeSpan.FromSeconds(ParseCacheSeconds(Environment.GetEnvironmentVariable(CacheSecondsVariable)));
        var key = $"{allowed}/{account}";

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is { } cached && cached.Key == key && _time.GetUtcNow() - cached.LoadedAt < ttl)
            {
                return cached.Signer;
            }

            _cached = null;
            var signer = await LoadAsync(allowed, account);
            if (ttl > TimeSpan.Zero)
            {
                _cached = (key, signer, _time.GetUtcNow());
            }

            return signer;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ISparkSigner> LoadAsync(HDFingerprint fingerprint, int account)
    {
        var config = Function.GetConfig(fingerprint)
                     ?? throw new InvalidOperationException($"{FingerprintVariable} names {fingerprint}, but MF_{fingerprint} is not configured");

        if (config.Compromised)
        {
            throw new SparkSignerPolicyException($"the seed {fingerprint} is marked Compromised and may not sign for Spark");
        }

        var mnemonic = NormalizeMnemonic(await Function.DecryptSeedphrase(_kmsClientFactory(), config));

        // Never echo the phrase: parsing errors are reported without their message
        HDFingerprint actual;
        ISparkSigner signer;
        try
        {
            actual = new Mnemonic(mnemonic).DeriveExtKey().GetPublicKey().GetHDFingerPrint();
            signer = SparkSigner.FromMnemonic(mnemonic, account);
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"the seed MF_{fingerprint} is not a valid BIP-39 mnemonic");
        }

        if (actual != fingerprint)
        {
            throw new InvalidOperationException(
                $"the seed in MF_{fingerprint} has master fingerprint {actual}, refusing to sign for Spark with it");
        }

        return signer;
    }

    /// <summary>
    /// Collapses any whitespace run to a single space: BIP-39 validation splits on single spaces, and
    /// a stray double space must not derive a different (empty) wallet
    /// </summary>
    public static string NormalizeMnemonic(string mnemonic) =>
        string.Join(' ', mnemonic.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static HDFingerprint ParseFingerprint(string value) =>
        HDFingerprint.TryParse(value.Trim(), out var fingerprint)
            ? fingerprint
            : throw new InvalidOperationException($"{FingerprintVariable} is not a master fingerprint (8 hex characters)");

    private static int ParseAccount(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0
            : int.TryParse(value, out var account) && account >= 0
                ? account
                : throw new InvalidOperationException($"{AccountVariable} must be the Spark account index (0 or more)");

    private static int ParseCacheSeconds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DefaultCacheSeconds
            : int.TryParse(value, out var seconds) && seconds >= 0
                ? seconds
                : throw new InvalidOperationException($"{CacheSecondsVariable} must be a number of seconds (0 or more)");
}
