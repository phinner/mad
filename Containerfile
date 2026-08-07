# https://depot.dev/docs/container-builds/optimal-dockerfiles/dotnet-aspnetcore-dockerfile

FROM mcr.microsoft.com/dotnet/sdk:10.0 as build

COPY Mad.Application/Mad.Application.csproj Mad.Application/
COPY *.slnx ./

RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    --mount=type=cache,target=/root/.local/share/NuGet/plugins-cache \
    --mount=type=cache,target=/tmp/NuGetScratchroot \
    dotnet restore

COPY Mad.Application/ Mad.Application/

RUN --mount=type=cache,target=/root/.nuget/packages \
    --mount=type=cache,target=/root/.local/share/NuGet/v3-cache \
    --mount=type=cache,target=/root/.local/share/NuGet/plugins-cache \
    --mount=type=cache,target=/tmp/NuGetScratchroot \
    dotnet publish "Mad.Application/Mad.Application.csproj" \
    --no-restore \
    --configuration Release \
    --output /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:10.0 as runtime

RUN groupadd -g 1001 appgroup && \
    useradd -u 1001 -g appgroup -m -d /app -s /bin/false appuser

WORKDIR /app

COPY --from=build --chown=appuser:appgroup /app/publish .

USER appuser

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Mad.Application.dll"]
