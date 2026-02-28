using System.Security.Cryptography;
using System.Text;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using ActusInsurance.FastEndpointsSqliteGpuSample.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class DemoNameRequest
{
    public string DemoName { get; set; } = string.Empty;
}

/// <summary>
/// Starts a predefined demo run using packaged sample data.
/// Demo names: "basic", "gpu-showcase"
/// </summary>
public class StartDemoRunEndpoint(AppDbContext db, RunQueue queue, IWebHostEnvironment env)
    : Endpoint<DemoNameRequest, StartRunResponse>
{
    private static readonly HashSet<string> ValidDemos = ["basic", "gpu-showcase"];

    public override void Configure()
    {
        Post("/runs/demo/{demoName}");
        AllowAnonymous();
        Description(b => b.WithName("StartDemoRun").WithTags("Runs")
            .WithSummary("Start a predefined demo run using packaged sample data. Demo names: basic, gpu-showcase"));
    }

    public override async Task HandleAsync(DemoNameRequest req, CancellationToken ct)
    {
        if (!ValidDemos.Contains(req.DemoName))
        {
            await HttpContext.Response.SendAsync(new { error = $"Unknown demo '{req.DemoName}'. Valid demos: {string.Join(", ", ValidDemos)}" }, 404, cancellation: ct);
            return;
        }

        // Ensure sample data artifacts exist (idempotent)
        var (scenarioId, riskId, portfolioId, sinkId) = await EnsureSampleDataAsync(ct);

        var parameters = new Dictionary<string, string>
        {
            ["demo"]    = req.DemoName,
            ["demoHint"] = req.DemoName == "gpu-showcase"
                ? "prefer-gpu"
                : "cpu-only",
        };

        var run = new RunRecord
        {
            ScenarioArtifactId  = scenarioId,
            RiskArtifactId      = riskId,
            PortfolioArtifactId = portfolioId,
            SinkDefinitionId    = sinkId,
            ParametersJson      = System.Text.Json.JsonSerializer.Serialize(parameters),
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(run.Id, ct);

        await HttpContext.Response.SendAsync(new StartRunResponse
        {
            RunId     = run.Id,
            StatusUrl = $"/runs/{run.Id}/status",
            ResultUrl = $"/runs/{run.Id}/result",
            State     = run.State,
        }, 202, cancellation: ct);
    }

    private async Task<(Guid scenario, Guid risk, Guid portfolio, Guid sink)> EnsureSampleDataAsync(CancellationToken ct)
    {
        var blobDir = Path.Combine(env.ContentRootPath, "data", "blobs");
        Directory.CreateDirectory(blobDir);

        var scenarioId  = await EnsureArtifactAsync("Scenario",  "scenario.json",  blobDir, ct);
        var riskId      = await EnsureArtifactAsync("Risk",       "risk.json",      blobDir, ct);
        var portfolioId = await EnsureArtifactAsync("Portfolio",  "portfolio.json", blobDir, ct);
        var sinkId      = await EnsureSinkAsync(ct);

        return (scenarioId, riskId, portfolioId, sinkId);
    }

    private async Task<Guid> EnsureArtifactAsync(string type, string sampleFile, string blobDir, CancellationToken ct)
    {
        // Check if already seeded (look for a file with the sample filename marker)
        var existing = await db.FileArtifacts
            .Where(f => f.Type == type && f.FileName == $"sample_{sampleFile}")
            .FirstOrDefaultAsync(ct);

        if (existing is not null) return existing.Id;

        var dataDir = Path.Combine(env.ContentRootPath, "samples-data");
        var sourcePath = Path.Combine(dataDir, sampleFile);
        string content = File.Exists(sourcePath)
            ? await File.ReadAllTextAsync(sourcePath, ct)
            : $"{{\"sample\": \"{type}\", \"note\": \"placeholder\"}}";

        var bytes = Encoding.UTF8.GetBytes(content);
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
