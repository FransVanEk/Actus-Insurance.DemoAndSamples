using System.Text.Json;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using ActusInsurance.FastEndpointsSqliteGpuSample.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Services;

/// <summary>Background service that processes runs from the queue.</summary>
public sealed class RunWorkerService(
    RunQueue queue,
    IServiceScopeFactory scopeFactory,
    [FromKeyedServices("cpu")] ICalculationEngine cpuEngine,
    [FromKeyedServices("gpu")] ICalculationEngine gpuEngine,
    ICalculationEngine defaultEngine,
    ILogger<RunWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RunWorkerService started, default engine = {Engine}", defaultEngine.Label);

        await foreach (var runId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessRunAsync(runId, stoppingToken);
        }
    }

    private ICalculationEngine SelectEngine(string? preference) => preference switch
    {
        "GPU" => gpuEngine,
        "CPU" => cpuEngine,
        _     => defaultEngine,
    };

    private async Task ProcessRunAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var run = await db.Runs.FindAsync([runId], ct);
        if (run is null)
        {
            logger.LogWarning("Run {RunId} not found", runId);
            return;
        }

        run.State     = RunState.Running;
        run.StartedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;

        var engine = SelectEngine(run.EnginePreference);
        run.EngineUsed = engine.Label;
        await db.SaveChangesAsync(ct);

        try
        {
            // Load file contents
            string scenarioContent   = await ReadArtifactAsync(db, run.ScenarioArtifactId, ct);
            string riskContent       = await ReadArtifactAsync(db, run.RiskArtifactId, ct);
            string portfolioContent  = await ReadArtifactAsync(db, run.PortfolioArtifactId, ct);
            string sinkJson          = await ReadSinkAsync(db, run.SinkDefinitionId, ct);

            var parameters = run.ParametersJson is not null
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(run.ParametersJson) ?? []
                : new Dictionary<string, string>();

            var inputs = new CalculationInputs(
                scenarioContent, riskContent, portfolioContent, sinkJson, parameters);

            var progress = new Progress<ProgressInfo>(info =>
            {
                // Fire-and-forget progress updates (best effort)
                _ = UpdateProgressAsync(runId, info, scopeFactory, logger);
            });

            var result = await engine.ExecuteAsync(inputs, progress, ct);

            // Save result
            using var resultScope = scopeFactory.CreateScope();
            var resultDb = resultScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var finishedRun = await resultDb.Runs.FindAsync([runId], ct);
            if (finishedRun is not null)
            {
                finishedRun.State       = RunState.Completed;
                finishedRun.Progress    = 100;
                finishedRun.CompletedAt = DateTime.UtcNow;
                finishedRun.UpdatedAt   = DateTime.UtcNow;
                finishedRun.ResultJson  = JsonSerializer.Serialize(result);
                finishedRun.MetricsJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["stage"]        = "Done",
                    ["percent"]      = 100,
                    ["elapsed_ms"]   = result.DurationMs,
                    ["engine"]       = result.EngineLabel,
                    ["numScenarios"] = result.PortfolioPvByScenario.Length,
                    ["meanPv"]       = result.MeanPv,
                });
                await resultDb.SaveChangesAsync(ct);
            }

            logger.LogInformation("Run {RunId} completed ({Engine}, {Ms} ms)", runId, result.EngineLabel, result.DurationMs);
        }
        catch (OperationCanceledException)
        {
            using var cancelScope = scopeFactory.CreateScope();
            var cancelDb = cancelScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cancelRun = await cancelDb.Runs.FindAsync([runId], CancellationToken.None);
            if (cancelRun is not null)
            {
                cancelRun.State     = RunState.Canceled;
                cancelRun.UpdatedAt = DateTime.UtcNow;
                await cancelDb.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed", runId);
            using var errScope = scopeFactory.CreateScope();
            var errDb = errScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var errRun = await errDb.Runs.FindAsync([runId], CancellationToken.None);
            if (errRun is not null)
            {
                errRun.State        = RunState.Failed;
                errRun.ErrorMessage = ex.Message;
                errRun.UpdatedAt    = DateTime.UtcNow;
                await errDb.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    private static async Task UpdateProgressAsync(Guid runId, ProgressInfo info, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.Runs.FindAsync([runId]);
            if (run is not null)
            {
                run.Progress    = info.Percent;
                run.UpdatedAt   = DateTime.UtcNow;
                var elapsedMs = run.StartedAt.HasValue
                    ? (long)(DateTime.UtcNow - run.StartedAt.Value).TotalMilliseconds
                    : (long?)null;
                var metrics = new Dictionary<string, object>
                {
                    ["stage"]   = info.Stage,
                    ["percent"] = info.Percent,
                };
                if (elapsedMs.HasValue)
                    metrics["elapsed_ms"] = elapsedMs.Value;
                run.MetricsJson = JsonSerializer.Serialize(metrics);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to update progress for run {RunId} (best-effort update)", runId);
        }
    }

    private static async Task<string> ReadArtifactAsync(AppDbContext db, Guid? id, CancellationToken ct)
    {
        if (id is null) return string.Empty;
        var artifact = await db.FileArtifacts.FindAsync([id.Value], ct);
        if (artifact is null || !File.Exists(artifact.StoragePath)) return string.Empty;
        return await File.ReadAllTextAsync(artifact.StoragePath, ct);
    }

    private static async Task<string> ReadSinkAsync(AppDbContext db, Guid? id, CancellationToken ct)
    {
        if (id is null) return "{}";
        var sink = await db.SinkDefinitions.FindAsync([id.Value], ct);
        return sink?.JsonDefinition ?? "{}";
    }
}
