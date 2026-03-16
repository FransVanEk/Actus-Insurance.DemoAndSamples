using System.Security.Cryptography;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class BatchUploadRequest
{
    public IFormFileCollection Files { get; set; } = null!;
}

public class BatchUploadResponse
{
    public List<UploadFileResponse> Uploaded { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public class BatchUploadScenariosEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<BatchUploadRequest, BatchUploadResponse>
{
    public override void Configure()
    {
        Post("/files/scenarios/batch");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("BatchUploadScenarios").WithTags("Files")
            .WithSummary("Upload one or more scenario files in a single multipart request"));
    }

    public override async Task HandleAsync(BatchUploadRequest req, CancellationToken ct)
        => await BatchUploadHelper.HandleAsync(req, "Scenario", db, env, HttpContext, ct);
}

public class BatchUploadRisksEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<BatchUploadRequest, BatchUploadResponse>
{
    public override void Configure()
    {
        Post("/files/risks/batch");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("BatchUploadRisks").WithTags("Files")
            .WithSummary("Upload one or more risk files in a single multipart request"));
    }

    public override async Task HandleAsync(BatchUploadRequest req, CancellationToken ct)
        => await BatchUploadHelper.HandleAsync(req, "Risk", db, env, HttpContext, ct);
}

public class BatchUploadPortfoliosEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<BatchUploadRequest, BatchUploadResponse>
{
    public override void Configure()
    {
        Post("/files/portfolios/batch");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("BatchUploadPortfolios").WithTags("Files")
            .WithSummary("Upload one or more portfolio files in a single multipart request"));
    }

    public override async Task HandleAsync(BatchUploadRequest req, CancellationToken ct)
        => await BatchUploadHelper.HandleAsync(req, "Portfolio", db, env, HttpContext, ct);
}

internal static class BatchUploadHelper
{
    internal static async Task HandleAsync(
        BatchUploadRequest req,
        string type,
        AppDbContext db,
        IWebHostEnvironment env,
        HttpContext ctx,
        CancellationToken ct)
    {
        if (req.Files is null || req.Files.Count == 0)
        {
            await ctx.Response.SendAsync(
                new { error = "At least one file is required" }, 400, cancellation: ct);
            return;
        }

        var blobDir = Path.Combine(env.ContentRootPath, "data", "blobs");
        Directory.CreateDirectory(blobDir);

        var response = new BatchUploadResponse();

        foreach (var file in req.Files)
        {
            if (file.Length == 0)
            {
                response.Errors.Add($"{file.FileName}: file is empty, skipped");
                continue;
            }

            try
            {
                var storagePath = Path.Combine(blobDir, $"{Guid.NewGuid()}_{file.FileName}");
                await using var stream = File.Create(storagePath);
                await file.CopyToAsync(stream, ct);

                stream.Position = 0;
                var hashBytes = await SHA256.HashDataAsync(stream, ct);
                var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

                var artifact = new FileArtifact
                {
                    Type        = type,
                    FileName    = file.FileName,
                    ContentType = file.ContentType,
                    Size        = file.Length,
                    Sha256      = sha256,
                    StoragePath = storagePath,
                };
                db.FileArtifacts.Add(artifact);
                await db.SaveChangesAsync(ct);

                response.Uploaded.Add(new UploadFileResponse
                {
                    Id        = artifact.Id,
                    Type      = artifact.Type,
                    FileName  = artifact.FileName,
                    Size      = artifact.Size,
                    Sha256    = artifact.Sha256,
                    CreatedAt = artifact.CreatedAt,
                });
            }
            catch (Exception ex)
            {
                response.Errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        int statusCode = response.Uploaded.Count > 0 ? 201 : 400;
        await ctx.Response.SendAsync(response, statusCode, cancellation: ct);
    }
}
