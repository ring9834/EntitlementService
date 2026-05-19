using EntitlementService.Data;
using EntitlementService.Services;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<INeo4jDataAccess, Neo4jDataAccess>();
builder.Services.AddScoped<IEntitlementCheckService, EntitlementCheckService>();
builder.Services.AddScoped<ISeedService, SeedService>();

builder.Services.AddOpenApi();

var app = builder.Build();

// Global exception handler — returns a consistent JSON error body instead of leaking stack traces or swallowing errors silently.
app.UseExceptionHandler(errorApp =>
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var feature = context.Features.Get<IExceptionHandlerFeature>();
        if (feature is not null)
        {
            // Avoid exposing exception details outside development.
            var message = app.Environment.IsDevelopment()
                ? feature.Error.ToString()
                : "An unexpected error occurred.";

            await context.Response.WriteAsJsonAsync(new { error = message });
        }
    }));

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
