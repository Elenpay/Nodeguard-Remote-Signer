FROM --platform=linux/amd64 public.ecr.aws/lambda/dotnet:10 AS base

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 as build
WORKDIR /src
COPY ["RemoteSigner.csproj", "RemoteSigner/"]
RUN dotnet restore "RemoteSigner/RemoteSigner.csproj"

WORKDIR "/src/RemoteSigner"
COPY . .
RUN dotnet build "RemoteSigner.csproj" --configuration Release --output /app/build

FROM build AS publish
RUN dotnet publish "RemoteSigner.csproj" \
            --configuration Release \
            --runtime linux-x64 \
            --self-contained false \
            --output /app/publish \
            -p:PublishReadyToRun=true

FROM base AS final

# The eMudhra intermediates compromised in October 2026 chain to this root, and AL2023 has not shipped a ca-certificates release without it yet
RUN dnf -y upgrade ca-certificates \
    && awk '/^# emSign ECC Root CA - G3$/{f=1} f&&/BEGIN CERTIFICATE/{p=1} p{print} p&&/END CERTIFICATE/{exit}' /etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem > /etc/pki/ca-trust/source/blocklist/emsign-ecc-root-ca-g3.pem \
    && update-ca-trust extract \
    && dnf clean all

WORKDIR /var/task
COPY --from=publish /app/publish .