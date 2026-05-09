using System.Security.Claims;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure Key Vault for secrets management (optional - falls back to environment variables)
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrWhiteSpace(keyVaultUrl))
{
    try
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultUrl),
            new DefaultAzureCredential(),
            new Azure.Extensions.AspNetCore.Configuration.Secrets.AzureKeyVaultConfigurationOptions
            {
                ReloadInterval = TimeSpan.FromHours(1)
            });
    }
    catch (Exception ex)
    {
        // Log the error and continue with environment variables
        Console.WriteLine($"Warning: Failed to load Key Vault secrets from {keyVaultUrl}: {ex.Message}");
        Console.WriteLine("Falling back to environment variables.");
    }
}

const string internalOidcScheme = "InternalOidc";
const string externalOidcScheme = "ExternalOidc";

string? GetSetting(params string[] keys)
{
    foreach (var key in keys)
    {
        var value = builder.Configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        // Ignore placeholder values from appsettings templates.
        if (value.Contains('<') || value.Contains('>'))
        {
            continue;
        }

        return value;
    }

    return null;
}

string NormalizeAuthPath(string? value, string fallback)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    var trimmed = value.Trim();
    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
    {
        trimmed = absoluteUri.AbsolutePath;
    }

    if (!trimmed.StartsWith('/'))
    {
        trimmed = "/" + trimmed;
    }

    return trimmed;
}

var internalInstance = GetSetting("EntraInternal:Instance", "AzureAdInternal:Instance")?.TrimEnd('/');
var internalTenantId = GetSetting("EntraInternal:TenantId", "AzureAdInternal:TenantId");
var internalClientId = GetSetting("EntraInternal:ClientId", "AzureAdInternal:ClientId");
var internalClientSecret = GetSetting("EntraInternal:ClientSecret", "AzureAdInternal:ClientSecret");
var internalCallbackPath = NormalizeAuthPath(GetSetting("EntraInternal:CallbackPath", "AzureAdInternal:CallbackPath"), "/signin-oidc-internal");
var internalSignedOutPath = NormalizeAuthPath(GetSetting("EntraInternal:SignedOutCallbackPath", "AzureAdInternal:SignedOutCallbackPath"), "/signout-callback-oidc");

var externalInstance = GetSetting("EntraExternalId:Instance", "AzureAdB2C:Instance", "AzureAd:Instance");
var externalDomain = GetSetting("EntraExternalId:Domain", "AzureAdB2C:Domain", "AzureAd:Domain");
var externalTenantId = GetSetting("EntraExternalId:TenantId", "AzureAdB2C:TenantId", "AzureAd:TenantId");
var externalClientId = GetSetting("EntraExternalId:ClientId", "AzureAdB2C:ClientId", "AzureAd:ClientId");
var externalClientSecret = GetSetting("EntraExternalId:ClientSecret", "AzureAdB2C:ClientSecret", "AzureAd:ClientSecret");
var externalPolicyId = GetSetting("EntraExternalId:SignUpSignInPolicyId", "AzureAdB2C:SignUpSignInPolicyId", "AzureAd:SignUpSignInPolicyId");
var externalCallbackPath = NormalizeAuthPath(GetSetting("EntraExternalId:CallbackPath", "AzureAdB2C:CallbackPath", "AzureAd:CallbackPath"), "/signin-oidc");
var externalSignedOutPath = NormalizeAuthPath(GetSetting("EntraExternalId:SignedOutCallbackPath", "AzureAdB2C:SignedOutCallbackPath", "AzureAd:SignedOutCallbackPath"), "/signout-callback-oidc");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = externalOidcScheme;
    });

authBuilder.AddMicrosoftIdentityWebApp(options =>
    {
        options.Instance = externalInstance;
        options.Domain = externalDomain;
        options.TenantId = externalTenantId;
        options.ClientId = externalClientId;
        options.ClientSecret = externalClientSecret;
        options.SignUpSignInPolicyId = externalPolicyId;
        options.CallbackPath = externalCallbackPath;
        options.SignedOutCallbackPath = externalSignedOutPath;
        options.TokenValidationParameters.RoleClaimType = "roles";
    },
    openIdConnectScheme: externalOidcScheme,
    cookieScheme: CookieAuthenticationDefaults.AuthenticationScheme);

authBuilder.AddOpenIdConnect(internalOidcScheme, options =>
{
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.ClientId = internalClientId;
    options.ClientSecret = internalClientSecret;
    options.CallbackPath = internalCallbackPath;
    options.SignedOutCallbackPath = internalSignedOutPath;
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
    if (string.IsNullOrWhiteSpace(internalClientId) || string.IsNullOrWhiteSpace(internalTenantId))
    {
        return Results.Problem(
            title: "Internal authentication is not configured",
            detail: "Set EntraInternal__ClientId and EntraInternal__TenantId in App Service settings.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    try
    {
        await context.ChallengeAsync(internalOidcScheme, new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        });
        return Results.Empty;
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Internal sign-in failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/auth/signin/external", async (HttpContext context, string? returnUrl) =>
{
    if (string.IsNullOrWhiteSpace(externalClientId))
    {
        return Results.Problem(
            title: "External authentication is not configured",
            detail: "Set EntraExternalId__ClientId in App Service settings.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    try
    {
        await context.ChallengeAsync(externalOidcScheme, new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        });
        return Results.Empty;
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "External sign-in failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
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
    try
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
    catch
    {
        // Best effort signout for local cookie only.
    }

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
