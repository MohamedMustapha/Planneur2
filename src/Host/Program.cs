using Cracra.BuildingBlocks.Ai;
using Cracra.BuildingBlocks.Mediator;
using Cracra.BuildingBlocks.Persistence;
using Cracra.BuildingBlocks.Storage;
using Cracra.BuildingBlocks.Web;
using Cracra.BuildingBlocks.Web.Behaviors;
using Cracra.Host.DevSeed;
using Cracra.Modules.Access;
using Cracra.Modules.Directory;
using Cracra.Modules.Finance;
using Cracra.Modules.Problems;
using Cracra.Modules.Integrations;
using Cracra.Modules.Kudos;
using Cracra.Modules.Meetings;
using Cracra.Modules.Activities;
using Cracra.Modules.Portfolio;
using Cracra.Modules.Projects;
using Cracra.Modules.Reporting;
using Cracra.Modules.Scheduling;
using Cracra.Modules.Strategy;
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

// Pipeline order is registration order (conventions.md §1): Logging (from AddMediator) -> Validation -> Transaction
// -> Handler. Validation is registered centrally because it is request-shaped, not module-shaped; the transaction
// behavior registers itself per module, because only the module knows which unit of work is its own.
builder.Services.AddMediatorBehavior(typeof(ValidationBehavior<,>));
builder.Services.AddCracraPersistence(builder.Configuration);
builder.Services.AddCracraStorage();
builder.Services.AddCracraAi();

builder.Services.AddFastEndpoints();

// --- Modules -------------------------------------------------------------------------------------------------
// One AddXxxModule() call each; the host knows nothing about a module beyond this line.
builder.Services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.SchemaName);
builder.Services.AddAccessModule();
builder.Services.AddDirectoryModule();
builder.Services.AddProjectsModule();
builder.Services.AddPortfolioModule();
builder.Services.AddActivitiesModule();
builder.Services.AddSchedulingModule();
builder.Services.AddMeetingsModule();
builder.Services.AddKudosModule();
builder.Services.AddReportingModule();
builder.Services.AddIntegrationsModule();
builder.Services.AddFinanceModule();
builder.Services.AddProblemsModule();
builder.Services.AddStrategyModule();

// --- Development data ----------------------------------------------------------------------------------------
// Two projects and a few weeks of activity, so a fresh dev box opens on populated boards rather than on empty
// ones. Behind an environment check rather than only a configuration flag: this writes invented projects, and a
// stray key in the wrong appsettings must not be enough to do that anywhere but here.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCracraDevSeed(builder.Configuration);
}

var app = builder.Build();

// The access module goes inside the seam, not after it: it replaces the token's roles with the effective ones,
// and both the authorization gate and the RLS interceptor must see that answer rather than the raw token.
app.UseCracraWeb(access => access.UseAccessModule());

app.UseFastEndpoints(config =>
{
    config.Endpoints.RoutePrefix = "api";
    config.Errors.UseProblemDetails();
});

app.MapCracraDefaultEndpoints();

await app.RunAsync();

/// <summary>Named so <c>WebApplicationFactory&lt;Program&gt;</c> can find it from the integration tests.</summary>
public partial class Program;
