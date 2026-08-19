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