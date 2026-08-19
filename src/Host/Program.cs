using Cracra.BuildingBlocks.Ai;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Storage;
using Cracra.BuildingBlocks.Web;
using Cracra.ServiceDefaults;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.AddCracraServiceDefaults("cracra-api");

// --- Authentication ------------------------------------------------------------------------------------------
// The browser never sees a token: the BFF holds it and attaches it to the proxied call. This API therefore only
// ever validates a bearer token, and only one issued by our realm.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Cracra:Keycloak:Authority"];
        options.Audience = builder.Configuration["Cracra:Keycloak:Audience"] ?? "cracra-api";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;

        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
    });

// --- Cross-cutting -------------------------------------------------------------------------------------------
builder.Services.AddCracraWeb();
builder.Services.AddMediator();
builder.Services.AddCracraPersistence(builder.Configuration);
builder.Services.AddCracraStorage();
builder.Services.AddCracraAi();

builder.Services.AddFastEndpoints();

// --- Modules -------------------------------------------------------------------------------------------------
// S1 onwards each add themselves here with a single AddXxxModule() call. S0 registers only the platform's own
// schema, which exists to prove the migration, RLS and outbox machinery works before any module depends on it.
builder.Services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.SchemaName);

var app = builder.Build();

app.UseCracraWeb();

app.UseFastEndpoints(config =>
{
    config.Endpoints.RoutePrefix = "api";
    config.Errors.UseProblemDetails();
});

app.MapCracraDefaultEndpoints();

await app.RunAsync();

/// <summary>Named so <c>WebApplicationFactory&lt;Program&gt;</c> can find it from the integration tests.</summary>
public partial class Program;
