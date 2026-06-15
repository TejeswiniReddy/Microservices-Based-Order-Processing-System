using OrderFlow.InventoryService;
using OrderFlow.Platform.Caching;
using OrderFlow.Platform.Messaging;
using OrderFlow.Platform.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOrderFlowSecurity(builder.Configuration);

// Default (dev/test): in-memory providers, zero external dependencies.
builder.Services.AddInMemoryEventBus();
builder.Services.AddInMemoryCache();
builder.Services.AddInventoryModule();

#if CLOUD
// Production: swap in Redis cache, Azure Service Bus, and EF Core SQL Server,
// selected by configuration (Providers:Bus / Providers:Cache / Providers:Storage).
builder.Services.AddOrderFlowCloud(builder.Configuration);
#endif

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "inventory" }));

app.Run();
