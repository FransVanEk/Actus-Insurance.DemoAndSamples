using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Files;

public class ListFilesRequest
{
    public string? Type { get; set; }
}

public class FileArtifactDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ListFilesEndpoint(AppDbContext db) : Endpoint<ListFilesRequest, List<FileArtifactDto>>
{
    public override void Configure()
    {
        Get("/files");
        AllowAnonymous();
        Description(b => b.WithName("ListFiles").WithTags("Files")
            .WithSummary("List uploaded files, optionally filtered by type (Scenario|Risk|Portfolio|Sink)"));
    }

    public override async Task HandleAsync(ListFilesRequest req, CancellationToken ct)
    {
        var query = db.FileArtifacts.AsQueryable();
        if (!string.IsNullOrEmpty(req.Type))
            query = query.Where(f => f.Type == req.Type);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FileArtifactDto
            {
                Id        = f.Id,
                Type      = f.Type,
                FileName  = f.FileName,
                Size      = f.Size,
                Sha256    = f.Sha256,
                CreatedAt = f.CreatedAt,
            })
            .ToListAsync(ct);

        await HttpContext.Response.SendAsync(items, cancellation: ct);
    }
}
