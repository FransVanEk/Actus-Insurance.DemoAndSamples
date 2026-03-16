using System.Security.Cryptography;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
}

public class UploadFileResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class UploadScenariosEndpoint(AppDbContext db, IWebHostEnvironment env) 
    : Endpoint<UploadFileRequest, UploadFileResponse>
{
    public override void Configure()
    {
        Post("/files/scenarios");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("UploadScenario").WithTags("Files")
            .WithSummary("Upload a scenario file"));
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
        => await UploadHelper.HandleAsync(req, "Scenario", db, env, this, ct);
}

public class UploadRisksEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<UploadFileRequest, UploadFileResponse>
{
    public override void Configure()
    {
        Post("/files/risks");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("UploadRisk").WithTags("Files")
            .WithSummary("Upload a risk file"));
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
        => await UploadHelper.HandleAsync(req, "Risk", db, env, this, ct);
}

public class UploadPortfoliosEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<UploadFileRequest, UploadFileResponse>
{
    public override void Configure()
    {
        Post("/files/portfolios");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("UploadPortfolio").WithTags("Files")
            .WithSummary("Upload a portfolio file"));
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
        => await UploadHelper.HandleAsync(req, "Portfolio", db, env, this, ct);
}

public class UploadSinksFileEndpoint(AppDbContext db, IWebHostEnvironment env)
    : Endpoint<UploadFileRequest, UploadFileResponse>
{
    public override void Configure()
    {
        Post("/files/sinks");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("UploadSinkFile").WithTags("Files")
            .WithSummary("Upload a sink file"));
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
        => await UploadHelper.HandleAsync(req, "Sink", db, env, this, ct);
}

internal static class UploadHelper
{
    internal static async Task HandleAsync(
        UploadFileRequest req,
        string type,
        AppDbContext db,
        IWebHostEnvironment env,
        IEndpoint endpoint,
        CancellationToken ct)
    {
        if (req.File is null || req.File.Length == 0)
        {
            await endpoint.HttpContext.Response.SendAsync(
                new { error = "File is required" }, 400, cancellation: ct);
            return;
        }

        var blobDir = Path.Combine(env.ContentRootPath, "data", "blobs");
        Directory.CreateDirectory(blobDir);

        var storagePath = Path.Combine(blobDir, $"{Guid.NewGuid()}_{req.File.FileName}");
        await using var stream = File.Create(storagePath);
        await req.File.CopyToAsync(stream, ct);

        // Compute SHA-256
        stream.Position = 0;
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var artifact = new FileArtifact
        {
            Type        = type,
            FileName    = req.File.FileName,
            ContentType = req.File.ContentType,
            Size        = req.File.Length,
            Sha256      = sha256,
            StoragePath = storagePath,
        };

        db.FileArtifacts.Add(artifact);
        await db.SaveChangesAsync(ct);

        await endpoint.HttpContext.Response.SendAsync(new UploadFileResponse
        {
            Id        = artifact.Id,
            Type      = artifact.Type,
            FileName  = artifact.FileName,
            Size      = artifact.Size,
            Sha256    = artifact.Sha256,
            CreatedAt = artifact.CreatedAt,
        }, 201, cancellation: ct);
    }
}
