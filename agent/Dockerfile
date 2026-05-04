FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AutoUpRelease.Agent/AutoUpRelease.Agent.csproj AutoUpRelease.Agent/
RUN dotnet restore AutoUpRelease.Agent/AutoUpRelease.Agent.csproj
COPY AutoUpRelease.Agent/ AutoUpRelease.Agent/
RUN dotnet publish AutoUpRelease.Agent/AutoUpRelease.Agent.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && . /etc/os-release \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian ${VERSION_CODENAME} stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "AutoUpRelease.Agent.dll"]
