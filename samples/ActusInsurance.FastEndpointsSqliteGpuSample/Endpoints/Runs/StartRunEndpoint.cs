using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using ActusInsurance.FastEndpointsSqliteGpuSample.Services;
using FastEndpoints;
using System.Text.Json;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class StartRunRequest
{
    public Guid? ScenarioArtifactId { get; set; }
    public Guid? RiskArtifactId { get; set; }
    public Guid? PortfolioArtifactId { get; set; }
    public Guid? SinkDefinitionId { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    /// <summary>
    /// Optional per-run engine override.
    /// true = GPU (simulated), false = CPU, null = use global config default.
    /// </summary>
    public bool? PreferGpu { get; set; }
}

public class StartRunResponse
{
    public Guid RunId { get; set; }
    public string StatusUrl { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public string State { get; set; } = RunState.Queued;
}

public class StartRunEndpoint(AppDbContext db, RunQueue queue) : Endpoint<StartRunRequest, StartRunResponse>
{
    public override void Configure()
    {
        Post("/runs");
        AllowAnonymous();
        Description(b => b.WithName("StartRun").WithTags("Runs")
            .WithSummary("Start an async insurance calculation run. Returns immediately with a run ID for polling."));
    }

    public override async Task HandleAsync(StartRunRequest req, CancellationToken ct)
    {
        var run = new RunRecord
        {
            ScenarioArtifactId  = req.ScenarioArtifactId,
            RiskArtifactId      = req.RiskArtifactId,
            PortfolioArtifactId = req.PortfolioArtifactId,
            SinkDefinitionId    = req.SinkDefinitionId,
            EnginePreference    = req.PreferGpu switch { true => "GPU", false => "CPU", _ => null },
            ParametersJson      = req.Parameters is not null
                ? JsonSerializer.Serialize(req.Parameters)
                : null,
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
}
