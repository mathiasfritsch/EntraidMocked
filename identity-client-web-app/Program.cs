using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Provider is selected by configuration only: "EntraId" (default, AzureAd section) or "Keycloak" (Identity section).
var provider = builder.Configuration["Identity:Provider"] ?? "EntraId";
string clientId;
string[] scopes;
Uri authorizationUrl;
Uri tokenUrl;

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

switch (provider)
{
    case "EntraId":
        var azureAd = builder.Configuration.GetSection("AzureAd");
        var tenantId = azureAd["TenantId"];
        clientId = azureAd["ClientId"]!;
        scopes = azureAd.GetSection("Scopes").Get<string[]>() ?? [];
        authBuilder.AddMicrosoftIdentityWebApi(azureAd);
        authorizationUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize");
        tokenUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token");
        break;

    case "Keycloak":
        var identity = builder.Configuration.GetSection("Identity");
        var authority = identity["Authority"]!.TrimEnd('/');
        clientId = identity["ClientId"]!;
        scopes = identity.GetSection("Scopes").Get<string[]>() ?? [];
        var validIssuers = identity.GetSection("ValidIssuers").Get<string[]>() ?? [authority];
        authBuilder.AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            options.TokenValidationParameters.ValidAudience = identity["Audience"];
            options.TokenValidationParameters.ValidIssuers = validIssuers;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
        });
        authorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth");
        tokenUrl = new Uri($"{authority}/protocol/openid-connect/token");
        break;

    default:
        throw new InvalidOperationException($"Unsupported Identity:Provider '{provider}'. Use 'EntraId' or 'Keycloak'.");
}

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).WithHeaders("Authorization", "Content-Type").AllowAnyMethod()));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["OAuth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = authorizationUrl,
                    TokenUrl = tokenUrl,
                    Scopes = scopes.ToDictionary(s => s, s => s)
                }
            }
        };
        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("OAuth2", document)] = [.. scopes]
            }
        ];
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Identity API")
        .AddPreferredSecuritySchemes("OAuth2")
        .AddAuthorizationCodeFlow("OAuth2", flow =>
        {
            flow.ClientId = clientId;
            flow.Pkce = Pkce.Sha256;
            flow.SelectedScopes = scopes;
        });
});

app.MapGet("/me", (HttpContext context) => new
{
    name = context.User.Identity?.Name,
    claims = context.User.Claims.Select(c => new { c.Type, c.Value })
})
.RequireAuthorization()
.WithName("GetMe");

app.Run();
