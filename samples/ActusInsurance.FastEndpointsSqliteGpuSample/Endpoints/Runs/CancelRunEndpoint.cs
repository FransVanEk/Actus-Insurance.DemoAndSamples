using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class CancelRunEndpoint(AppDbContext db) : EndpointWithoutRequest<EmptyResponse>
{
    public override void Configure()
    {
        Post("/runs/{runId}/cancel");
        AllowAnonymous();
        Description(b => b.WithName("CancelRun").WithTags("Runs")
            .WithSummary("Request cancellation of a queued or running run"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var runId = Route<Guid>("runId");
        var run = await db.Runs.FindAsync([runId], ct);
        if (run is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        if (run.State is RunState.Completed or RunState.Failed or RunState.Canceled)
        {
            await HttpContext.Response.SendAsync(new { error = $"Cannot cancel a run in state '{run.State}'" }, 409, cancellation: ct);
            return;
        }

        run.State     = RunState.Canceled;
        run.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
