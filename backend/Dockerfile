# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PortfolioApi/PortfolioApi.csproj src/PortfolioApi/
# Restore the runtime project only — the .sln also lists the test
# project, which isn't copied into the image (and shouldn't be: we
# don't want test deps in the production layer). Scoping to the
# csproj keeps the image lean and decouples the docker build from
# the test project's existence.
RUN dotnet restore src/PortfolioApi/PortfolioApi.csproj

COPY src/ src/
RUN dotnet publish src/PortfolioApi/PortfolioApi.csproj -c Release -o /app /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Media uploads live here; mount a volume to persist across rebuilds.
RUN mkdir -p /app/media
VOLUME ["/app/media"]

ENTRYPOINT ["dotnet", "PortfolioApi.dll"]
