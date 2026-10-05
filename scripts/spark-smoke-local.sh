#!/usr/bin/env bash
# Smoke-tests the real signer image locally, never touching AWS: the Lambda runtime interface
# emulator (built into the AWS base image) runs it against a throwaway floci KMS, with the PUBLIC
# dev test vector (fingerprint ed0210c8) as the Spark seed. Checks both routes:
#   /spark/info returns the dev vector's Spark identity (the one ceremony verify prints), and
#               refuses any other wallet;
#   /           still signs the golden PSBT byte-for-byte.
# Emulator ciphertexts are throwaway by design - never reuse them.
# Usage: scripts/spark-smoke-local.sh [floci-image]   (needs docker, aws CLI, jq, dotnet)
set -euo pipefail

FLOCI_IMAGE="${1:-floci/floci:1.5.25}"
ROOT=$(cd "$(dirname "$0")/.." && pwd)
NAME="nodeguard-signer-smoke-$$"
IMAGE="nodeguard-remote-signer:spark-smoke"
DEV_MNEMONIC="middle teach digital prefer fiscal theory syrup enter crash muffin easily anxiety ill barely eagle swim volume consider dynamic unaware deputy middle into physical"
DEV_IDENTITY="032986ec06431b7b80a4171836174ac10c07e1cb1dff4af93082ee5f3ebca64a0a"
WORK=$(mktemp -d)

cleanup() {
  docker rm -f "$NAME-kms" "$NAME-signer" >/dev/null 2>&1 || true
  docker network rm "$NAME" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

fail() {
  echo "SMOKE FAILED: $1" >&2
  docker logs "$NAME-signer" 2>&1 | tail -30 >&2 || true
  exit 1
}

echo "Building the signer image..."
git -C "$ROOT" submodule update --init vendor/nspark
docker build -q --platform linux/amd64 -f "$ROOT/RemoteSigner/Dockerfile" -t "$IMAGE" "$ROOT" >/dev/null

echo "Starting a throwaway KMS ($FLOCI_IMAGE)..."
docker network create "$NAME" >/dev/null
docker run -d --name "$NAME-kms" --network "$NAME" -p 127.0.0.1::4566 "$FLOCI_IMAGE" >/dev/null
export AWS_ENDPOINT_URL="http://$(docker port "$NAME-kms" 4566/tcp | head -1)"
export AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=eu-central-1
for _ in $(seq 30); do aws kms list-keys >/dev/null 2>&1 && break; sleep 1; done
KEY_ID=$(aws kms create-key --query KeyMetadata.KeyId --output text)

echo "Encrypting the dev seed with the ceremony tool..."
echo "$DEV_MNEMONIC" | dotnet run --project "$ROOT/RemoteSigner.SeedCeremony" -- \
  encrypt --kms-key-id "$KEY_ID" --network regtest --out "$WORK/manifest.json" >/dev/null

{
  echo "AWS_ENDPOINT_URL=http://$NAME-kms:4566"
  echo "AWS_ACCESS_KEY_ID=test"
  echo "AWS_SECRET_ACCESS_KEY=test"
  echo "AWS_REGION=eu-central-1"
  echo "MF_ed0210c8=$(jq -r .EnvValue "$WORK/manifest.json")"
  echo "SPARK_SEED_FINGERPRINT=ed0210c8"
  echo "SPARK_ACCOUNT=0"
} > "$WORK/signer.env"

echo "Starting the signer under the Lambda runtime interface emulator..."
docker run -d --platform linux/amd64 --name "$NAME-signer" --network "$NAME" --env-file "$WORK/signer.env" \
  -p 127.0.0.1::8080 "$IMAGE" RemoteSigner::RemoteSigner.Function::FunctionHandler >/dev/null
RIE="http://$(docker port "$NAME-signer" 8080/tcp | head -1)/2015-03-31/functions/function/invocations"

# A Function URL event (payload format 2.0) for a POST of $2 to path $1
invoke() {
  jq -n --arg path "$1" --arg body "$2" \
    '{version: "2.0", rawPath: $path, body: $body, isBase64Encoded: false,
      requestContext: {http: {method: "POST", path: $path}}}' |
    curl -s --fail --retry 30 --retry-all-errors --retry-delay 1 -X POST "$RIE" -d @-
}

INFO=$(invoke /spark/info '{"wallet":"ed0210c8"}') || fail "the signer did not answer"
[ "$(jq -r .statusCode <<<"$INFO")" = 200 ] || fail "/spark/info: $INFO"
IDENTITY=$(jq -r '.body | fromjson | .result.identityPublicKey' <<<"$INFO")
[ "$IDENTITY" = "$DEV_IDENTITY" ] || fail "/spark/info returned identity $IDENTITY, expected $DEV_IDENTITY"
echo "OK  /spark/info -> Spark identity $IDENTITY"

CEREMONY=$(dotnet run --project "$ROOT/RemoteSigner.SeedCeremony" -- verify --in "$WORK/manifest.json" --spark-account 0)
grep -q "$DEV_IDENTITY" <<<"$CEREMONY" || fail "ceremony verify printed another Spark identity: $CEREMONY"
echo "OK  ceremony verify --spark-account 0 -> the same identity"

OTHER=$(invoke /spark/info '{"wallet":"1fcce4de"}') || fail "the signer did not answer"
[ "$(jq -r .statusCode <<<"$OTHER")" = 403 ] || fail "/spark/info for another wallet was not refused: $OTHER"
echo "OK  /spark/info for another wallet -> 403"

GOLDEN=$(jq -c '.[] | select(.name == "p2wsh-2of3-all")' "$ROOT/RemoteSigner.Tests/Fixtures/golden-psbt-signatures.json")
SIGNED=$(invoke / "$(jq -c '{Psbt: .psbt, EnforcedSighash: 1, Network: .network}' <<<"$GOLDEN")") ||
  fail "the signer did not answer"
[ "$(jq -r .statusCode <<<"$SIGNED")" = 200 ] || fail "PSBT signing: $SIGNED"
[ "$(jq -r '.body | fromjson | .Psbt' <<<"$SIGNED")" = "$(jq -r .expected <<<"$GOLDEN")" ] ||
  fail "the signed PSBT differs from the golden output"
echo "OK  / -> golden PSBT signed byte-for-byte"

echo "Local Spark smoke test OK"
