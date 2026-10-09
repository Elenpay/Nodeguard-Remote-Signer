# NodeGuard Remote Signer

AWS Lambda function in dotnet to allow the remote signing of transactions in NodeGuard.

## Trusted coordinator signing

The NodeGuard has two modes of signing transactions as as Trusted coordinator (it is mandatory for security reasons, since the FM relies on SIGHASH_NONE inputs from finance managers):

1. Embedded (legacy) signing withing the NodeGuard, less secure but easier to manage
2. Use a remote signing function (AWS Lambda function with public URL) to sign with a AWS KMS encrypted seedphrase that only the function can decrypt. Bear in mind that the AWS KMS symmetric encryption private key is never exposed and it is managed by AWS KMS.

To enable mode #1 set env var as follows `ENABLE_REMOTE_SIGNER = false`, otherwise in case you want mode #2 you need a lambda function deployed with this project with a public function URL and a AWS KMS Symmetric encryption key. Then set up the following env vars as in this example (json-like):

```
"ENABLE_REMOTE_SIGNER": "true",
"AWS_ACCESS_KEY_ID": "********",
"AWS_SECRET_ACCESS_KEY": "********",
"AWS_REGION": "eu-west-1",
"REMOTE_SIGNER_ENDPOINT": "https://*.lambda-url.eu-west-1.on.aws/"
```

> Note: NodeGuard does not read any `AWS_KMS_KEY_ID` env var — the KMS key id lives only inside this function's per-fingerprint `MF_*` configuration (see below).

They are detailed as follows:

- AWS_ACCESS_KEY_ID: IAM-based user account id used to auth against AWS lambda
- AWS_SECRET_ACCESS_KEY: IAM-based user secret
- AWS_REGION: the region code of the AWS deployed lambda function
- REMOTE_SIGNER_ENDPOINT: AWS Function url endpoint, check https://docs.aws.amazon.com/lambda/latest/dg/lambda-urls.html

### Invoking the lambda function

The key aspect when invoking the lambda function is to use the AWS API Gateway format for payloads, check [AWS Function URL invocation basics](https://docs.aws.amazon.com/lambda/latest/dg/urls-invocation.html).

Example input to AWS lambda function:

```json
{
  "Version": null,
  "RouteKey": null,
  "RawPath": null,
  "RawQueryString": null,
  "Cookies": null,
  "Headers": null,
  "QueryStringParameters": null,
  "RequestContext": null,
  "Body": "{\"Psbt\":\"cHNidP8BAF4BAAAAAWAvqvtTSjdcNjNuK8YKWQg7RM1S8LFDdIXg3KU34l6/AQAAAAD/////AYSRNXcAAAAAIgAguNLINpkV//IIFd1ti2ig15\\u002B6mPOhNWykV0mwsneO9FcAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP\\u002BhqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po\\u002BBAlGvFeBbuLfqwYlbP19H/\\u002B/s2DIaAu8iKY\\u002BJ0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6\\u002BzUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn\\u002B/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBKwCUNXcAAAAAIgAgs1MYpDJWIIGz/LeRwb5D/c1wgjKmSotvf8QyY3nsEMQiAgLYVMVgz\\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssUcwRAIgKsJYoVeZWSHLhJIIELCGqDZXBWF2JcYFgYUbTSg31gYCIAbh5LXC9mmOKmqjB3kW3rgBbHrht4B3Vz5jDXmrS\\u002Bn7AgEDBAIAAAABBWlSIQLYVMVgz\\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssSEDAmf/CxGXSG9xiPljcG/e5CXFnnukFn0pJ64Q9U2aNL8hAxpTd/JawX43QWk3yFK6wOPpsRK931hHnT2R2BYwsouPU64iBgLYVMVgz\\u002BbATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssRgfzOTeMAAAgAEAAIABAACAAAAAAAAAAAAiBgMCZ/8LEZdIb3GI\\u002BWNwb97kJcWee6QWfSknrhD1TZo0vxhg86CzMAAAgAEAAIABAACAAAAAAAAAAAAiBgMaU3fyWsF\\u002BN0FpN8hSusDj6bESvd9YR509kdgWMLKLjxjtAhDIMAAAgAEAAIABAACAAAAAAAAAAAAAAA==\",\"EnforcedSighash\":1,\"Network\":\"Regtest\"}",
  "PathParameters": null,
  "IsBase64Encoded": false,
  "StageVariables": null
}
```

Request input body fields:

- Psbt: The base64-encoded PSBT to sign
- EnforcedSighash: used to enforce a SIGHASH mode signing for all the inputs, 1 means SIGHASH_ALL, check NBitcoin enums to match other sighash types.
- Network: Bitcoin network, this is case-insensitive as long the values are either one of the following `Mainnet, Regtest, Testnet`

Example output from the lambda function:

```json
{
  "statusCode": 200,
  "body": "{\"Psbt\":\"cHNidP8BAF4BAAAAAWAvqvtTSjdcNjNuK8YKWQg7RM1S8LFDdIXg3KU34l6/AQAAAAD/////AYSRNXcAAAAAIgAguNLINpkV//IIFd1ti2ig15\\u002B6mPOhNWykV0mwsneO9FcAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP\\u002BhqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po\\u002BBAlGvFeBbuLfqwYlbP19H/\\u002B/s2DIaAu8iKY\\u002BJ0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6\\u002BzUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn\\u002B/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBKwCUNXcAAAAAIgAgs1MYpDJWIIGz/LeRwb5D/c1wgjKmSotvf8QyY3nsEMQBAwQBAAAAAQVpUiEC2FTFYM/mwE4L60Q0G2p5QElV7YlMD7fcgoJEH79pLLEhAwJn/wsRl0hvcYj5Y3Bv3uQlxZ57pBZ9KSeuEPVNmjS/IQMaU3fyWsF\\u002BN0FpN8hSusDj6bESvd9YR509kdgWMLKLj1OuIgIC2FTFYM/mwE4L60Q0G2p5QElV7YlMD7fcgoJEH79pLLFHMEQCICrCWKFXmVkhy4SSCBCwhqg2VwVhdiXGBYGFG00oN9YGAiAG4eS1wvZpjipqowd5Ft64AWx64beAd1c\\u002BYw15q0vp\\u002BwIiAgMaU3fyWsF\\u002BN0FpN8hSusDj6bESvd9YR509kdgWMLKLj0cwRAIgU0100AuYgFliCrcGHwN4nB5ZIPSTbGlFEyjuCccDgxICIBf3Zeqc\\u002B7g49r\\u002BnIYw7tFpo7Jt6RasMja2X3RJuy9Y/ASIGAthUxWDP5sBOC\\u002BtENBtqeUBJVe2JTA\\u002B33IKCRB\\u002B/aSyxGB/M5N4wAACAAQAAgAEAAIAAAAAAAAAAACIGAwJn/wsRl0hvcYj5Y3Bv3uQlxZ57pBZ9KSeuEPVNmjS/GGDzoLMwAACAAQAAgAEAAIAAAAAAAAAAACIGAxpTd/JawX43QWk3yFK6wOPpsRK931hHnT2R2BYwsouPGO0CEMgwAACAAQAAgAEAAIAAAAAAAAAAAAAA\"}",
  "isBase64Encoded": false
}
```

Request output body fields:

- Psbt: The base64-encoded signed PSBT

### Encrypted Seedphrase generation — the seed-ceremony CLI

Seeds are provisioned with the `seed-ceremony` console tool in [RemoteSigner.SeedCeremony](RemoteSigner.SeedCeremony/), which reuses this function's own encryption/derivation code so the output can never drift from what the lambda expects. It replaces the old flow of pasting the mnemonic into the `GenerateEncryptedSeedTest` unit test — never do that anymore.

AWS credentials come from the [default AWS SDK chain](https://docs.aws.amazon.com/sdk-for-net/latest/developer-guide/creds-assign.html) (env vars, profiles, SSO), optionally overridden with `--profile`/`--region`. The only KMS permission needed is `kms:Encrypt` (`kms:Decrypt` too for `verify`).

```bash
# Generate a NEW 24-word seed (interactive terminal required: shows the words once,
# quizzes the backup, wipes the screen) and write the manifest file:
just ceremony-generate mrk-xxxxxxxx mainnet manifest.json

# Or encrypt an EXISTING seed (hidden prompt or --seed-file, never a CLI argument):
just ceremony-encrypt mrk-xxxxxxxx mainnet manifest.json

# Preflight gate before touching the lambda: KMS-decrypts the manifest's env value and
# checks the fingerprint, env var name and account xpub all re-derive identically:
just ceremony-verify manifest.json
```

The manifest contains only public data (env var name/value with the KMS ciphertext, master fingerprint, account xpub, derivation path, network):

- `EnvName`/`EnvValue` go to the lambda configuration (next section).
- `MasterFingerprint`/`AccountXpub` are what NodeGuard needs for its internal wallet (`/setup-internal-wallet`, or the rotation runbook in the NodeGuard repo at `docs/internal-wallet-rotation.md`).
- The derivation path must match NodeGuard's `DEFAULT_DERIVATION_PATH` (default `m/48'/1'`); pass `--derivation-path` if your deployment overrides it.

### Applying a new seed to the lambda (snapshot → merge → apply)

`aws lambda update-function-configuration --environment` **replaces the entire env var map** — applying only the new entry would delete every other `MF_*` seed. Always snapshot and merge:

```bash
FN=SignPSBT-stg            # or SignPSBT-prod
REGION=eu-central-1
TS=$(date +%Y%m%dT%H%M%S)

# 1. Snapshot the current env vars (keep this file: it is the rollback artifact)
aws lambda get-function-configuration --function-name "$FN" --region "$REGION" \
  --query 'Environment.Variables' > "env-$FN-$TS.json"

# 2. Merge the manifest's entry into the snapshot (file-based, nothing sensitive inline)
jq --slurpfile m manifest.json \
  '{Variables: (. + {($m[0].EnvName): $m[0].EnvValue})}' \
  "env-$FN-$TS.json" > "env-$FN-merged.json"

# 3. Sanity-check: every old MF_* key still present, plus the new one; stays under the 4 KB limit
jq -r '.Variables | keys[]' "env-$FN-merged.json"
wc -c "env-$FN-merged.json"

# 4. Apply from the file and wait
aws lambda update-function-configuration --function-name "$FN" --region "$REGION" \
  --environment "file://env-$FN-merged.json"
aws lambda wait function-updated-v2 --function-name "$FN" --region "$REGION"
```

To later mark a retired seed as compromised (multisig-only co-signing, see the `Compromised` field above), run the same snapshot → merge → apply flow with:

```bash
jq --arg name MF_xxxxxxxx \
  '{Variables: (.[$name] = (.[$name] | fromjson | .Compromised = true | tojson))}' \
  "env-$FN-$TS.json" > "env-$FN-merged.json"
```

### Setting the function main config

The lambda function uses environment variables as a key-value dictionary for configuration of the different wallets that can be used to sign, the dictionary keys are the master fingerprints of the different wallets while the value of the keys are the configuration of the lambda function.

The environment variable key must start with a prefix as `MF_{Master Fingerprint}` (e.g. MF_ed0210c8)

The configuration has the following fields:

- EncryptedSeedphrase: The encrypted seedphrase as explained above
- AwsKmsKeyId: Symmetric key generated by AWS KMS which decrypts the seedphrase
- Compromised (optional, defaults to false): when true, this seed may only co-sign true multisig inputs (threshold >= 2), so its signature alone can never move funds. Use it for retired/rotated seeds once their single-sig (hot) wallets are drained: a compromised NodeGuard host or stolen AWS credentials can then no longer drain legacy single-sig wallets through this function, while multisig spends remain gated by human co-signers. Note this does NOT protect against a party holding the seed plaintext itself.

Example (json-like structure of key-value, `ed0210c8` is the master fingerprint of the wallet):

```json
{
"MF_ed0210c8": {
  "EncryptedSeedphrase": "AQICAHheBtxW+2iTBvvhvmRXaxaScHh6up1/VWCRSMlopexrdwE1C/ylXBL5pmjJ3P/UG7XnAAABBzCCAQMGCSqGSIb3DQEHBqCB9TCB8gIBADCB7AYJKoZIhvcNAQcBMB4GCWCGSAFlAwQBLjARBAxPlkxPX65p7aRcXykCARCAgb4En2Bb/nWQ6m4i3JDP+KGjaGDAVF4LR6+2Ljl7orp6pfZbCCxK6e89OBpJWi7elQM670vD/SWkYSZ9MUWUshU8n7NyBJZuZgBhtaH6j6yDhgHtBv7cwJngv0d72QEaTrH2YqLCVuoddEKEpB13ezfkf56230QD134kcJze4fITQGA6sXxQ0x+WjKOeYltpB+Shk4+kaNja42ZM0MMjyrMOmQtXCkgdoTUVi6twiqU+qr8mQEEq0aNdZzlLCI/v",
  "AwsKmsKeyId": "mrk-cec3e3ef59bc4616a6f44da60bfea0ba"
}}
```

## Spark signing

The same function also signs for NodeGuard's [Spark](https://www.spark.money/) wallet, which NodeGuard uses as a swap-out provider: it receives a Lightning payment on Spark and exits it on-chain to a NodeGuard wallet. Requests under `/spark/` follow the `NSpark.RemoteSigner` contract from NSpark (the [Elenpay/nspark](https://github.com/Elenpay/nspark) fork of [orklabs/nspark](https://github.com/orklabs/nspark), in [vendor/nspark](vendor/nspark/)) (`docs/remote-signer.md` there). Every other path, including none, is the PSBT signer above, unchanged.

Spark is off until `SPARK_SEED_FINGERPRINT` is set. Until then every `/spark/` request gets a 403.

### Which seed signs for Spark

Spark reuses one existing `MF_` seed, and only that one: `SPARK_SEED_FINGERPRINT` names it. The Spark keys are derived from it at `m/8797555'/{SPARK_ACCOUNT}'/…`, apart from the PSBT keys.

- Requests for any other wallet are refused before KMS is called.
- A seed marked `Compromised` is refused. Before marking the Spark seed compromised or rotating it, drain NodeGuard's Spark balance; the NodeGuard rotation runbook has the step.
- The decrypted mnemonic must be valid BIP-39 and must reproduce its `MF_` fingerprint.
- Only the derived Spark account keys are cached in memory, for `SPARK_SIGNER_CACHE_SECONDS`. The mnemonic and master key are not cached.

Because this seed also signs PSBTs, a bug on the Spark path could reach it. The protections are the single allowlisted seed, the routing that sends only `/spark/` requests to Spark code, the policy below, and the NSpark native library rebuilt from Lightspark's source. NodeGuard also keeps Spark balances transit-only.

### Operations and policy

Served operations: `info`, `identity-public-key`, `sign-with-identity`, `sign-compact-with-identity`, `deposit-public-key`, `leaf-public-key`, `static-deposit-public-key`, `sign-leaf-frost`, `build-send-tweaks`, `build-claim-tweaks` and `build-preimage-shares`.

Never served (404): ECIES decryption, private key export, two-phase FROST nonces and adaptor keys. Leaf signatures with an adaptor key are refused (403).

| Variable | Required | Meaning |
|---|---|---|
| `SPARK_SEED_FINGERPRINT` | to enable Spark | Master fingerprint of the `MF_` seed that signs for Spark |
| `SPARK_ACCOUNT` | no | Spark account index, `0` by default. It must match NodeGuard's `SPARK_ACCOUNT` |
| `SPARK_OPERATOR_KEYS` | yes | Comma-separated identity keys (compressed, hex) of the Spark operators. Key tweaks and preimage shares are only built for exactly this set. Without it, every tweak and share is refused |
| `SPARK_THRESHOLD` | with operators | The operators' signing threshold (1 to the number of operators) |
| `SPARK_ALLOWED_RECEIVERS` | yes | Comma-separated identity keys (compressed, hex) that leaves may be sent to: the SSP. The signer's own identity is always allowed |
| `SPARK_SIGNER_CACHE_SECONDS` | no | How long derived Spark keys stay cached. Default 60; 0 disables |

A malformed policy refuses every Spark request with a 500 before any seed is decrypted. `/spark/info` returns the identity and the policy, and NodeGuard checks both at startup against its own configuration.

To get the identity NodeGuard pins as `SPARK_IDENTITY_PUBKEY`, run the ceremony's `verify` on the seed's manifest with the Spark account. It prints only public data:

```bash
dotnet run --project RemoteSigner.SeedCeremony -- verify --in manifest.json --spark-account 0
```

### Enabling Spark on a deployed function

1. Raise the memory to 512 MB once: `just set-memory stg` (or `prod`).
2. Add the `SPARK_*` variables with the snapshot → merge → apply flow above, merging a file instead of a manifest entry:

   ```bash
   jq --slurpfile s spark-env.json '{Variables: (. + $s[0])}' \
     "env-$FN-$TS.json" > "env-$FN-merged.json"
   ```

   `spark-env.json` holds the `SPARK_*` keys. They add about 400 bytes, so check that `wc -c` stays under Lambda's 4 KB environment limit.
3. Call `/spark/info` from NodeGuard (it does so at startup) and compare the identity with the ceremony's output.

Spark and PSBT requests share the function's concurrency. NodeGuard sends its Spark requests one at a time, so they hold at most one execution. If the function has reserved concurrency, leave room for that plus PSBT signing.

### Local smoke test

```bash
just spark-smoke-local
```

This builds the image and runs it under the Lambda runtime interface emulator with a throwaway [floci](https://hub.docker.com/r/floci/floci) KMS and the public dev seed (`ed0210c8`). It checks that `/spark/info` serves the dev seed's Spark identity and refuses other wallets, and that `/` still signs the golden PSBT byte-for-byte. It never touches AWS. It needs docker, the aws CLI, jq and dotnet.

## CI pipeline

The project uses GitHub Actions for continuous integration. The workflow is defined in `.github/workflows/ci.yml` and triggers on three events:

### Pull requests to develop

When a pull request targets the `develop` branch, the pipeline:

1. Runs the .NET test suite (restore, build, test)
2. Builds the Docker image (no push)

This validates that both the code and the Docker build are correct before merging.

### Push to develop

When code is merged into `develop`, the pipeline:

1. Runs the .NET test suite
2. Builds the Docker image
3. Pushes the image to GHCR with two tags:
   - `develop` (branch name)
   - `latest`

### Tag push (releases)

When a tag matching `v*` is pushed (e.g. `v1.2.3`), the pipeline:

1. Runs the .NET test suite
2. Builds the Docker image
3. Pushes the image to GHCR with the tag name as the Docker tag (e.g. `v1.2.3`)

### Workflow diagram

```
Pull request (develop)     Push to develop           Push tag v*
       |                        |                        |
   run tests                run tests                run tests
       |                        |                        |
   build image              build image              build image
       |                        |                        |
   (no push)              push to GHCR              push to GHCR
                          tags: latest, develop    tag: <semver>
```

## Using the image

### Pull from GHCR

```bash
docker pull ghcr.io/elenpay/nodeguard-remote-signer:latest
docker pull ghcr.io/elenpay/nodeguard-remote-signer:develop
docker pull ghcr.io/elenpay/nodeguard-remote-signer:v1.2.3
```

### Push to ECR for Lambda deployment

The image is consumed by an AWS Lambda function. To deploy it to your own AWS account, pull from GHCR, tag for ECR, and push.

Replace the account ID and region with your own values.

```bash
# Authenticate with your ECR registry
aws ecr get-login-password --region <region> | \
  docker login --username AWS --password-stdin <account-id>.dkr.ecr.<region>.amazonaws.com

# Create the repository if it does not exist
aws ecr create-repository --repository-name nodeguardremotesigner --region <region>

# Pull from GHCR
docker pull ghcr.io/elenpay/nodeguard-remote-signer:latest

# Tag for ECR
docker tag ghcr.io/elenpay/nodeguard-remote-signer:latest \
  <account-id>.dkr.ecr.<region>.amazonaws.com/nodeguardremotesigner:latest

# Push to ECR
docker push <account-id>.dkr.ecr.<region>.amazonaws.com/nodeguardremotesigner:latest

# Update the Lambda function
aws lambda update-function-code \
  --function-name arn:aws:lambda:<region>:<account-id>:function:SignPSBT-<env> \
  --image-uri <account-id>.dkr.ecr.<region>.amazonaws.com/nodeguardremotesigner:latest \
  --publish
```

Replace `<account-id>`, `<region>`, and `<env>` (stg or prod) with your actual values.

## Deploying the lambda function

### Requirements

- AWS CLI
- Access to the AWS account where the lambda function will be deployed

### Deploy

```bash
# Pull the image from GHCR, tag for ECR, push, and update the Lambda function
# See instructions above for the full sequence
```
