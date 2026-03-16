using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class ListRunsRequest
{
    /// <summary>Filter by state: Queued|Running|Completed|Failed|Canceled</summary>
    public string? State { get; set; }
    /// <summary>Maximum number of runs to return (default 50, max 200)</summary>
    public int Limit { get; set; } = 50;
}

public class RunSummaryDto
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
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ListRunsEndpoint(AppDbContext db) : Endpoint<ListRunsRequest, List<RunSummaryDto>>
{
    public override void Configure()
    {
        Get("/runs");
        AllowAnonymous();
        Description(b => b.WithName("ListRuns").WithTags("Runs")
            .WithSummary("List all runs, newest first. Optional ?state= filter."));
    }

    public override async Task HandleAsync(ListRunsRequest req, CancellationToken ct)
    {
        var limit = Math.Clamp(req.Limit, 1, 200);
        var query = db.Runs.AsQueryable();

        if (!string.IsNullOrEmpty(req.State))
            query = query.Where(r => r.State == req.State);

        var runs = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new RunSummaryDto
            {
                Id                  = r.Id,
                State               = r.State,
                Progress            = r.Progress,
                Engine              = string.IsNullOrEmpty(r.EngineUsed) ? null : r.EngineUsed,
                EnginePreference    = r.EnginePreference,
                ScenarioArtifactId  = r.ScenarioArtifactId,
                RiskArtifactId      = r.RiskArtifactId,
                PortfolioArtifactId = r.PortfolioArtifactId,
                SinkDefinitionId    = r.SinkDefinitionId,
                ErrorMessage        = r.ErrorMessage,
                CreatedAt           = r.CreatedAt,
                StartedAt           = r.StartedAt,
                CompletedAt         = r.CompletedAt,
                UpdatedAt           = r.UpdatedAt,
            })
            .ToListAsync(ct);

        await HttpContext.Response.SendAsync(runs, cancellation: ct);
    }
}
