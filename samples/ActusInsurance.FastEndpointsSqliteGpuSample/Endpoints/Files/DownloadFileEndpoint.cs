using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class DownloadFileByIdRequest
{
    public Guid Id { get; set; }
}

/// <summary>
/// Streams the raw bytes of an uploaded file artifact back to the caller.
/// Response Content-Type mirrors the original upload's content-type.
/// </summary>
public class DownloadFileEndpoint(AppDbContext db) : Endpoint<DownloadFileByIdRequest, EmptyResponse>
{
    public override void Configure()
    {
        Get("/files/{id}/content");
        AllowAnonymous();
        Description(b => b.WithName("DownloadFile").WithTags("Files")
            .WithSummary("Download the raw content of an uploaded file artifact"));
    }

    public override async Task HandleAsync(DownloadFileByIdRequest req, CancellationToken ct)
    {
        var artifact = await db.FileArtifacts.FindAsync([req.Id], ct);
        if (artifact is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        if (!File.Exists(artifact.StoragePath))
        {
            await HttpContext.Response.SendAsync(
                new { error = "File content not found on disk" }, 404, cancellation: ct);
            return;
        }

        HttpContext.Response.ContentType = string.IsNullOrEmpty(artifact.ContentType)
            ? "application/octet-stream"
            : artifact.ContentType;

        HttpContext.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{artifact.FileName}\"";
        HttpContext.Response.Headers["X-Content-Sha256"] = artifact.Sha256;

        await HttpContext.Response.StartAsync(ct);
        await using var fs = File.OpenRead(artifact.StoragePath);
        await fs.CopyToAsync(HttpContext.Response.Body, ct);
    }
}
