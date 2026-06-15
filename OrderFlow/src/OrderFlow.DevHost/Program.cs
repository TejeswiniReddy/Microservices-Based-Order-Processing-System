using OrderFlow.InventoryService;
using OrderFlow.OrderService;
using OrderFlow.PaymentService;
using OrderFlow.Platform.Caching;
using OrderFlow.Platform.Messaging;
using OrderFlow.Platform.Security;

// ---------------------------------------------------------------------------
// DevHost: runs the Order, Payment and Inventory services in ONE process for
// local development and integration testing. All three modules register their
// handlers against a single shared in-memory event bus, so the full
// choreography saga (OrderCreated -> Payment -> Inventory -> Confirmed/Cancelled
// with refund compensation) executes end-to-end with zero external services.
//
// In production these are three independently deployed containers and the bus
// is Azure Service Bus; the cache is Redis; storage is SQL Server. The service
// code is identical — only the registered providers differ.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    // MVC only scans the entry assembly for controllers by default; register
    // each service assembly (and Platform, which owns AuthController) explicitly.
    .AddApplicationPart(typeof(OrderModule).Assembly)
    .AddApplicationPart(typeof(PaymentModule).Assembly)
    .AddApplicationPart(typeof(InventoryModule).Assembly)
    .AddApplicationPart(typeof(AuthController).Assembly);

builder.Services.AddOrderFlowSecurity(builder.Configuration);

// One shared bus + cache for the whole process.
builder.Services.AddInMemoryEventBus();
builder.Services.AddInMemoryCache();

// All three service modules register their saga handlers on that shared bus.
builder.Services.AddOrderModule();
builder.Services.AddPaymentModule();
builder.Services.AddInventoryModule();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "devhost" }));

app.Run();
