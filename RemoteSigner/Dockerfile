# Build context: the repository root (NSpark lives in the vendor/nspark submodule).
#   docker build -f RemoteSigner/Dockerfile .
FROM --platform=linux/amd64 public.ecr.aws/lambda/dotnet:10 AS base

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["RemoteSigner/RemoteSigner.csproj", "RemoteSigner/"]
COPY ["vendor/nspark/", "vendor/nspark/"]
RUN dotnet restore "RemoteSigner/RemoteSigner.csproj" --runtime linux-x64

COPY ["RemoteSigner/", "RemoteSigner/"]
RUN dotnet publish "RemoteSigner/RemoteSigner.csproj" \
            --configuration Release \
            --runtime linux-x64 \
            --self-contained false \
            --no-restore \
            --output /app/publish \
            -p:PublishReadyToRun=true

FROM base AS final
WORKDIR /var/task
COPY --from=build /app/publish .
