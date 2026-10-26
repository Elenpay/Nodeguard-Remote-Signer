FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime

FROM --platform=linux/amd64 public.ecr.aws/lambda/dotnet:10 AS base
# The Lambda base image still ships .NET 10.0.13, vulnerable to CVE-2026-111497 (Brotli decoder).
# Swap in the patched 10.0.14 runtime; drop this once the base image ships 10.0.14 or later.
RUN rm -rf /var/lang/bin/shared/Microsoft.NETCore.App/10.0.13 /var/lang/bin/shared/Microsoft.AspNetCore.App/10.0.13
COPY --from=runtime /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.14 /var/lang/bin/shared/Microsoft.NETCore.App/10.0.14
COPY --from=runtime /usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.14 /var/lang/bin/shared/Microsoft.AspNetCore.App/10.0.14
ENV DOTNET_VERSION=10.0.14

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
WORKDIR /var/task
COPY --from=publish /app/publish .