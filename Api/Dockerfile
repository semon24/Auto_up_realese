# API + Docker CLI (через сокет хоста) для docker compose в каталоге deploy.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AutoUpRelease.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# Плагин compose: для linux/arm64 замените имя артефакта на docker-compose-linux-aarch64.
ARG DOCKER_COMPOSE_VERSION=2.29.7
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl docker.io \
    && mkdir -p /usr/libexec/docker/cli-plugins \
    && curl -fsSL "https://github.com/docker/compose/releases/download/v${DOCKER_COMPOSE_VERSION}/docker-compose-linux-x86_64" \
        -o /usr/libexec/docker/cli-plugins/docker-compose \
    && chmod +x /usr/libexec/docker/cli-plugins/docker-compose \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:3001
EXPOSE 3001
ENTRYPOINT ["dotnet", "AutoUpRelease.Api.dll"]
