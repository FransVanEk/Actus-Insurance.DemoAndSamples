using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using ActusInsurance.FastEndpointsSqliteGpuSample.Services;
using ActusInsurance.FastEndpointsSqliteGpuSample.Engines;
using FastEndpoints;
using System.Text.Json;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Runs;

public class StartPamMonteCarloRequest
{
    /// <summary>Number of contracts in the portfolio (synthetic mode)</summary>
    public int NumContracts { get; set; } = 1000;
    
    /// <summary>Number of Monte Carlo scenarios</summary>
    public int NumScenarios { get; set; } = 100;
    
    /// <summary>Number of months to maturity (default: 600 = 50 years, synthetic mode)</summary>
    public int MonthsToMaturity { get; set; } = 600;
    
    /// <summary>Calculation date index (month offset from base date)</summary>
    public int CalcDateIndex { get; set; } = 0;
    
    /// <summary>Random seed for reproducible results</summary>
    public ulong Seed { get; set; } = 12345;
    
    /// <summary>Base date for the calculation</summary>
    public DateTime BaseDate { get; set; } = DateTime.Today;
    
    /// <summary>Prefer GPU computation if available</summary>
    public bool PreferGpu { get; set; } = false;
    
    /// <summary>Optional run description</summary>
    public string? Description { get; set; }
    
    // ── File upload mode (alternative to synthetic generation) ──
    /// <summary>Portfolio CSV content (replaces synthetic generation)</summary>
    public string? PortfolioCsv { get; set; }
    
    /// <summary>Contract metadata CSV content</summary>
    public string? MetadataCsv { get; set; }
    
    /// <summary>Scenario data as JSON (replaces synthetic scenarios)</summary>
    public string? ScenarioJson { get; set; }
}

public class StartPamMonteCarloResponse
{
    public Guid RunId { get; set; }
    public string StatusUrl { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public string State { get; set; } = RunState.Queued;
    public string Description { get; set; } = string.Empty;
}

public class StartPamMonteCarloEndpoint(AppDbContext db, RunQueue queue) : Endpoint<StartPamMonteCarloRequest, StartPamMonteCarloResponse>
{
    public override void Configure()
    {
        Post("/runs/pam-monte-carlo");
        AllowAnonymous();
        Description(b => b.WithName("StartPamMonteCarlo").WithTags("Runs")
            .WithSummary("Start a PAM Monte Carlo calculation run with specified parameters"));
    }

    public override async Task HandleAsync(StartPamMonteCarloRequest req, CancellationToken ct)
    {
        // Determine mode: file input vs synthetic generation
        bool hasFileInput = !string.IsNullOrEmpty(req.PortfolioCsv) || !string.IsNullOrEmpty(req.ScenarioJson);
        
        // Convert PAM-specific parameters to generic parameters dictionary
        var parameters = new Dictionary<string, string>
        {
            ["contracts"] = req.NumContracts.ToString(),
            ["scenarios"] = req.NumScenarios.ToString(),
            ["months"] = req.MonthsToMaturity.ToString(),
            ["calcDateIndex"] = req.CalcDateIndex.ToString(),
            ["seed"] = req.Seed.ToString(),
            ["baseDate"] = req.BaseDate.ToString("yyyy-MM-dd"),
            ["type"] = "pam-monte-carlo",
            ["hasFileInput"] = hasFileInput.ToString()
        };
        
        // Add file data to parameters if provided
        if (hasFileInput)
        {
            if (!string.IsNullOrEmpty(req.PortfolioCsv))
                parameters["portfolioCsv"] = req.PortfolioCsv;
            if (!string.IsNullOrEmpty(req.MetadataCsv))
                parameters["metadataCsv"] = req.MetadataCsv;
            if (!string.IsNullOrEmpty(req.ScenarioJson))
                parameters["scenarioJson"] = req.ScenarioJson;
        }

        var description = !string.IsNullOrEmpty(req.Description) 
            ? req.Description 
            : hasFileInput 
                ? $"PAM Monte Carlo: Custom data × {req.NumScenarios:N0} scenarios"
                : $"PAM Monte Carlo: {req.NumContracts:N0} contracts × {req.NumScenarios:N0} scenarios";

        var run = new RunRecord
        {
            // For PAM Monte Carlo, we don't need specific artifacts - parameters define everything
            ScenarioArtifactId = null,
            RiskArtifactId = null, 
            PortfolioArtifactId = null,
            SinkDefinitionId = null,
            EnginePreference = req.PreferGpu ? "PAM_GPU" : "PAM_CPU",
            ParametersJson = JsonSerializer.Serialize(parameters),
            Description = description
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(run.Id, ct);

        await HttpContext.Response.SendAsync(new StartPamMonteCarloResponse
        {
            RunId = run.Id,
            StatusUrl = $"/runs/{run.Id}/status",
            ResultUrl = $"/runs/{run.Id}/result",
            State = run.State,
            Description = description
        }, 202, cancellation: ct);
    }
}