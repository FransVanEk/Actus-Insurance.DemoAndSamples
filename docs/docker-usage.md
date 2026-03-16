# Docker Images Usage Guide

This guide explains how to run each ACTUS Insurance Docker image and how to connect them together.

## � Table of Contents

- [Available Images](#-available-images)
- [Running Individual Services](#-running-individual-services)
  - [ACTUS Insurance API](#1-actus-insurance-api)
  - [ACTUS Designer Frontend](#2-actus-designer-frontend)
- [Connecting API & Designer](#-connecting-api--designer)
- [CLI Tools Usage](#-cli-tools-usage)
- [Complete Workflow Example](#-complete-workflow-example)
- [Configuration Examples](#-configuration-examples)
- [Troubleshooting](#-troubleshooting)
- [Security Considerations](#-security-considerations)

## �📋 Available Images

All images are available on DockerHub with the following naming convention:
- **Stable**: `neobluetechlabs/{project}:latest`, `neobluetechlabs/{project}:1.0.0`
- **Preview**: `neobluetechlabs/{project}:main-preview`

**Platform Support**: Images are built for both `linux/amd64` and `linux/arm64` architectures, supporting both Intel/AMD and Apple Silicon (M1/M2) systems.

| Project | Image Name | Type | Description |
|---------|------------|------|-------------|
| **ACTUS Insurance API** | `neobluetechlabs/actus-insurance-api` | Web API | FastEndpoints .NET API with SQLite |
| **ACTUS Designer** | `neobluetechlabs/actus-designer` | Frontend | Next.js dashboard interface |
| **Scenario Calculator** | `neobluetechlabs/scenario-calc-demo` | CLI Tool | CPU/GPU calculation demo |
| **Monte Carlo CLI** | `neobluetechlabs/pam-monte-carlo` | CLI Tool | Portfolio analysis tool |

## 🚀 Running Individual Services

### 1. ACTUS Insurance API

```bash
# Basic run (SQLite data will be lost on container restart)
docker run -p 8080:8080 neobluetechlabs/actus-insurance-api:latest

# With persistent data volume
docker run -p 8080:8080 \
  -v actus-data:/app/data \
  neobluetechlabs/actus-insurance-api:latest

# With custom configuration
docker run -p 8080:8080 \
  -v actus-data:/app/data \
  -e Calculation__PreferGpu=true \
  -e ASPNETCORE_ENVIRONMENT=Development \
  neobluetechlabs/actus-insurance-api:latest
```

**Access Points:**
- API: http://localhost:8080
- Swagger UI: http://localhost:8080/swagger
- Health Check: http://localhost:8080/health

**Environment Variables:**
- `ASPNETCORE_URLS`: Default `http://+:8080`
- `Calculation__PreferGpu`: Enable GPU calculations (`true`/`false`)
- `ASPNETCORE_ENVIRONMENT`: Set to `Development` for detailed logging

### 2. ACTUS Designer (Frontend)

```bash
# Basic run (uses built-in API proxy)
docker run -p 3000:3000 neobluetechlabs/actus-designer:latest

# Connect to external API
docker run -p 3000:3000 \
  -e NEXT_PUBLIC_API_URL=http://localhost:8080 \
  -e API_BASE_URL=http://localhost:8080 \
  -e API_KEY=your-api-key \
  neobluetechlabs/actus-designer:latest

# Development mode
docker run -p 3000:3000 \
  -e NODE_ENV=development \
  -e NEXT_PUBLIC_API_URL=http://localhost:8080 \
  neobluetechlabs/actus-designer:latest
```

**Access Points:**
- Frontend: http://localhost:3000

**Environment Variables:**
- `NEXT_PUBLIC_API_URL`: Public API URL (empty = use Next.js proxy)
- `API_BASE_URL`: Server-side API URL 
- `API_KEY`: Authentication key for API
- `NODE_ENV`: Environment mode (`production`/`development`)

## 🔗 Connecting API & Designer

### Option 1: Docker Network (Recommended)

Create a custom Docker network to connect the services:

```bash
# Create network
docker network create actus-network

# Run API
docker run -d \
  --name actus-api \
  --network actus-network \
  -p 8080:8080 \
  -v actus-data:/app/data \
  neobluetechlabs/actus-insurance-api:latest

# Run Designer (connected to API)
docker run -d \
  --name actus-designer \
  --network actus-network \
  -p 3000:3000 \
  -e API_BASE_URL=http://actus-api:8080 \
  -e API_KEY=dev-api-key-change-me \
  neobluetechlabs/actus-designer:latest
```

### Option 2: Host Network

Use host networking for simpler connection:

```bash
# Run API
docker run -d \
  --name actus-api \
  -p 8080:8080 \
  -v actus-data:/app/data \
  neobluetechlabs/actus-insurance-api:latest

# Run Designer (using localhost)
docker run -d \
  --name actus-designer \
  -p 3000:3000 \
  -e NEXT_PUBLIC_API_URL=http://localhost:8080 \
  -e API_BASE_URL=http://host.docker.internal:8080 \
  neobluetechlabs/actus-designer:latest
```

### Option 3: Docker Compose (Easiest)

Use the provided docker-compose.yml:

```bash
# Clone repository and use docker-compose
git clone <repository-url>
cd Actus-Insurance.DemoAndSamples

# Start both services
docker-compose up -d

# Or use specific images
docker-compose -f docker-compose.yml up -d
```

## 🛠️ CLI Tools Usage

### Scenario Calculator Demo

```bash
# Basic run
docker run neobluetechlabs/scenario-calc-demo:latest

# With custom data volume
docker run -v $(pwd)/data:/app/data \
  neobluetechlabs/scenario-calc-demo:latest

# Interactive mode
docker run -it \
  -v $(pwd)/data:/app/data \
  neobluetechlabs/scenario-calc-demo:latest
```

### Monte Carlo CLI

```bash
# Basic run
docker run neobluetechlabs/pam-monte-carlo:latest

# With input/output data mounting
docker run -v $(pwd)/input:/app/data/input \
  -v $(pwd)/output:/app/data/output \
  neobluetechlabs/pam-monte-carlo:latest

# Using sample data (included in image)
docker run -v $(pwd)/results:/app/data \
  neobluetechlabs/pam-monte-carlo:latest
```

**CLI Data Volumes:**
- `/app/data` - Main data directory
- `/app/samples` - Built-in sample data (read-only)

## 🛠️ CLI-Specific Troubleshooting

### .NET Runtime Issues

If you encounter ".NET SDKs were found" or "application '.dll' does not exist" errors:

```bash
# Check if .NET runtime is available in container
docker run --rm neobluetechlabs/pam-monte-carlo:latest dotnet --info

# Verify DLL files exist
docker run --rm --entrypoint ls neobluetechlabs/pam-monte-carlo:latest -la /app/

# Run container interactively to debug
docker run -it --entrypoint /bin/bash neobluetechlabs/pam-monte-carlo:latest
# Inside container, check:
ls -la /app/
dotnet --version
which dotnet

# Test manual execution
docker run --rm neobluetechlabs/pam-monte-carlo:latest dotnet /app/PamMonteCarlo50Y.dll --help
```

### CLI Execution Issues

```bash
# Check container logs for detailed error messages
docker logs <container-name>

# Run with verbose output
docker run -e DOTNET_LOGGING__CONSOLE__LOGLEVEL=Debug neobluetechlabs/pam-monte-carlo:latest

# Memory issues for large portfolios
docker run --memory=8g neobluetechlabs/pam-monte-carlo:latest

# Check sample data availability
docker run --rm neobluetechlabs/pam-monte-carlo:latest find /app -name "*.csv" -o -name "*.json"

# Force rebuild if there are build issues
docker build --no-cache -t pam-mc-debug ./CLI/PamMonteCarlo50Y/
```

## 📊 Complete Workflow Example

### Step 1: Start the Full Stack

```bash
# Create network
docker network create actus-network

# Start API with data persistence
docker run -d \
  --name actus-api \
  --network actus-network \
  -p 8080:8080 \
  -v actus-data:/app/data \
  neobluetechlabs/actus-insurance-api:latest

# Start Frontend
docker run -d \
  --name actus-designer \
  --network actus-network \
  -p 3000:3000 \
  -e API_BASE_URL=http://actus-api:8080 \
  neobluetechlabs/actus-designer:latest

# Wait for services to start
sleep 10
```

### Step 2: Initialize with Sample Data

```bash
# Create default sample data
curl -X POST http://localhost:8080/samples/create-default

# Verify API is working
curl http://localhost:8080/health
```

### Step 3: Use the Interface

1. Open **Frontend**: http://localhost:3000
2. Open **API Documentation**: http://localhost:8080/swagger
3. Use the dashboard to create and run calculations

### Step 4: Run CLI Analysis

```bash
# Run scenario calculations
docker run -v actus-data:/app/data \
  neobluetechlabs/scenario-calc-demo:latest

# Run Monte Carlo analysis
docker run -v actus-data:/app/data \
  neobluetechlabs/pam-monte-carlo:latest
```

## 🔧 Configuration Examples

### Production Setup

```bash
# API with optimized settings
docker run -d \
  --name actus-api-prod \
  --restart unless-stopped \
  -p 8080:8080 \
  -v /opt/actus/data:/app/data \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Calculation__PreferGpu=true \
  -e Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning \
  neobluetechlabs/actus-insurance-api:latest

# Designer with production settings
docker run -d \
  --name actus-designer-prod \
  --restart unless-stopped \
  -p 3000:3000 \
  -e NODE_ENV=production \
  -e API_BASE_URL=http://actus-api-prod:8080 \
  -e API_KEY=production-secure-key \
  neobluetechlabs/actus-designer:latest
```

### Development Setup

```bash
# API with debug logging
docker run -d \
  --name actus-api-dev \
  -p 8080:8080 \
  -v $(pwd)/dev-data:/app/data \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Logging__LogLevel__Default=Debug \
  neobluetechlabs/actus-insurance-api:latest

# Designer with hot-reload support
docker run -d \
  --name actus-designer-dev \
  -p 3000:3000 \
  -e NODE_ENV=development \
  -e NEXT_PUBLIC_API_URL=http://localhost:8080 \
  neobluetechlabs/actus-designer:latest
```

## 🐛 Troubleshooting

### Connection Issues

```bash
# Check if containers are running
docker ps

# Check container logs
docker logs actus-api
docker logs actus-designer

# Test API connectivity from frontend container
docker exec actus-designer curl http://actus-api:8080/health

# Test network connectivity
docker network inspect actus-network
```

### Data Issues

```bash
# Check data volume
docker volume inspect actus-data

# Reset data (WARNING: Deletes all data)
docker volume rm actus-data
docker run -v actus-data:/app/data neobluetechlabs/actus-insurance-api:latest

# Backup data
docker run --rm -v actus-data:/data -v $(pwd):/backup busybox tar czf /backup/actus-backup.tar.gz -C /data .
```

### Platform Issues

```bash
# For platform-specific errors, force pull for your architecture
docker pull --platform linux/amd64 neobluetechlabs/actus-insurance-api:latest
docker pull --platform linux/arm64 neobluetechlabs/actus-insurance-api:latest

# Build locally for your specific platform
docker build --platform linux/amd64 -t actus-api-local ./samples/ActusInsurance.FastEndpointsSqliteGpuSample
docker build --platform linux/arm64 -t actus-api-local ./samples/ActusInsurance.FastEndpointsSqliteGpuSample

# Check available platforms for an image
docker buildx imagetools inspect neobluetechlabs/actus-insurance-api:latest
```

### Port Conflicts

```bash
# Check what's using ports
lsof -i :3000
lsof -i :8080

# Use different ports
docker run -p 3001:3000 neobluetechlabs/actus-designer:latest
docker run -p 8081:8080 neobluetechlabs/actus-insurance-api:latest
```

## 📚 Next Steps

1. **Explore the API**: Visit http://localhost:8080/swagger
2. **Use the Dashboard**: Open http://localhost:3000
3. **Run Calculations**: Use the web interface or CLI tools
4. **Monitor Logs**: `docker logs -f actus-api`
5. **Scale Up**: Use Docker Swarm or Kubernetes for production

## 🔒 Security Considerations

- Change default API keys in production
- Use HTTPS for production deployments
- Secure data volumes with proper permissions
- Consider using Docker secrets for sensitive configuration
- Regularly update image versions for security patches