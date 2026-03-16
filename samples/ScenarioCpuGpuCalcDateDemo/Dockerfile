# Individual Dockerfile for ScenarioCpuGpuCalcDateDemo  
# Alternative: Use the shared Dockerfile.cli from repository root with build args
#   docker build --build-arg PROJECT=ScenarioCpuGpuCalcDateDemo --build-arg PROJECT_PATH=samples/ScenarioCpuGpuCalcDateDemo -f ../../Dockerfile.cli ../../

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Accept build argument for private NuGet token
ARG PKG_TOKEN

# Copy project file and restore dependencies
COPY ScenarioCpuGpuCalcDateDemo.csproj .

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

# Copy published app
COPY --from=build /app/publish .

# List contents to verify dll exists
RUN ls -la /app/ && echo "Contents of /app directory above"

# Set entrypoint
ENTRYPOINT ["dotnet", "ScenarioCpuGpuCalcDateDemo.dll"]