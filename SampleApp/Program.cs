using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

var entraSection = builder.Configuration.GetSection("EntraExternalId");
var ciamInstance = entraSection["Instance"]?.TrimEnd('/');
var ciamTenantId = entraSection["TenantId"];

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(options =>
    {
        entraSection.Bind(options);

        if (!string.IsNullOrWhiteSpace(ciamInstance) && !string.IsNullOrWhiteSpace(ciamTenantId))
        {
            var authority = $"{ciamInstance}/{ciamTenantId}/v2.0";
            options.Authority = authority;
            options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
        }

        options.TokenValidationParameters.RoleClaimType = "roles";
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

app.MapGet("/auth/signin", async (HttpContext context, string? returnUrl) =>
{
    var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = redirectUrl
    });
});

app.MapGet("/auth/signout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
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

    return Results.Ok(new
    {
        Name = user.Identity?.Name,
        Authenticated = user.Identity?.IsAuthenticated ?? false,
        Roles = roles,
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
