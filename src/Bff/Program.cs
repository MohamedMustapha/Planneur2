using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cracra.Bff;
using Cracra.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddCracraServiceDefaults("cracra-bff");

var keycloak = builder.Configuration.GetSection("Cracra:Keycloak");

// Keep the wire claim names. Mapping "sub" to a WS-Federation URI would silently break the API's claim contract.
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "cracra.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // A browser calling /api must get a 401 to react to, not a 302 into an HTML login page it cannot render.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = keycloak["Authority"];
        options.ClientId = keycloak["ClientId"] ?? "cracra-bff";
        options.ClientSecret = keycloak["ClientSecret"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;

        // The token never reaches the browser; it lives in the encrypted cookie and is attached server-side.
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.MapInboundClaims = false;

        // Keycloak's standard scopes only. Our claims ride on the client's own protocol mappers rather than a
        // custom scope, so there is nothing extra to request — see deploy/keycloak/build-realm.py.
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
    });

builder.Services.AddAuthorization();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
        context.AddRequestTransform(async transform =>
        {
            // Swap the session cookie for the bearer token Keycloak issued. This is the whole point of the BFF:
            // one hop where a browser credential becomes an API credential.
            var accessToken = await transform.HttpContext.GetTokenAsync("access_token");

            if (!string.IsNullOrEmpty(accessToken))
            {
                transform.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
            }
        }));

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapCracraDefaultEndpoints();
app.MapBffEndpoints();

// Every proxied API call must carry the anti-forgery header. Combined with SameSite=Strict this closes the CSRF
// hole the cookie-based session would otherwise open: a cross-site form post cannot set a custom header.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseMiddleware<AntiForgeryHeaderMiddleware>());

app.MapReverseProxy().RequireAuthorization();

// Anything else is an Angular route; hand it index.html and let the client router resolve it.
app.MapFallbackToFile("index.html");

await app.RunAsync();

public partial class Program;
