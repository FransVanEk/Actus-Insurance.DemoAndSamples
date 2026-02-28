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

// ── Calculation engine ────────────────────────────────────────────────────────
bool preferGpu = builder.Configuration.GetValue<bool>("Calculation:PreferGpu");
if (preferGpu)
    builder.Services.AddSingleton<ICalculationEngine, GpuCalculationEngine>();
else
    builder.Services.AddSingleton<ICalculationEngine, CpuCalculationEngine>();

// ── Run queue + background worker ─────────────────────────────────────────────
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddHostedService<RunWorkerService>();

// ── FastEndpoints + Swagger ───────────────────────────────────────────────────
builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(o =>
{
    o.DocumentSettings = s =>
    {
        s.Title       = "Actus Insurance — FastEndpoints + SQLite + GPU Sample";
        s.Version     = "v1";
        s.Description = "Async insurance run API demonstrating scenario/risk/portfolio processing with CPU and GPU (simulated) engines.";
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

app.Run();
