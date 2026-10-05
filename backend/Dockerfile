# The Shapers API as a container. Build from the backend folder:
#   docker build -t shapers-api backend
#
# Two stages: the SDK compiles and publishes, then only the published app is copied into a small "chiseled"
# Ubuntu runtime image. Chiseled images have no shell or package manager and run as a non-root user.
# The "extra" variant keeps time-zone data and ICU, which the API needs for Africa/Johannesburg and en-ZA formats.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so this layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props Shapers.slnx .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Host/Shapers.Api/Shapers.Api.csproj

# The OpenAPI document is generated for the TypeScript client during normal builds; not needed in the image.
RUN dotnet publish src/Host/Shapers.Api/Shapers.Api.csproj \
      --no-restore -c Release -o /app \
      -p:OpenApiGenerateDocuments=false \
      -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Shapers.Api.dll"]
