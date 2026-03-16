using ActusInsurance.FastEndpointsSqliteGpuSample.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<FileArtifact> FileArtifacts => Set<FileArtifact>();
    public DbSet<SinkDefinition> SinkDefinitions => Set<SinkDefinition>();
    public DbSet<RunRecord> Runs => Set<RunRecord>();
}
