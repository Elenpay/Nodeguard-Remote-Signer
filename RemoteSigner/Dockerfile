FROM public.ecr.aws/lambda/dotnet:6 AS base

FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim as build
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