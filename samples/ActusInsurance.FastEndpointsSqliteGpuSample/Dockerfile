# ─────────────────────────────────────────────────────────────────
# Stage 1 – build
# ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file first so NuGet restore is cached separately
COPY ActusInsurance.FastEndpointsSqliteGpuSample.csproj ./
RUN dotnet restore

# Copy source and publish
COPY . .
RUN dotnet publish ActusInsurance.FastEndpointsSqliteGpuSample.csproj \
    --no-restore \
    -c Release \
    -o /app/publish

# ─────────────────────────────────────────────────────────────────
# Stage 2 – runtime
# ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create the data directory that will hold app.db + blobs
# (override with a volume mount in production: -v /host/data:/app/data)
RUN mkdir -p /app/data/blobs

# Copy published output
COPY --from=build /app/publish .

# SQLite database and file blobs live under /app/data
# Mount this path as a volume to persist data across container restarts:
#   docker run -v actus-data:/app/data actus-sample
VOLUME /app/data

# Expose the HTTP port (override ASPNETCORE_URLS for HTTPS)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# ── Optional tuning ───────────────────────────────────────────────
# Set to "true" to use the simulated GPU engine instead of CPU:
ENV Calculation__PreferGpu=false

# Reduce EF Core log noise in production containers:
ENV Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning

ENTRYPOINT ["dotnet", "ActusInsurance.FastEndpointsSqliteGpuSample.dll"]
