var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/info", () => new
{
    AppName = "SampleApp",
    Framework = ".NET 8",
    Host = Environment.MachineName,
    Environment = app.Environment.EnvironmentName,
    Time = DateTime.UtcNow
});

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
