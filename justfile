# Justfile
set positional-arguments

build-docker-image:
    # Navigate to the RemoteSigner directory and build the Docker image
    cd RemoteSigner && docker build -t 839166930136.dkr.ecr.eu-central-1.amazonaws.com/nodeguardremotesigner:latest .
  
push-docker-image:
    # Push the Docker image to the repository
    docker push 839166930136.dkr.ecr.eu-central-1.amazonaws.com/nodeguardremotesigner:latest

deploy: build-docker-image push-docker-image
    #!/usr/bin/env bash
    # Set bash options for robust error handling
    set -euxo pipefail
    # Use Gum to choose the environment
    ENV=$(gum choose "stg" "prod")
    # Update the AWS Lambda function code
    aws lambda update-function-code --no-paginate --function-name arn:aws:lambda:eu-central-1:839166930136:function:SignPSBT-$ENV --image-uri 839166930136.dkr.ecr.eu-central-1.amazonaws.com/nodeguardremotesigner:latest --publish
  
deploy-no-cli env='stg': build-docker-image push-docker-image
    # Update the AWS Lambda function code without using Gum
    aws lambda update-function-code --no-paginate --function-name arn:aws:lambda:eu-central-1:839166930136:function:SignPSBT-{{env}} --image-uri 839166930136.dkr.ecr.eu-central-1.amazonaws.com/nodeguardremotesigner:latest --publish

# Seed ceremony: generate a NEW 24-word seed, KMS-encrypt it and write the manifest (interactive terminal required)
ceremony-generate kms_key_id network='mainnet' out='manifest.json':
    dotnet run --project RemoteSigner.SeedCeremony -- generate --kms-key-id {{kms_key_id}} --network {{network}} --out {{out}}

# Seed ceremony: KMS-encrypt an EXISTING seed (prompted, hidden input — never passed as an argument)
ceremony-encrypt kms_key_id network='mainnet' out='manifest.json':
    dotnet run --project RemoteSigner.SeedCeremony -- encrypt --kms-key-id {{kms_key_id}} --network {{network}} --out {{out}}

# Seed ceremony: preflight-verify a manifest (KMS-decrypt + re-derive fingerprint/xpub) before touching the lambda
ceremony-verify manifest='manifest.json':
    dotnet run --project RemoteSigner.SeedCeremony -- verify --in {{manifest}}

# Smoke-test the ceremony encrypt->verify flow against a LOCAL AWS emulator's KMS (floci, LocalStack, ...)
# already listening on the given endpoint. Never touches real AWS. Uses the committed PUBLIC dev test
# vector (fingerprint ed0210c8); emulator ciphertexts are throwaway by design - never reuse them.
ceremony-test-local endpoint='http://localhost:4566':
    #!/usr/bin/env bash
    set -euo pipefail
    export AWS_ENDPOINT_URL={{endpoint}} AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=eu-central-1
    aws kms list-keys >/dev/null || { echo "No AWS emulator reachable at {{endpoint}}"; exit 1; }
    KEY_ID=$(aws kms create-key --query KeyMetadata.KeyId --output text)
    MANIFEST=$(mktemp)
    trap 'rm -f "$MANIFEST"' EXIT
    echo "middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical" \
      | dotnet run --project RemoteSigner.SeedCeremony -- encrypt --kms-key-id "$KEY_ID" --network regtest --out "$MANIFEST"
    dotnet run --project RemoteSigner.SeedCeremony -- verify --in "$MANIFEST"
    grep -q '"MF_ed0210c8"' "$MANIFEST"
    echo "Local emulator ceremony smoke test OK (fingerprint ed0210c8 round-tripped)"