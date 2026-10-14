using EcommerceWebApi;
using EcommerceWebApi.Authentication;
using EcommerceWebApi.Filters;
using EcommerceWebApi.Notification;
using EcommerceWebApi.Services;
using Serilog;
using Serilog.Events;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
ConfigurationManager configuration = builder.Configuration;

// cz-dotnet-1005: Configure Kestrel to use ASPNETCORE_HTTP_PORT / ASPNETCORE_HTTPS_PORT
// environment variables injected via Kubernetes ConfigMap for dynamic port assignment.
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    var httpPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORT"), out var p) ? p : 3000;
    serverOptions.ListenAnyIP(httpPort);
});

var logger = new LoggerConfiguration().MinimumLevel
    .Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.File(
        "Logs/log-.txt",
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(5),
        rollingInterval: RollingInterval.Day
    )
    .CreateLogger();
builder.Logging.AddSerilog(logger);

builder.Services.AddControllers();
builder.Services
    .AddControllers()
    .AddJsonOptions(x => x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks();

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("ApplicationSettings"));

// cz-dotnet-1004: Register IHttpContextAccessor for Redis-backed AuthService session state
builder.Services.AddHttpContextAccessor();

// cz-dotnet-1004: Register Redis-backed distributed cache (Amazon ElastiCache on EKS)
// to replace in-memory Blazor Server circuit state in AuthService, JwtService, and TotpService.
// REDIS_CONNECTION_STRING environment variable must be set to the ElastiCache endpoint.
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "EcommerceWebApi:";
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            // cz-dotnet-1005: Replace hardcoded SignalR CORS origin port with environment variable
            // SIGNALR_CORS_ORIGINS env var (comma-separated) injected via Kubernetes ConfigMap
            .WithOrigins(
                (Environment.GetEnvironmentVariable("SIGNALR_CORS_ORIGINS")
                    ?? builder.Configuration["SignalR:CorsOrigins"]
                    ?? "http://localhost:3001")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<UnitOfWork>();

builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<OrderService>();

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<TotpService>();
builder.Services.AddScoped<AuthorizeFilter>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddSignalR();

builder.Services.AddSingleton<NotificationSubject>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");
app.MapHealthChecks("/health");

app.Run();
