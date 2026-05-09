using System.Security.Claims;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure Key Vault for secrets management
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential(),
        new Azure.Extensions.AspNetCore.Configuration.Secrets.AzureKeyVaultConfigurationOptions
        {
            ReloadInterval = TimeSpan.FromHours(1)
        });
}

const string internalOidcScheme = "InternalOidc";
const string externalOidcScheme = "ExternalOidc";

var internalEntraSection = builder.Configuration.GetSection("EntraInternal");
var externalEntraSection = builder.Configuration.GetSection("EntraExternalId");
var internalInstance = internalEntraSection["Instance"]?.TrimEnd('/');
var internalTenantId = internalEntraSection["TenantId"];
var ciamInstance = externalEntraSection["Instance"]?.TrimEnd('/');
var ciamTenantId = externalEntraSection["TenantId"];

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = externalOidcScheme;
    });

authBuilder.AddMicrosoftIdentityWebApp(options =>
    {
        externalEntraSection.Bind(options);

        if (!string.IsNullOrWhiteSpace(ciamInstance) && !string.IsNullOrWhiteSpace(ciamTenantId))
        {
            var authority = $"{ciamInstance}/{ciamTenantId}/v2.0";
            options.Authority = authority;
            options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
        }

        options.TokenValidationParameters.RoleClaimType = "roles";
    },
    openIdConnectScheme: externalOidcScheme,
    cookieScheme: CookieAuthenticationDefaults.AuthenticationScheme);

authBuilder.AddOpenIdConnect(internalOidcScheme, options =>
{
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.ClientId = internalEntraSection["ClientId"];
    options.ClientSecret = internalEntraSection["ClientSecret"];
    options.CallbackPath = internalEntraSection["CallbackPath"] ?? "/signin-oidc-internal";
    options.SignedOutCallbackPath = internalEntraSection["SignedOutCallbackPath"] ?? "/signout-callback-oidc";
    options.ResponseType = "code";
    options.SaveTokens = true;

    if (!string.IsNullOrWhiteSpace(internalInstance) && !string.IsNullOrWhiteSpace(internalTenantId))
    {
        var internalAuthority = $"{internalInstance}/{internalTenantId}/v2.0";
        options.Authority = internalAuthority;
        options.MetadataAddress = $"{internalAuthority}/.well-known/openid-configuration";
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    // Add custom claim to identify internal authentication
    options.Events.OnTokenValidated = context =>
    {
        if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
        {
            identity.AddClaim(new System.Security.Claims.Claim("auth_scheme", "Internal"));
        }
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole("App.Admin"));

    options.AddPolicy("RequireReaderOrAdmin", policy =>
        policy.RequireRole("App.Reader", "App.Admin"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/auth/signin/internal", async (HttpContext context, string? returnUrl) =>
{
    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await context.ChallengeAsync(internalOidcScheme, new AuthenticationProperties
    {
        RedirectUri = redirectUrl
    });
});

app.MapGet("/auth/signin/external", async (HttpContext context, string? returnUrl) =>
{
    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await context.ChallengeAsync(externalOidcScheme, new AuthenticationProperties
    {
        RedirectUri = redirectUrl
    });
});

app.MapGet("/auth/signin", async (HttpContext context, string? returnUrl) =>
{
    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await context.ChallengeAsync(externalOidcScheme, new AuthenticationProperties
    {
        RedirectUri = redirectUrl
    });
});

app.MapGet("/auth/signout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Redirect("/");
});

app.MapGet("/api/info", () => new
{
    AppName = "SampleApp",
    Framework = ".NET 8",
    Host = Environment.MachineName,
    Environment = app.Environment.EnvironmentName,
    Time = DateTime.UtcNow
});

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var roles = user.Claims
        .Where(c => c.Type is "roles" or ClaimTypes.Role)
        .Select(c => c.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var authScheme = user.Claims.FirstOrDefault(c => c.Type == "auth_scheme")?.Value ?? "External";

    return Results.Ok(new
    {
        Name = user.Identity?.Name,
        Authenticated = user.Identity?.IsAuthenticated ?? false,
        Roles = roles,
        AuthScheme = authScheme,
        Claims = user.Claims.Select(c => new { c.Type, c.Value })
    });
}).RequireAuthorization();

app.MapGet("/api/admin", () => Results.Ok(new
{
    Message = "You have App.Admin role access."
})).RequireAuthorization("RequireAdminRole");

app.MapGet("/api/reader", () => Results.Ok(new
{
    Message = "You have App.Reader or App.Admin role access."
})).RequireAuthorization("RequireReaderOrAdmin");

app.MapGet("/api/weatherforecast", () =>
{
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
    var icons = new Dictionary<string, string>
    {
        ["Freezing"] = "\u2744\ufe0f", ["Bracing"] = "\ud83c\udf2c\ufe0f", ["Chilly"] = "\ud83e\udde5",
        ["Cool"] = "\ud83c\udf43", ["Mild"] = "\u26c5", ["Warm"] = "\u2600\ufe0f",
        ["Balmy"] = "\ud83c\udf3a", ["Hot"] = "\ud83d\udd25", ["Sweltering"] = "\ud83c\udf21\ufe0f", ["Scorching"] = "\u2668\ufe0f"
    };

    var forecast = Enumerable.Range(1, 7).Select(index =>
    {
        var summary = summaries[Random.Shared.Next(summaries.Length)];
        return new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summary,
            icons.GetValueOrDefault(summary, "\ud83c\udf24\ufe0f")
        );
    }).ToArray();

    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary, string Icon)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
