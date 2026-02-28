using System.Security.Cryptography;
using System.Text;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Samples;

public class CreateDefaultSampleResponse
{
    public Guid ScenarioArtifactId { get; set; }
    public Guid RiskArtifactId { get; set; }
    public Guid PortfolioArtifactId { get; set; }
    public Guid SinkDefinitionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreateDefaultSampleEndpoint(AppDbContext db, IWebHostEnvironment env)
    : EndpointWithoutRequest<CreateDefaultSampleResponse>
{
    public override void Configure()
    {
        Post("/samples/create-default");
        AllowAnonymous();
        Description(b => b.WithName("CreateDefaultSample").WithTags("Samples")
            .WithSummary("Create default sample datasets (idempotent). Use the returned IDs to start a run."));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var blobDir = Path.Combine(env.ContentRootPath, "data", "blobs");
        Directory.CreateDirectory(blobDir);

        var scenarioId  = await EnsureArtifactAsync("Scenario",  "scenario.json",  blobDir, ct);
        var riskId      = await EnsureArtifactAsync("Risk",       "risk.json",      blobDir, ct);
        var portfolioId = await EnsureArtifactAsync("Portfolio",  "portfolio.json", blobDir, ct);
        var sinkId      = await EnsureSinkAsync(ct);

        await HttpContext.Response.SendAsync(new CreateDefaultSampleResponse
        {
            ScenarioArtifactId  = scenarioId,
            RiskArtifactId      = riskId,
            PortfolioArtifactId = portfolioId,
            SinkDefinitionId    = sinkId,
            Message             = "Default sample data ready. Use the returned IDs with POST /runs to start a calculation.",
        }, 200, cancellation: ct);
    }

    private async Task<Guid> EnsureArtifactAsync(string type, string sampleFile, string blobDir, CancellationToken ct)
    {
        var existing = await db.FileArtifacts
            .Where(f => f.Type == type && f.FileName == $"sample_{sampleFile}")
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return existing.Id;

        var dataDir    = Path.Combine(env.ContentRootPath, "samples-data");
        var sourcePath = Path.Combine(dataDir, sampleFile);
        string content = File.Exists(sourcePath)
            ? await File.ReadAllTextAsync(sourcePath, ct)
            : $"{{\"sample\": \"{type}\"}}";

        var bytes       = Encoding.UTF8.GetBytes(content);
        var storagePath = Path.Combine(blobDir, $"sample_{sampleFile}");
        await File.WriteAllBytesAsync(storagePath, bytes, ct);

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var artifact = new FileArtifact
        {
            Type        = type,
            FileName    = $"sample_{sampleFile}",
            ContentType = "application/json",
            Size        = bytes.Length,
            Sha256      = sha256,
            StoragePath = storagePath,
        };
        db.FileArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        return artifact.Id;
    }

    private async Task<Guid> EnsureSinkAsync(CancellationToken ct)
    {
        var existing = await db.SinkDefinitions
            .Where(s => s.Name == "Default Sample Sink")
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return existing.Id;

        var dataDir    = Path.Combine(env.ContentRootPath, "samples-data");
        var sourcePath = Path.Combine(dataDir, "sink-definition.json");
        string json = File.Exists(sourcePath)
            ? await File.ReadAllTextAsync(sourcePath, ct)
            : "{\"output\": \"csv\", \"aggregation\": \"portfolio\"}";

        var sink = new SinkDefinition
        {
            Name           = "Default Sample Sink",
            Version        = "1.0",
            JsonDefinition = json,
        };
        db.SinkDefinitions.Add(sink);
        await db.SaveChangesAsync(ct);
        return sink.Id;
    }
}
