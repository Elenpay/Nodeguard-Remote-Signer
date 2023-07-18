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