using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Xunit;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using NBitcoin;

namespace RemoteSigner.Tests;

public class FunctionTest : IDisposable
{
    private const string awsKmsKeyId = "mrk-51c7f523a83442718f2f71a986b7c7fb";
    /// <summary>
    /// Aux method that sets the env vars for tests
    /// </summary>
    /// <returns></returns>
    public FunctionTest()
    {

        var config = new SignPSBTConfig
        {
            AwsKmsKeyId = awsKmsKeyId,
            EncryptedSeedphrase =
                "AQICAHhe+d+teMKKbR5xXmbXpF4qfzisFjJj5Y6Asix6NDVAOQFrraM7Hcu2ue1yvNu8yuXhAAABBzCCAQMGCSqGSIb3DQEHBqCB9TCB8gIBADCB7AYJKoZIhvcNAQcBMB4GCWCGSAFlAwQBLjARBAyPGf7Q4fYr+eXavfcCARCAgb53WC0LWorJn/rF02h9+rDDkO7FRYoSzXfS0QFTNgAT6n+HUZcr7xHhxw8zZgFGG30bu/m33kSSLRo5EsUU8nX8n6Hud61acTd0VJxmwlKugFUxJ3YVODtWIZCdVfCUmR+JekSVeJWiairCYIQEex4ixdaJcFVzpjIYlBnqBBEklODwvSsEjBUEuNFVwZeda168rsZw75aSZ3ToSgqsHg0O+Vyz77EwmH4Clck9R0kMTDfWuFcvVwBtqkOtrPqt"
        };
        var configJSon = JsonSerializer.Serialize(config);
        Environment.SetEnvironmentVariable("MF_ed0210c8", configJSon);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MF_ed0210c8", null);
    }

    [Theory]
    [InlineData("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==", SigHash.All, "Regtest")]
    [InlineData("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==", SigHash.None, "Regtest")]
    [InlineData("cHNidP8BAKQBAAAAAwXAGAr1uq/i06r+EW2SjFMKQp3Pg0q+eJcqQ9iWKLrMAAAAAAD/////E9fa5RGuwTHL6xLgYpdDDXz2piFg7F9UWPZXyAZdM8kAAAAAAP////9YszwapNpRRrI7LFJglswjr9SLkao+ywZq/AtjZMDChAAAAAAA/////wGsJgMGAAAAABYAFJO3OqgJq4Mr3qWsDV1YUNj0aDHQAAAAAE8BBDWHzwN9uUaNAAAAAIDetxqi8U7tfzci9EleGtB59Z/A84PlsnvZ229emSEgA6/rPqXCpw3EqihylkpeL/EXKvNGahv+0Dm2JmVJf8VGEO0CEMgwAACAAQAAgAAAAAAAAQEfjGkeAAAAAAAWABQCmza03sKejExNXjBVHR8UyJJWpgEDBAIAAAAiBgIUCFqogmf/kpcaV+42XlzRzx4OWdqxWDesHZkVuK70sBjtAhDIMAAAgAEAAIAAAAAAAAAAAD0AAAAAAQEfyFrXBQAAAAAWABR9cTsoys8smwP2qmjSQM06tKj4fwEDBAIAAAAiBgPzpHxMZtZ1f3rW4L0yyV4gPS45MGMDooXHpvIhAGbvtBjtAhDIMAAAgAEAAIAAAAAAAQAAAC8AAAAAAQEfMGYNAAAAAAAWABRmqBq5qDk2/37GDEq0zM5HXigXjwEDBAIAAAAiBgJr4vl26F2PI9F3JT63vX1qltyDoaAOZ/D212UNJ3u1XhjtAhDIMAAAgAEAAIAAAAAAAQAAADQAAAAAAA==", SigHash.All, "Regtest")]
    [InlineData("cHNidP8BAKQBAAAAAwXAGAr1uq/i06r+EW2SjFMKQp3Pg0q+eJcqQ9iWKLrMAAAAAAD/////E9fa5RGuwTHL6xLgYpdDDXz2piFg7F9UWPZXyAZdM8kAAAAAAP////9YszwapNpRRrI7LFJglswjr9SLkao+ywZq/AtjZMDChAAAAAAA/////wGsJgMGAAAAABYAFJO3OqgJq4Mr3qWsDV1YUNj0aDHQAAAAAE8BBDWHzwN9uUaNAAAAAIDetxqi8U7tfzci9EleGtB59Z/A84PlsnvZ229emSEgA6/rPqXCpw3EqihylkpeL/EXKvNGahv+0Dm2JmVJf8VGEO0CEMgwAACAAQAAgAAAAAAAAQEfjGkeAAAAAAAWABQCmza03sKejExNXjBVHR8UyJJWpgEDBAIAAAAiBgIUCFqogmf/kpcaV+42XlzRzx4OWdqxWDesHZkVuK70sBjtAhDIMAAAgAEAAIAAAAAAAAAAAD0AAAAAAQEfyFrXBQAAAAAWABR9cTsoys8smwP2qmjSQM06tKj4fwEDBAIAAAAiBgPzpHxMZtZ1f3rW4L0yyV4gPS45MGMDooXHpvIhAGbvtBjtAhDIMAAAgAEAAIAAAAAAAQAAAC8AAAAAAQEfMGYNAAAAAAAWABRmqBq5qDk2/37GDEq0zM5HXigXjwEDBAIAAAAiBgJr4vl26F2PI9F3JT63vX1qltyDoaAOZ/D212UNJ3u1XhjtAhDIMAAAgAEAAIAAAAAAAQAAADQAAAAAAA==", SigHash.None, "Regtest")]
    // Input UTXOs have the same derivation path
    [InlineData("cHNidP8BAIUBAAAAA5AuveJtsmh34KQCGn2QjTVgYZeAP0wBbFfk5ZOGpl0OAAAAAAD/////0qsBGBoCPD41OUI73G9W8Q1hv3z8MkaGRgWlTiutSFwAAAAAAP////+7IC+zZyJI1BBQ/ghKrJi18xK+L7VVg1Q/B4ohnnMFsAEAAAAA/////wAAAAAATwEENYfPA325Ro0AAAABg9H86IDUttPPFss+9te+0DByQgbeD7RPXNuVH9mh1qIDnMEWyKA+kvyG038on8+HxI+9AD8r6ZI1dNIDSGC8824Q7QIQyDAAAIABAACAAQAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAA=", SigHash.All, "Regtest")]
    [InlineData("cHNidP8BAIUBAAAAA5AuveJtsmh34KQCGn2QjTVgYZeAP0wBbFfk5ZOGpl0OAAAAAAD/////0qsBGBoCPD41OUI73G9W8Q1hv3z8MkaGRgWlTiutSFwAAAAAAP////+7IC+zZyJI1BBQ/ghKrJi18xK+L7VVg1Q/B4ohnnMFsAEAAAAA/////wAAAAAATwEENYfPA325Ro0AAAABg9H86IDUttPPFss+9te+0DByQgbeD7RPXNuVH9mh1qIDnMEWyKA+kvyG038on8+HxI+9AD8r6ZI1dNIDSGC8824Q7QIQyDAAAIABAACAAQAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAABAR+ghgEAAAAAABYAFBQptc6zAas/qjlmx6MhONjyd11nAQMEAgAAACIGAvFR60eN3aNhBP2/c4cH9itYW+UFAq7F1bDfFv40mrRiGO0CEMgwAACAAQAAgAEAAAAAAAAAMwAAAAA=", SigHash.None, "Regtest")]
    public async Task SignTest(string psbt, SigHash sigHash, string network)
    {
        //Arrange
        var function = new Function();

        var originalPSBT = PSBT.Parse(psbt, Function.ParseNetwork(network));

        var input = new SignPSBTRequest(psbt,
            sigHash,
            network);

        Func<RootedKeyPath?, Task<string?>> GetSeed = (_) => Task.FromResult("middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical");
        //Act
        var result = await function.SignPSBT(psbt, network, sigHash, GetSeed);

        var parsedPSBT = PSBT.Parse(result.Psbt ?? throw new InvalidOperationException(), Function.ParseNetwork(network));

        //Assert
        result.Should().NotBeNull();
        //Now compare parsedPSBT with originalPSBT and check that the number of partial signatures has been increased
        parsedPSBT.Inputs.Sum(x => x.PartialSigs.Count).Should()
            .BeGreaterThan(originalPSBT.Inputs.Sum(x => x.PartialSigs.Count));
        parsedPSBT.Inputs.All(x => x.SighashType == input.EnforcedSighash).Should().BeTrue();
    }

    [Fact]
    public async Task FailedSignTest_InvalidDerivationPath()
    {
        //Arrange
        var function = new Function();
        var context = new TestLambdaContext();

        var psbtBase64 = "cHNidP8BAF4BAAAAAWAvqvtTSjdcNjNuK8YKWQg7RM1S8LFDdIXg3KU34l6/AQAAAAD/////AYSRNXcAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FcAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBKwCUNXcAAAAAIgAgs1MYpDJWIIGz/LeRwb5D/c1wgjKmSotvf8QyY3nsEMQiAgLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssUcwRAIgKsJYoVeZWSHLhJIIELCGqDZXBWF2JcYFgYUbTSg31gYCIAbh5LXC9mmOKmqjB3kW3rgBbHrht4B3Vz5jDXmrS+n7AgEDBAIAAAABBWlSIQLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssSEDAmf/CxGXSG9xiPljcG/e5CXFnnukFn0pJ64Q9U2aNL8hAxpTd/JawX43QWk3yFK6wOPpsRK931hHnT2R2BYwsouPU64iBgLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssRgfzOTeMAAAgAEAAIABAACAAAAAAAAAAAAiBgMCZ/8LEZdIb3GI+WNwb97kJcWee6QWfSknrhD1TZo0vxhg86CzMAAAgAEAAIABAACAAAAAAAAAAAAiBgMaU3fyWsF+N0FpN8hSusDj6bESvd9YR509kdgWMLKLjxjtAhDIMAAAgAEAAIABAACAAAAAAAAAAAAAAA==";

        var psbt = PSBT.Parse(psbtBase64, Network.RegTest);

        foreach (var psbtInput in psbt.Inputs)
        {
            var temp = psbtInput.HDKeyPaths;
            psbtInput.HDKeyPaths.Clear(); ;
            foreach (var rootedKeyPath in temp)
            {
                psbtInput.AddKeyPath(rootedKeyPath.Key, new RootedKeyPath(rootedKeyPath.Value.MasterFingerprint, new KeyPath("m/48'")));
            }
        }

        var input = new SignPSBTRequest(
            psbt.ToBase64(),
            SigHash.All,
            "Regtest");

        var inputJson = JsonSerializer.Serialize(input);

        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            Body = inputJson
        };
        //Act
        var result = await function.FunctionHandler(request, context);

        //Assert
        result.StatusCode.Should().Be(500);
        result.Body.Should().Contain("derivation");
    }

    [Fact]
    public async Task FailedSignTest_NoAddedPartialSig()
    {
        //Arrange
        var function = new Function();

        //Test PSBT with 1 partial sig on input 0
        //JSON
        //{ "Psbt":"cHNidP8BAF4BAAAAAWAvqvtTSjdcNjNuK8YKWQg7RM1S8LFDdIXg3KU34l6/AQAAAAD/////AYSRNXcAAAAAIgAguNLINpkV//IIFd1ti2ig15\u002B6mPOhNWykV0mwsneO9FcAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP\u002BhqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po\u002BBAlGvFeBbuLfqwYlbP19H/\u002B/s2DIaAu8iKY\u002BJ0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6\u002BzUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn\u002B/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBKwCUNXcAAAAAIgAgs1MYpDJWIIGz/LeRwb5D/c1wgjKmSotvf8QyY3nsEMQiAgLYVMVgz\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssUcwRAIgKsJYoVeZWSHLhJIIELCGqDZXBWF2JcYFgYUbTSg31gYCIAbh5LXC9mmOKmqjB3kW3rgBbHrht4B3Vz5jDXmrS\u002Bn7AgEDBAIAAAABBWlSIQLYVMVgz\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssSEDAmf/CxGXSG9xiPljcG/e5CXFnnukFn0pJ64Q9U2aNL8hAxpTd/JawX43QWk3yFK6wOPpsRK931hHnT2R2BYwsouPU64iBgLYVMVgz\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssRgfzOTeMAAAgAEAAIABAACAAAAAAAAAAAAiBgMCZ/8LEZdIb3GI\u002BWNwb97kJcWee6QWfSknrhD1TZo0vxhg86CzMAAAgAEAAIABAACAAAAAAAAAAAAiBgMaU3fyWsF\u002BN0FpN8hSusDj6bESvd9YR509kdgWMLKLjxjtAhDIMAAAgAEAAIABAACAAAAAAAAAAAAAAA==","Fingerprint":"ed0210c8","AccountDerivationPath":"m/48\u0027/1\u0027/1\u0027","AddressDerivationPath":"0/0","EnforcedSighash":1,"Network":"Regtest","AwsKmsKeyId":"mrk-cec3e3ef59bc4616a6f44da60bfea0ba"}
        var psbtBase64 = "cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==";

        var psbt = PSBT.Parse(psbtBase64, Network.RegTest);

        foreach (var psbtInput in psbt.Inputs)
        {
            var temp = psbtInput.HDKeyPaths.ToList();
            psbtInput.HDKeyPaths.Clear(); ;
            foreach (var rootedKeyPath in temp)
            {
                psbtInput.AddKeyPath(rootedKeyPath.Key, new RootedKeyPath(rootedKeyPath.Value.MasterFingerprint, new KeyPath("m/48'")));
            }
        }

        Func<RootedKeyPath?, Task<string?>> GetSeed = (_) => Task.FromResult("middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical");
        //Act
        var act = () => function.SignPSBT(psbt.ToBase64(), "Regtest", SigHash.All, GetSeed);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid expected number of partial signatures after signing the PSBT, expected: 1, actual: 0");
    }

    [Fact]
    public async Task GenerateEncryptedSeedTest()
    {
        //Arrange
        var function = new Function();
        var context = new TestLambdaContext();

        var mnemonicString =
            "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical";

        var keyId = awsKmsKeyId;
        //Act
        var result = await function.EncryptSeedphrase(mnemonicString, keyId);
        var base64Decoding = Convert.FromBase64String(result);
        //Assert
        result.Should().NotBeEmpty();

        base64Decoding.Should().NotBeEmpty();
    }


    [Fact]
    public async Task ValidateXPub_ValidInputs_NoExceptionThrown()
    {
        // Arrange
        var function = new Function();
        var psbt = PSBT.Parse("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==",Network.RegTest);
        var masterXpriv =
            new Mnemonic(
                    "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical")
                .DeriveExtKey().GetWif(Network.RegTest);


        // Act
        var act = async () => await function.ValidateXPub(psbt, masterXpriv);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateXPub_NullPsbt_ThrowsArgumentNullException()
    {
        // Arrange
        var function = new Function();
        PSBT psbt = null;
        var masterXpriv =
            new Mnemonic(
                    "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical")
                .DeriveExtKey().GetWif(Network.RegTest);

        // Act
        var act = async () => await function.ValidateXPub(psbt, masterXpriv);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("Value cannot be null. (Parameter 'psbt')");
    }

    [Fact]
    public async Task ValidateXPub_NullMasterXpriv_ThrowsArgumentNullException()
    {
        // Arrange
        var function = new Function();
        var psbt = PSBT.Parse("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==",Network.RegTest);;
        BitcoinExtKey masterXpriv = null;


        // Act
        var act = async () => await function.ValidateXPub(psbt, masterXpriv);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("Value cannot be null. (Parameter 'masterXpriv')");
    }

    [Fact]
    public async Task ValidateXPub_MissingFingerprint_ThrowsException()
    {
        // Arrange
        var function = new Function();
        var psbt = PSBT.Parse("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==",Network.RegTest);
        var masterXpriv =
            new Mnemonic(
                    "virtual labor ill affair marine believe polar strike save torch tissue machine weekend crystal two guitar auction vintage lonely dinner country possible laundry park")
                .DeriveExtKey().GetWif(Network.RegTest);


        // Act
        var act = async () => await function.ValidateXPub(psbt, masterXpriv);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*is not present in the global xpubs*");

    }

    [Fact]
    public async Task ValidateXPub_XpubMismatch_ThrowsException()
    {
        // Arrange
        var function = new Function();
        var psbt = PSBT.Parse("cHNidP8BAF4BAAAAAcbYkt1iwOa6IsI8lrNx1DWQCCg/y7+fQTlfEhDIKOWVAAAAAAD/////AeiN9QUAAAAAIgAgg+aofANl6wKKByTgFl5yBnqUK8f7sn4ULhAAIJb1C0cAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2SQ9QUAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FciAgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BkcwRAIgPcwj6yaA6RZn+4YSHi4S1WE5ziHEt0IZO5KqDE5B0zMCID6cSLumR2AbgwqMTI3/Z3szEyMQauxtzvBpY8Z4oSp8AgEDBAIAAAABBWlSIQMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BiEDgTQLkhqca3brBTunNmjIsb4WEsFryTwd3BH/ZPS4KkohA91uD9EYRlzIBT6yNU2S2L/wvOA0/em4ocaM//veOtN2U64iBgMnQqNaMT2Yz47ME+CqhsEMK9fB1sQRGvbBQkPau524BhgfzOTeMAAAgAEAAIABAACAAQAAAAAAAAAiBgOBNAuSGpxrdusFO6c2aMixvhYSwWvJPB3cEf9k9LgqShjtAhDIMAAAgAEAAIABAACAAQAAAAAAAAAiBgPdbg/RGEZcyAU+sjVNkti/8LzgNP3puKHGjP/73jrTdhhg86CzMAAAgAEAAIABAACAAQAAAAAAAAAAAA==",Network.RegTest);
        var masterXpriv =
            new Mnemonic(
                    "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical")
                .DeriveExtKey().GetWif(Network.Main);


        // Act
        var act = async () => await function.ValidateXPub(psbt, masterXpriv);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*the xpub does not match the expected one*");
    }
}

