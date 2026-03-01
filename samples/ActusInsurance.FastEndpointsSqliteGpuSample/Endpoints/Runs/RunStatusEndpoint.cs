using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;
using System.Text.Json;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class RunIdRequest
{
    public Guid RunId { get; set; }
}

public class RunStatusResponse
{
    public Guid RunId { get; set; }
    public string State { get; set; } = string.Empty;
    public int Progress0To100 { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Engine { get; set; }
    /// <summary>
    /// Runtime metrics from the last completed progress update.
    /// Keys include elapsed_ms (once started), stage (current stage name).
    /// </summary>
    public Dictionary<string, JsonElement>? Metrics { get; set; }
}

public class RunStatusEndpoint(AppDbContext db) : Endpoint<RunIdRequest, RunStatusResponse>
{
    public override void Configure()
    {
        Get("/runs/{runId}/status");
        AllowAnonymous();
        Description(b => b.WithName("GetRunStatus").WithTags("Runs")
            .WithSummary("Get the current status and progress of a run"));
    }

    public override async Task HandleAsync(RunIdRequest req, CancellationToken ct)
    {
        var run = await db.Runs.FindAsync([req.RunId], ct);
        if (run is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        await HttpContext.Response.SendAsync(new RunStatusResponse
        {
            RunId        = run.Id,
            State        = run.State,
            Progress0To100 = run.Progress,
            Message      = run.ErrorMessage,
            CreatedAt    = run.CreatedAt,
            StartedAt    = run.StartedAt,
            UpdatedAt    = run.UpdatedAt,
            Engine       = string.IsNullOrEmpty(run.EngineUsed) ? null : run.EngineUsed,
            Metrics      = run.MetricsJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(run.MetricsJson)
                : null,
        }, cancellation: ct);
    }
}
