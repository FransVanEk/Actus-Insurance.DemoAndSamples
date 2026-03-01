namespace ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;

public class RunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = RunState.Queued;
    public int Progress { get; set; }
    public string EngineUsed { get; set; } = string.Empty;
    /// <summary>
    /// Optional per-run engine preference: "CPU" | "GPU" | null (use global config default).
    /// </summary>
    public string? EnginePreference { get; set; }
    public Guid? ScenarioArtifactId { get; set; }
    public Guid? RiskArtifactId { get; set; }
    public Guid? PortfolioArtifactId { get; set; }
    public Guid? SinkDefinitionId { get; set; }
    public string? ParametersJson { get; set; }
    public string? ResultJson { get; set; }
    /// <summary>JSON-serialized runtime metrics (elapsed_ms, stage, etc.).</summary>
    public string? MetricsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class RunState
{
    public const string Queued    = "Queued";
    public const string Running   = "Running";
    public const string Completed = "Completed";
    public const string Failed    = "Failed";
    public const string Canceled  = "Canceled";
}
