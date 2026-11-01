using IconChest.Core.Versioning;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    version = new VersionInfo().Version,
    timestamp = DateTime.UtcNow
}));

app.Run();

public partial class Program { }

