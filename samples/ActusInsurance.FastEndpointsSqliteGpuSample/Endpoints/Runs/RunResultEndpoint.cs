using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;
using System.Text.Json;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class RunResultResponse
{
    public Guid RunId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Engine { get; set; }
    /// <summary>Full calculation result JSON when state is Completed.</summary>
    public JsonElement? Result { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class RunResultEndpoint(AppDbContext db) : Endpoint<RunIdRequest, RunResultResponse>
{
    public override void Configure()
    {
        Get("/runs/{runId}/result");
        AllowAnonymous();
        Description(b => b.WithName("GetRunResult").WithTags("Runs")
            .WithSummary("Get the final result of a completed run"));
    }

    public override async Task HandleAsync(RunIdRequest req, CancellationToken ct)
    {
        var run = await db.Runs.FindAsync([req.RunId], ct);
        if (run is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        if (run.State != RunState.Completed)
        {
            await HttpContext.Response.SendAsync(new RunResultResponse
            {
                RunId  = run.Id,
                State  = run.State,
                Engine = string.IsNullOrEmpty(run.EngineUsed) ? null : run.EngineUsed,
            }, run.State == RunState.Failed ? 422 : 202, cancellation: ct);
            return;
        }

        JsonElement? result = null;
        if (run.ResultJson is not null)
            result = JsonSerializer.Deserialize<JsonElement>(run.ResultJson);

        await HttpContext.Response.SendAsync(new RunResultResponse
        {
            RunId       = run.Id,
            State       = run.State,
            Engine      = run.EngineUsed,
            Result      = result,
            CompletedAt = run.CompletedAt,
        }, cancellation: ct);
    }
}
