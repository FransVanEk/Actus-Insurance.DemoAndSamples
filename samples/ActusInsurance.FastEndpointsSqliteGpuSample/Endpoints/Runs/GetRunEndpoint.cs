using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;
using System.Text.Json;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class GetRunResponse
{
    public Guid Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? Engine { get; set; }
    public string? EnginePreference { get; set; }
    public Guid? ScenarioArtifactId { get; set; }
    public Guid? RiskArtifactId { get; set; }
    public Guid? PortfolioArtifactId { get; set; }
    public Guid? SinkDefinitionId { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Full calculation result JSON when state is Completed.</summary>
    public JsonElement? Result { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Returns the full detail of a single run including status, metadata and result (if available).
/// This is a convenience endpoint that merges the status + result views.
/// </summary>
public class GetRunEndpoint(AppDbContext db) : Endpoint<RunIdRequest, GetRunResponse>
{
    public override void Configure()
    {
        Get("/runs/{runId}");
        AllowAnonymous();
        Description(b => b.WithName("GetRun").WithTags("Runs")
            .WithSummary("Get full run details (status + result) in a single call"));
    }

    public override async Task HandleAsync(RunIdRequest req, CancellationToken ct)
    {
        var run = await db.Runs.FindAsync([req.RunId], ct);
        if (run is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        JsonElement? result = null;
        if (run.State == RunState.Completed && run.ResultJson is not null)
            result = JsonSerializer.Deserialize<JsonElement>(run.ResultJson);

        await HttpContext.Response.SendAsync(new GetRunResponse
        {
            Id                  = run.Id,
            State               = run.State,
            Progress            = run.Progress,
            Engine              = string.IsNullOrEmpty(run.EngineUsed) ? null : run.EngineUsed,
            EnginePreference    = run.EnginePreference,
            ScenarioArtifactId  = run.ScenarioArtifactId,
            RiskArtifactId      = run.RiskArtifactId,
            PortfolioArtifactId = run.PortfolioArtifactId,
            SinkDefinitionId    = run.SinkDefinitionId,
            ErrorMessage        = run.ErrorMessage,
            Result              = result,
            CreatedAt           = run.CreatedAt,
            StartedAt           = run.StartedAt,
            CompletedAt         = run.CompletedAt,
            UpdatedAt           = run.UpdatedAt,
        }, cancellation: ct);
    }
}
