using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Runtime.CredentialManagement;
using NBitcoin;

[assembly:
    LambdaSerializer(typeof(SourceGeneratorLambdaJsonSerializer<RemoteSigner.HttpApiJsonSerializerContext>))]

namespace RemoteSigner;

[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
public partial class HttpApiJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// DTO used to deserialize the env var value which key is a master fingerprint of the PSBT
/// </summary>
public class SignPSBTConfig
{
    /// <summary>
    /// Encrypted seed phrase
    /// </summary>
    public string EncryptedSeedphrase { get; set; }

    /// <summary>
    /// AWS KMS Key Id used to decrypt the encrypted seedphrase
    /// </summary>
    public string AwsKmsKeyId { get; set; }

    /// <summary>
    /// When true, this seed may only co-sign true multisig inputs (threshold of 2 or more), so its
    /// signature alone can never move funds. Meant for retired/rotated seeds: it contains
    /// Lambda-mediated misuse (e.g. a compromised caller draining legacy single-sig wallets), it is
    /// not a protection against a party holding the seed plaintext. Absent in existing env vars,
    /// which deserializes to false.
    /// </summary>
    public bool Compromised { get; set; }
}

public class Function
{
    /// <summary>
    /// A lambda function that takes a psbt and signs it, it it assumed that the psbt inputs come from the same wallet
    /// </summary>MF
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context)
    {
        var response = new APIGatewayHttpApiV2ProxyResponse();

        try
        {
            var requestBody = JsonSerializer.Deserialize<SignPSBTRequest>(request.Body);
            if (requestBody == null) throw new ArgumentNullException(nameof(requestBody), "Request body not found");

#if DEBUG
            //AWS SDK v4 removed StoredProfileAWSCredentials; resolve the "default" profile explicitly
            if (!new CredentialProfileStoreChain().TryGetAWSCredentials("default", out var debugCredentials))
                throw new InvalidOperationException("AWS profile 'default' was not found");
            var kmsClient = new AmazonKeyManagementServiceClient(debugCredentials, RegionEndpoint.EUCentral1);
#else
            var kmsClient = new AmazonKeyManagementServiceClient();
#endif

            Task<string> GetSeed(RootedKeyPath derivationPath) => DecryptSeed(kmsClient, derivationPath);
            var result = await SignPSBT(requestBody.Psbt, requestBody.Network, requestBody.EnforcedSighash, GetSeed);

            response = new APIGatewayHttpApiV2ProxyResponse()
            {
                Body = JsonSerializer.Serialize(result),
                IsBase64Encoded = false,
                StatusCode = 200
            };


            Console.WriteLine($"Signing request finished");
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync(e.Message);
            response = new APIGatewayHttpApiV2ProxyResponse
            {
                Body = e.Message,
                IsBase64Encoded = false,
                StatusCode = 500
            };
        }

        return response;
    }

    public async Task<SignPSBTResponse?> SignPSBT(string psbt, string networkStr, SigHash? enforcedSighash, Func<RootedKeyPath?, Task<string?>> getSeed)
    {
        var network = ParseNetwork(networkStr);

        Console.WriteLine($"Network: {network}");

        if (PSBT.TryParse(psbt, network, out var parsedPSBT))
        {
            parsedPSBT.AssertSanity();

            foreach (var psbtInput in parsedPSBT.Inputs)
            {
                //We search for a fingerprint that can be used as a key for getting the config (env-var)
                //Ideally, only fingerprints of the FundsManager signer wallet are set as env vars
                var derivationPath = psbtInput.HDKeyPaths.Values.SingleOrDefault(x =>
                    Environment.GetEnvironmentVariable($"MF_{x.MasterFingerprint.ToString()}") != null);
                if (derivationPath == null)
                {
                    throw new ArgumentException(
                        "Invalid PSBT, the derivation path and the signing configuration cannot be found for none of the master fingerprints of all the pub keys",
                        nameof(derivationPath));
                }

                var inputPSBTMasterFingerPrint = derivationPath.MasterFingerprint;

                var config = GetConfig(inputPSBTMasterFingerPrint);
                if (config is { Compromised: true })
                {
                    EnsureCompromisedSeedOnlyCoSignsMultisig(psbtInput, inputPSBTMasterFingerPrint);
                }

                var seed = await getSeed(derivationPath);
                if (seed != null)
                {
                    var extKey = new Mnemonic(seed).DeriveExtKey();
                    var bitcoinExtKey = extKey.GetWif(network);


                    //Validate global xpubs
                    await ValidateXPub(parsedPSBT, bitcoinExtKey);

                    var fingerPrint = bitcoinExtKey.GetPublicKey().GetHDFingerPrint();

                    if (fingerPrint != inputPSBTMasterFingerPrint)
                    {
                        var mismatchingFingerprint =
                            $"The master fingerprint from the input does not match the master fingerprint from the encrypted seedphrase master fingerprint";

                        throw new ArgumentException(mismatchingFingerprint, nameof(fingerPrint));
                    }

                    //We can enforce the sighash for all the inputs in the request in case the PSBT was not modified or serialized correctly.
                    if (enforcedSighash != null)
                    {
                        psbtInput.SighashType = enforcedSighash;

                        Console.WriteLine($"Enforced sighash: {psbtInput.SighashType:G}");
                    }


                    var key = bitcoinExtKey
                        .Derive(derivationPath.KeyPath)
                        .PrivateKey;

                    //Log
                    Console.WriteLine(
                        $"Signing PSBT input:{psbtInput.Index} with master fingerprint: {fingerPrint} on derivation path: {derivationPath.KeyPath} with pubkey: {key.PubKey.ToHex()}");

                    var partialSigsCountBeforeSigning = parsedPSBT.Inputs.Sum(x => x.PartialSigs.Count(x => x.Key == key.PubKey));

                    //We sign the input
                    psbtInput.Sign(key);

                    //We check that the partial signatures number has changed, otherwise finalize inmediately
                    var partialSigsCountAfterSignature =
                        parsedPSBT.Inputs.Sum(x => x.PartialSigs.Count(x => x.Key == key.PubKey));

                    //We should have added a signature for each input, plus already existing signatures
                    var expectedPartialSigs = partialSigsCountBeforeSigning + 1;

                    if (partialSigsCountAfterSignature == 0 ||
                        partialSigsCountAfterSignature != expectedPartialSigs)
                    {
                        var invalidNoOfPartialSignatures =
                            $"Invalid expected number of partial signatures after signing the PSBT, expected: {expectedPartialSigs}, actual: {partialSigsCountAfterSignature}";

                        throw new ArgumentException(
                            invalidNoOfPartialSignatures);
                    }
                }
            }

            //We check that the PSBT is still valid after signing
            parsedPSBT.AssertSanity();

            return new SignPSBTResponse(parsedPSBT.ToBase64());
        }

        return null;
    }

    /// <summary>
    /// Reads and deserializes the MF_{fingerprint} env var holding the signing configuration for a
    /// master fingerprint. Returns null when no configuration exists for that fingerprint.
    /// </summary>
    /// <param name="masterFingerprint"></param>
    public static SignPSBTConfig? GetConfig(HDFingerprint masterFingerprint)
    {
        var configJson = Environment.GetEnvironmentVariable($"MF_{masterFingerprint}");

        if (configJson == null) return null;

        var config = JsonSerializer.Deserialize<SignPSBTConfig>(configJson);

        if (config == null)
        {
            var message = "The config could not be deserialized";
            Console.Error.WriteLine(message);
            throw new ArgumentException(message, nameof(config));
        }

        return config;
    }

    /// <summary>
    /// Guard applied to seeds whose config is marked Compromised: the input being signed must be a
    /// true multisig (threshold of 2 or more signatures), so this seed's signature alone can never
    /// move funds. The script is taken from the input's signable coin, which NBitcoin only resolves
    /// when the witness/redeem script is consistent with the UTXO's scriptPubKey.
    /// </summary>
    /// <param name="psbtInput"></param>
    /// <param name="fingerprint"></param>
    private static void EnsureCompromisedSeedOnlyCoSignsMultisig(PSBTInput psbtInput, HDFingerprint fingerprint)
    {
        var signableCoin = psbtInput.GetSignableCoin(out var coinError);

        if (signableCoin == null)
        {
            throw new ArgumentException(
                $"The seed for master fingerprint {fingerprint} is marked as compromised and the signable coin of input {psbtInput.Index} could not be resolved: {coinError}",
                nameof(psbtInput));
        }

        var multisigParameters = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(signableCoin.GetScriptCode());

        if (multisigParameters == null || multisigParameters.SignatureCount < 2)
        {
            throw new ArgumentException(
                $"The seed for master fingerprint {fingerprint} is marked as compromised, refusing to sign the non-multisig input {psbtInput.Index}; a compromised seed may only co-sign multisig inputs requiring at least 2 signatures",
                nameof(psbtInput));
        }
    }

    private static async Task<string?> DecryptSeed(AmazonKeyManagementServiceClient kmsClient, RootedKeyPath? derivationPath)
    {
        var config = GetConfig(derivationPath.MasterFingerprint);

        if (config == null) return null;

        return await DecryptSeedphrase(kmsClient, config);
    }

    /// <summary>
    /// Decrypts the seedphrase of a signing configuration with AWS KMS and restores the original
    /// whitespaces (the words are stored joined with @ because AWS KMS removes whitespaces)
    /// </summary>
    /// <param name="kmsClient"></param>
    /// <param name="config"></param>
    /// <returns>The plaintext mnemonic</returns>
    public static async Task<string> DecryptSeedphrase(IAmazonKeyManagementService kmsClient, SignPSBTConfig config)
    {
        var decryptedSeed = await kmsClient.DecryptAsync(new DecryptRequest
        {
            CiphertextBlob = new MemoryStream(Convert.FromBase64String(config.EncryptedSeedphrase)),
            EncryptionAlgorithm = EncryptionAlgorithmSpec.SYMMETRIC_DEFAULT,
            KeyId = config.AwsKmsKeyId
        });

        if (decryptedSeed == null)
        {
            var message = "The seedphrase could not be decrypted / found";

            throw new ArgumentException(message, nameof(decryptedSeed));
        }

        var array = decryptedSeed.Plaintext.ToArray();

        //The seedphrase words were originally splitted with @ instead of whitespaces due to AWS removing them on encryption
        var seed = Encoding.UTF8.GetString(array).Replace("@", " ");

        return seed;
    }

    public static Network ParseNetwork(string upperCaseNetwork)
    {
        upperCaseNetwork = upperCaseNetwork.ToUpperInvariant();
        return upperCaseNetwork switch
        {
            "REGTEST" => Network.RegTest,
            "MAINNET" => Network.Main,
            "MAIN" => Network.Main, //NBitcoin uses "Main" as its network name for mainnet
            "TESTNET" => Network.TestNet,
            _ => throw new ArgumentException("Network not recognized")
        };
    }

    /// <summary>
    /// Checks that the global xpubs psbt section contains the expected xpub
    /// </summary>
    /// <param name="psbt"></param>
    /// <param name="masterXpriv"></param>
    public async Task ValidateXPub(PSBT psbt, BitcoinExtKey masterXpriv)
    {
        if (psbt == null) throw new ArgumentNullException(nameof(psbt));
        if (masterXpriv == null) throw new ArgumentNullException(nameof(masterXpriv));

        //Get the master fingerprint

        var fingerprint = masterXpriv.GetPublicKey().GetHDFingerPrint();

        //We search for the fingerprint in the global xpubs
        var entryExists = psbt.GlobalXPubs.Any(x => x.Value.MasterFingerprint == fingerprint);

        if (!entryExists)
        {
            var message =
                $"The PSBT does not contain the expected wallet xpub, the fingerprint {fingerprint} is not present in the global xpubs";
            throw new ArgumentException(message, nameof(fingerprint));
        }

        var xpubEntry = psbt.GlobalXPubs.Single(x => x.Value.MasterFingerprint == fingerprint);

        //Generate bitcoinextpubkey
        var bitcoinExtPubKey = masterXpriv.Derive(xpubEntry.Value.KeyPath).Neuter();


        if (xpubEntry.Key != bitcoinExtPubKey)
        {
            var message =
                $"The PSBT does not contain the expected wallet xpub, the xpub does not match the expected one, received: {xpubEntry.Key}, expected: {bitcoinExtPubKey} ";
            throw new ArgumentException(message, nameof(bitcoinExtPubKey));
        }
    }

    /// <summary>
    /// Aux method used to generate an encrypted seed, it is added for generating new ones with a unit test
    /// </summary>
    /// <param name="mnemonicString"></param>
    /// <param name="keyId"></param>
    /// <returns>Base64 encrypted seedphrase</returns>
    public async Task<string> EncryptSeedphrase(string mnemonicString, string keyId)
    {
        return await EncryptSeedphrase(mnemonicString, keyId, new AmazonKeyManagementServiceClient());
    }

    /// <summary>
    /// Overload of <see cref="EncryptSeedphrase(string,string)"/> with an injected KMS client so
    /// callers (e.g. the seed-ceremony CLI) can control credentials/region and tests can fake KMS
    /// </summary>
    /// <param name="mnemonicString"></param>
    /// <param name="keyId"></param>
    /// <param name="kmsClient"></param>
    /// <returns>Base64 encrypted seedphrase</returns>
    public async Task<string> EncryptSeedphrase(string mnemonicString, string keyId, IAmazonKeyManagementService kmsClient)
    {
        if (string.IsNullOrWhiteSpace(mnemonicString))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(mnemonicString));
        if (string.IsNullOrWhiteSpace(keyId))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(keyId));

        try
        {
            var mnemonic = new Mnemonic(mnemonicString);
        }
        catch (Exception e)
        {
            const string invalidMnemonicItContainsWhitespaces = "Invalid mnemonic";

            await Console.Error.WriteLineAsync(invalidMnemonicItContainsWhitespaces);

            await Console.Error.WriteLineAsync(e.Message);

            throw;
        }

        //To avoid KMS removing whitespaces and dismantling the seedphrase
        mnemonicString = mnemonicString.Replace(" ", "@");

        var encryptedSeed = await kmsClient.EncryptAsync(new EncryptRequest
        {
            EncryptionAlgorithm = EncryptionAlgorithmSpec.SYMMETRIC_DEFAULT,
            Plaintext = new MemoryStream(Encoding.UTF8.GetBytes(mnemonicString)), //UTF8 Encoding
            KeyId = keyId
        });

        var encryptedSeedBase64 = Convert.ToBase64String(encryptedSeed.CiphertextBlob.ToArray());

        return encryptedSeedBase64;
    }
}

public record SignPSBTRequest(string Psbt, SigHash? EnforcedSighash, string Network);

public record SignPSBTResponse(string? Psbt);