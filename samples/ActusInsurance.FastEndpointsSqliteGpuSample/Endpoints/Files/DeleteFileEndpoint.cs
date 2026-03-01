using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class DeleteFileByIdRequest
{
    public Guid Id { get; set; }
}

public class DeleteFileEndpoint(AppDbContext db, ILogger<DeleteFileEndpoint> logger) : Endpoint<DeleteFileByIdRequest, EmptyResponse>
{
    public override void Configure()
    {
        Delete("/files/{id}");
        AllowAnonymous();
        Description(b => b.WithName("DeleteFile").WithTags("Files")
            .WithSummary("Delete a file artifact and its stored blob"));
    }

    public override async Task HandleAsync(DeleteFileByIdRequest req, CancellationToken ct)
    {
        var artifact = await db.FileArtifacts.FindAsync([req.Id], ct);
        if (artifact is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        // Remove blob from disk (best-effort; file may already be gone)
        if (File.Exists(artifact.StoragePath))
        {
            try { File.Delete(artifact.StoragePath); }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete blob for artifact {Id} at {Path}", artifact.Id, artifact.StoragePath);
            }
        }

        db.FileArtifacts.Remove(artifact);
        await db.SaveChangesAsync(ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
