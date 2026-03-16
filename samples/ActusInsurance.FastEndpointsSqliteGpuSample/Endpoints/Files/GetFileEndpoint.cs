using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using FastEndpoints;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class GetFileByIdRequest
{
    public Guid Id { get; set; }
}

public class GetFileEndpoint(AppDbContext db) : Endpoint<GetFileByIdRequest, FileArtifactDto>
{
    public override void Configure()
    {
        Get("/files/{id}");
        AllowAnonymous();
        Description(b => b.WithName("GetFile").WithTags("Files")
            .WithSummary("Get metadata for a specific uploaded file artifact by ID"));
    }

    public override async Task HandleAsync(GetFileByIdRequest req, CancellationToken ct)
    {
        var artifact = await db.FileArtifacts.FindAsync([req.Id], ct);
        if (artifact is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        await HttpContext.Response.SendAsync(new FileArtifactDto
        {
            Id        = artifact.Id,
            Type      = artifact.Type,
            FileName  = artifact.FileName,
            Size      = artifact.Size,
            Sha256    = artifact.Sha256,
            CreatedAt = artifact.CreatedAt,
        }, cancellation: ct);
    }
}
