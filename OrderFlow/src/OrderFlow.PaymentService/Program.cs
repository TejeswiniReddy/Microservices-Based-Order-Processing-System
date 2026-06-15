using OrderFlow.PaymentService;
using OrderFlow.Platform.Caching;
using OrderFlow.Platform.Messaging;
using OrderFlow.Platform.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOrderFlowSecurity(builder.Configuration);

builder.Services.AddInMemoryEventBus();
builder.Services.AddInMemoryCache();
builder.Services.AddPaymentModule();

#if CLOUD
builder.Services.AddOrderFlowCloud(builder.Configuration);
#endif

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "payments" }));

app.Run();
