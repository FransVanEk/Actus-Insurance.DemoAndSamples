using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Endpoints.Sinks;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class SinkDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string JsonDefinition { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateSinkRequest
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string JsonDefinition { get; set; } = "{}";
}

public class UpdateSinkRequest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string JsonDefinition { get; set; } = "{}";
}

public class SinkIdRequest
{
    public Guid Id { get; set; }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

public class CreateSinkEndpoint(AppDbContext db) : Endpoint<CreateSinkRequest, SinkDto>
{
    public override void Configure()
    {
        Post("/sinks");
        AllowAnonymous();
        Description(b => b.WithName("CreateSink").WithTags("Sinks")
            .WithSummary("Create a new sink definition"));
    }

    public override async Task HandleAsync(CreateSinkRequest req, CancellationToken ct)
    {
        var sink = new SinkDefinition
        {
            Name           = req.Name,
            Version        = req.Version,
            JsonDefinition = req.JsonDefinition,
        };
        db.SinkDefinitions.Add(sink);
        await db.SaveChangesAsync(ct);
        await HttpContext.Response.SendAsync(ToDto(sink), 201, cancellation: ct);
    }

    private static SinkDto ToDto(SinkDefinition s) => new()
    {
        Id             = s.Id,
        Name           = s.Name,
        Version        = s.Version,
        JsonDefinition = s.JsonDefinition,
        CreatedAt      = s.CreatedAt,
        UpdatedAt      = s.UpdatedAt,
    };
}

public class ListSinksEndpoint(AppDbContext db) : EndpointWithoutRequest<List<SinkDto>>
{
    public override void Configure()
    {
        Get("/sinks");
        AllowAnonymous();
        Description(b => b.WithName("ListSinks").WithTags("Sinks")
            .WithSummary("List all sink definitions"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sinks = await db.SinkDefinitions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SinkDto
            {
                Id             = s.Id,
                Name           = s.Name,
                Version        = s.Version,
                JsonDefinition = s.JsonDefinition,
                CreatedAt      = s.CreatedAt,
                UpdatedAt      = s.UpdatedAt,
            })
            .ToListAsync(ct);

        await HttpContext.Response.SendAsync(sinks, cancellation: ct);
    }
}

public class GetSinkEndpoint(AppDbContext db) : Endpoint<SinkIdRequest, SinkDto>
{
    public override void Configure()
    {
        Get("/sinks/{id}");
        AllowAnonymous();
        Description(b => b.WithName("GetSink").WithTags("Sinks")
            .WithSummary("Get a sink definition by ID"));
    }

    public override async Task HandleAsync(SinkIdRequest req, CancellationToken ct)
    {
        var sink = await db.SinkDefinitions.FindAsync([req.Id], ct);
        if (sink is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        await HttpContext.Response.SendAsync(new SinkDto
        {
            Id             = sink.Id,
            Name           = sink.Name,
            Version        = sink.Version,
            JsonDefinition = sink.JsonDefinition,
            CreatedAt      = sink.CreatedAt,
            UpdatedAt      = sink.UpdatedAt,
        }, cancellation: ct);
    }
}

public class UpdateSinkEndpoint(AppDbContext db) : Endpoint<UpdateSinkRequest, SinkDto>
{
    public override void Configure()
    {
        Put("/sinks/{id}");
        AllowAnonymous();
        Description(b => b.WithName("UpdateSink").WithTags("Sinks")
            .WithSummary("Update a sink definition"));
    }

    public override async Task HandleAsync(UpdateSinkRequest req, CancellationToken ct)
    {
        var sink = await db.SinkDefinitions.FindAsync([req.Id], ct);
        if (sink is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        sink.Name           = req.Name;
        sink.Version        = req.Version;
        sink.JsonDefinition = req.JsonDefinition;
        sink.UpdatedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await HttpContext.Response.SendAsync(new SinkDto
        {
            Id             = sink.Id,
            Name           = sink.Name,
            Version        = sink.Version,
            JsonDefinition = sink.JsonDefinition,
            CreatedAt      = sink.CreatedAt,
            UpdatedAt      = sink.UpdatedAt,
        }, cancellation: ct);
    }
}

public class DeleteSinkEndpoint(AppDbContext db) : Endpoint<SinkIdRequest, EmptyResponse>
{
    public override void Configure()
    {
        Delete("/sinks/{id}");
        AllowAnonymous();
        Description(b => b.WithName("DeleteSink").WithTags("Sinks")
            .WithSummary("Delete a sink definition"));
    }

    public override async Task HandleAsync(SinkIdRequest req, CancellationToken ct)
    {
        var sink = await db.SinkDefinitions.FindAsync([req.Id], ct);
        if (sink is null) { await HttpContext.Response.SendNotFoundAsync(ct); return; }

        db.SinkDefinitions.Remove(sink);
        await db.SaveChangesAsync(ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
