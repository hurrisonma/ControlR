# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG CONTROLR_VERSION

WORKDIR /src
COPY . .

RUN test -n "${CONTROLR_VERSION}"
RUN mkdir -p ControlR.Web.Server/wwwroot/downloads \
  && printf '%s\n' "${CONTROLR_VERSION}" > ControlR.Web.Server/wwwroot/downloads/Version.txt
RUN dotnet restore ControlR.Web.Server/ControlR.Web.Server.csproj
RUN dotnet publish ControlR.Web.Server/ControlR.Web.Server.csproj \
  --configuration Release \
  --self-contained false \
  --no-restore \
  --output /out \
  -p:ContinuousIntegrationBuild=true \
  -p:ExcludeApp_Data=true \
  -p:Version="${CONTROLR_VERSION}" \
  -p:FileVersion="${CONTROLR_VERSION}"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
  && apt-get install --yes --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /out .

USER $APP_UID

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "ControlR.Web.Server.dll"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=5 \
  CMD curl --fail http://localhost:8080/health || exit 1
