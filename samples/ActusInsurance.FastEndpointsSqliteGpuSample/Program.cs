using ActusInsurance.FastEndpointsSqliteGpuSample.Data;
using ActusInsurance.FastEndpointsSqliteGpuSample.Engines;
using ActusInsurance.FastEndpointsSqliteGpuSample.Services;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "app.db");
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// ── Calculation engines ───────────────────────────────────────────────────────
// Both engines are registered as keyed singletons for per-run selection.
// The unkeyed ICalculationEngine binding follows the global "Calculation:PreferGpu" config.
builder.Services.AddKeyedSingleton<ICalculationEngine, CpuCalculationEngine>("cpu");
builder.Services.AddKeyedSingleton<ICalculationEngine, GpuCalculationEngine>("gpu");

bool preferGpu = builder.Configuration.GetValue<bool>("Calculation:PreferGpu");
builder.Services.AddSingleton<ICalculationEngine>(sp =>
    sp.GetRequiredKeyedService<ICalculationEngine>(preferGpu ? "gpu" : "cpu"));

// ── Run queue + background worker ─────────────────────────────────────────────
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddHostedService<RunWorkerService>();

// ── FastEndpoints ─────────────────────────────────────────────────────────────
builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o =>
{
    o.DocumentSettings = s =>
    {
        s.Title       = "Actus Insurance – FastEndpoints + SQLite + GPU Sample";
        s.Version     = "v1";
        s.Description = "Async insurance calculation API: upload scenario/risk/portfolio files, manage sink definitions, start calculation runs (CPU or GPU engine), and poll for results.";
    };
});

var app = builder.Build();

// ── Auto-migrate DB on startup ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseFastEndpoints();
app.UseSwaggerGen();

// Workaround for a FastEndpoints 8.x / .NET 9.13 incompatibility:
// FastEndpoints' ConfigureSerializer() does:
//   opts.TypeInfoResolver = opts.TypeInfoResolver?.WithAddedModifier(...)
// In .NET 9.13 the JsonSerializerOptions.TypeInfoResolver getter returns the
// internal OptionsBoundJsonTypeInfoResolverChain object even when the chain is
// empty, so WithAddedModifier wraps the chain itself → chain = [Modifier(chain)]
// → circular → stack overflow in EndpointDefinition.GetToHeaderProps on first request.
// Fix: after UseFastEndpoints (after ConfigureSerializer has run), replace the
// entire resolver chain with a single DefaultJsonTypeInfoResolver so that
// TypeInfoResolver returns that object directly (not the chain), breaking the cycle.
var feConfig = app.Services.GetRequiredService<Config>();
feConfig.Serializer.Options.TypeInfoResolverChain.Clear();
feConfig.Serializer.Options.TypeInfoResolverChain.Add(
    new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());

app.Run();
