# ACTUS Insurance Demo & Samples

This repository contains a complete Docker Compose setup for running the ACTUS Insurance API along with the actus-designer frontend interface.

## 🚀 Quick Start

### Prerequisites

- Docker and Docker Compose installed
- Git (to clone the repository)

### Running the Application

1. **Clone and navigate to the repository:**
   ```bash
   git clone <repository-url>
   cd Actus-Insurance.DemoAndSamples
   ```

2. **Set up private NuGet token (if building locally):**
   ```bash
   # For projects using GPU packages, set your GitHub token
   export PKG_TOKEN=your_github_personal_access_token
   # Or add it to your .env file: PKG_TOKEN=your_token_here
   ```

3. **Start all services:**
   ```bash
   docker-compose up -d --build
   ```

4. **Access the applications:**
   - **Frontend (ACTUS Designer)**: http://localhost:3000
   - **API**: http://localhost:8080
   - **API Documentation (Swagger)**: http://localhost:8080/swagger

## 📋 Services Overview

### ACTUS Insurance API (`actus-api`)
- **Technology**: .NET 9.0 with FastEndpoints
- **Port**: 8080
- **Database**: SQLite with Entity Framework Core
- **Features**: 
  - File management (upload/download scenarios, risks, portfolios)
  - Calculation runs (CPU/GPU support)
  - Result management and export

### ACTUS Designer Frontend (`actus-designer`)
- **Technology**: Next.js 16 with TypeScript and Tailwind CSS
- **Port**: 3000
- **Features**:
  - Interactive dashboard for insurance calculations
  - Contract management interface
  - Risk visualization and metrics
  - Real-time event feeds

## 🔧 Configuration

### Environment Variables

Copy and customize the environment variables:

```bash
# Copy the example environment file
cp .env .env.local

# Edit configuration as needed
nano .env.local
```

**Key Configuration Options:**

| Variable | Default | Description |
|----------|---------|-------------|
| `API_PORT` | 8080 | Port for the API service |
| `FRONTEND_PORT` | 3000 | Port for the frontend service |
| `PREFER_GPU` | false | Enable GPU calculations (set to `true`) |
| `API_KEY` | dev-api-key-change-me | API authentication key |
| `NEXT_PUBLIC_API_URL` | (empty) | Frontend API URL (uses proxy if empty) |

### GPU Calculations

To enable GPU-accelerated calculations:

```bash
# Set in .env file
PREFER_GPU=true
```

Or pass it directly in API requests:
```json
{
  "enginePreference": "GPU"
}
```

## 🛠️ Development

### Development Mode

For development with hot-reload capabilities:

```bash
# Start with development overrides
docker-compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

### View Logs

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f actus-api
docker-compose logs -f actus-designer
```

### Stop Services

```bash
# Stop all services
docker-compose down

# Stop and remove volumes (removes database)
docker-compose down -v
```

## 📊 Using the API

### Quick API Test

1. **Create sample data:**
   ```bash
   curl -X POST http://localhost:8080/samples/create-default
   ```

2. **Start a calculation run:**
   ```bash
   curl -X POST http://localhost:8080/runs \
     -H "Content-Type: application/json" \
     -d '{"scenarioArtifactId": 1, "riskArtifactId": 1}'
   ```

3. **Check run status:**
   ```bash
   curl http://localhost:8080/runs/{runId}/status
   ```

4. **Get results:**
   ```bash
   curl http://localhost:8080/runs/{runId}/result
   ```

### API Endpoints

- `POST /samples/create-default` - Seed database with sample data
- `GET/POST /files` - File management (scenarios, risks, portfolios)
- `GET/POST /sinks` - Calculation output definitions
- `GET/POST /runs` - Start and manage calculation runs
- `GET /runs/{id}/status` - Check run status
- `GET /runs/{id}/result` - Retrieve calculation results

## 🔍 Health Checks

Both services include health check endpoints:

- **API Health**: http://localhost:8080/health  
- **Frontend Health**: http://localhost:3000 (Next.js default)

## 📁 Data Persistence

- **Database**: SQLite database persisted in Docker volume `actus-data`
- **File Uploads**: Stored in `/app/data/blobs` within the API container
- **Volume Mount**: Use `-v /host/data:/app/data` for external persistence

## 🐛 Troubleshooting

### Common Issues

1. **Port conflicts:**
   ```bash
   # Check what's using ports 3000 or 8080
   lsof -i :3000
   lsof -i :8080
   
   # Change ports in .env file if needed
   ```

2. **API connection issues:**
   ```bash
   # Check if API is responding
   curl http://localhost:8080/health
   
   # Check Docker network connectivity
   docker-compose exec actus-designer curl http://actus-api:8080/health
   ```

3. **Build issues:**
   ```bash
   # Clean build (removes cached layers)
   docker-compose build --no-cache
   
   # Reset everything
   docker-compose down -v
   docker system prune -f
   docker-compose up -d --build
   ```

### Logs and Debugging

```bash
# Check service status
docker-compose ps

# View real-time logs
docker-compose logs -f

# Enter container shell for debugging
docker-compose exec actus-api bash
docker-compose exec actus-designer sh
```

## 📚 Next Steps

1. Explore the **Swagger UI** at http://localhost:8080/swagger
2. Use the **ACTUS Designer** interface at http://localhost:3000 
3. Review the documentation:
   - [Docker Usage Guide](docs/docker-usage.md) - Complete Docker deployment guide
   - [Input/Output Contract](docs/input-output-contract.md) - API data contracts
4. Check the sample data in `CLI/PamMonteCarlo50Y/samples/`

## 🐳 Docker Images

All projects are available as Docker images on DockerHub:

### Available Images

| Project | DockerHub Image | Description |
|---------|-----------------|-------------|
| **ACTUS Insurance API** | `neobluetechlabs/actus-insurance-api` | FastEndpoints .NET API with SQLite |
| **ACTUS Designer** | `neobluetechlabs/actus-designer` | Next.js frontend interface |
| **Scenario Calculator** | `neobluetechlabs/scenario-calc-demo` | CPU/GPU calculation demo |
| **Monte Carlo CLI** | `neobluetechlabs/pam-monte-carlo` | Portfolio analysis CLI tool |

### Quick Start with Pre-built Images

```bash
# Run API only
docker run -p 8080:8080 -v actus-data:/app/data neobluetechlabs/actus-insurance-api:latest

# Run Frontend only  
docker run -p 3000:3000 neobluetechlabs/actus-designer:latest

# Run both with networking
docker network create actus-network
docker run -d --name actus-api --network actus-network -p 8080:8080 -v actus-data:/app/data neobluetechlabs/actus-insurance-api:latest
docker run -d --name actus-designer --network actus-network -p 3000:3000 -e API_BASE_URL=http://actus-api:8080 neobluetechlabs/actus-designer:latest
```

### 📖 Detailed Docker Usage

For comprehensive Docker usage instructions, including:
- Individual container configuration
- Service networking and connection
- CLI tools usage
- Production deployment
- Troubleshooting

**See: [docs/docker-usage.md](docs/docker-usage.md)**

### Image Tags

- **Stable releases**: `latest`, `1.0.0`, `1.0`, `1`
- **Preview builds**: `main-preview`, `main-{sha}-preview`
- **Platform support**: `linux/amd64`, `linux/arm64` (Intel/AMD and Apple Silicon)

### Using Pre-built Images

```bash
# Pull and run API only
docker run -p 8080:8080 neobluetechlabs/actus-insurance-api:latest

# Pull and run frontend only  
docker run -p 3000:3000 neobluetechlabs/actus-designer:latest

# Use preview images
docker run neobluetechlabs/actus-insurance-api:main-preview
```

## 🚀 CI/CD Pipeline

### Automated Builds

The repository includes GitHub Actions workflows that automatically:

1. **On Push to Main**: 
   - Build and test all projects
   - Create preview Docker images with `-preview` suffix
   - Push to DockerHub

2. **On Release**:
   - Build and test all projects  
   - Create stable Docker images with semantic version tags
   - Push to DockerHub with `latest` tag

### Required Secrets

To enable DockerHub publishing, configure these repository secrets:

```bash
DOCKERHUB_USERNAME=your-dockerhub-username
DOCKERHUB_TOKEN=your-dockerhub-access-token  
PKG_TOKEN=your-github-personal-access-token-for-private-nuget-packages
```

### Manual Image Builds

Build specific projects locally:

```bash
# Frontend (no private packages needed)
docker build -t actus-designer ./samples/actus-designer

# .NET Projects (may require PKG_TOKEN for GPU packages)
export PKG_TOKEN=your_github_personal_access_token

# API
docker build --build-arg PKG_TOKEN=$PKG_TOKEN -t actus-insurance-api ./samples/ActusInsurance.FastEndpointsSqliteGpuSample

# CLI Projects - Option 1: Individual Dockerfiles
docker build --build-arg PKG_TOKEN=$PKG_TOKEN -t scenario-calc-demo ./samples/ScenarioCpuGpuCalcDateDemo
docker build --build-arg PKG_TOKEN=$PKG_TOKEN -t pam-monte-carlo ./CLI/PamMonteCarlo50Y

# CLI Projects - Option 2: Shared Dockerfile (from root)
docker build --build-arg PROJECT=ScenarioCpuGpuCalcDateDemo \
             --build-arg PROJECT_PATH=samples/ScenarioCpuGpuCalcDateDemo \
             --build-arg PKG_TOKEN=$PKG_TOKEN \
             -f Dockerfile.cli -t scenario-calc-demo .
```

**Note**: The PKG_TOKEN is required for .NET projects that use private ActusInsurance.GPU NuGet packages. It should be a GitHub Personal Access Token with `read:packages` permission.

## 🤝 Contributing

1. Make sure all services start successfully
2. Test both API and frontend functionality  
3. Update documentation for any configuration changes
4. Follow the existing code patterns in both .NET and Next.js projects

For more detailed information, see the individual project READMEs in their respective directories.