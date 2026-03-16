# Individual Dockerfile for PamMonteCarlo50Y
# Alternative: Use the shared Dockerfile.cli from repository root with build args
#   docker build --build-arg PROJECT=PamMonteCarlo50Y --build-arg PROJECT_PATH=CLI/PamMonteCarlo50Y -f ../../Dockerfile.cli ../../

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Accept build argument for private NuGet token
ARG PKG_TOKEN

# Copy project file and restore dependencies
COPY PamMonteCarlo50Y.csproj .

# Configure NuGet with private token if provided
RUN if [ ! -z "$PKG_TOKEN" ]; then \
      echo "Configuring GitHub NuGet source..." && \
      (dotnet nuget list source | grep -q "github" || \
       dotnet nuget add source "https://nuget.pkg.github.com/FransVanEk/index.json" \
         --name "github" \
         --username "FransVanEk" \
         --password "$PKG_TOKEN" \
         --store-password-in-clear-text) || \
      echo "Warning: Failed to configure GitHub NuGet source, continuing with existing sources"; \
    fi

RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app

# Create directory for input/output data
RUN mkdir -p /app/data

# Copy published app
COPY --from=build /app/publish .

# Copy samples directory if it exists
COPY --from=build /src/samples ./samples

# List contents to verify dll exists
RUN ls -la /app/ && echo "Contents of /app directory above"

# Volume for data persistence
VOLUME ["/app/data"]

# Set entrypoint
ENTRYPOINT ["dotnet", "PamMonteCarlo50Y.dll"]