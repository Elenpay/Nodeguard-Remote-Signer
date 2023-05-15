using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Runtime;
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
            var kmsClient = new AmazonKeyManagementServiceClient(new StoredProfileAWSCredentials("default"),
                RegionEndpoint.EUCentral1);
#else
            var kmsClient = new AmazonKeyManagementServiceClient();
#endif

            var network = ParseNetwork(requestBody.Network);

            Console.WriteLine($"Network: {network}");

            if (PSBT.TryParse(requestBody.Psbt, network, out var parsedPSBT))
            {
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

                    var masterFingerPrint = $"MF_{inputPSBTMasterFingerPrint}";
                    var configJson = Environment.GetEnvironmentVariable(masterFingerPrint);


                    if (configJson != null)
                    {
                        var config = JsonSerializer.Deserialize<SignPSBTConfig>(configJson);

                        if (config == null)
                        {
                            var message = "The config could not be deserialized";
                            await Console.Error.WriteLineAsync(message);
                            throw new ArgumentException(message, nameof(config));
                        }

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
                        if (requestBody.EnforcedSighash != null)
                        {
                            psbtInput.SighashType = requestBody.EnforcedSighash;

                            Console.WriteLine($"Enforced sighash: {psbtInput.SighashType:G}");
                        }


                        var key = bitcoinExtKey
                            .Derive(derivationPath.KeyPath)
                            .PrivateKey;

                        //Log
                        Console.WriteLine(
                            $"Signing PSBT input:{psbtInput.Index} with master fingerprint: {fingerPrint} on derivation path: {derivationPath.KeyPath} with pubkey: {key.PubKey.ToHex()}");
                        
                        var partialSigsCountBeforeSigning = psbtInput.PartialSigs.Count(x=> x.Key == key.PubKey);

                        //We sign the input
                        psbtInput.Sign(key);

                        //We check that the partial signatures number has changed, otherwise finalize inmediately
                        var partialSigsCountAfterSignature =
                            parsedPSBT.Inputs.Sum(x => x.PartialSigs.Count(x=> x.Key == key.PubKey));

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

                var result = new SignPSBTResponse(parsedPSBT.ToBase64());

                response = new APIGatewayHttpApiV2ProxyResponse()
                {
                    Body = JsonSerializer.Serialize(result),
                    IsBase64Encoded = false,
                    StatusCode = 200
                };
            }


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

        var kmsClient = new AmazonKeyManagementServiceClient();

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