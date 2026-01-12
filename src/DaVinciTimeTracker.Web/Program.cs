using DaVinciTimeTracker.Core.Services;
using DaVinciTimeTracker.Core.Utilities;
using DaVinciTimeTracker.Data;
using DaVinciTimeTracker.Data.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Database
builder.Services.AddDbContext<TimeTrackerDbContext>(options =>
    options.UseSqlite(AppPaths.DatabaseConnectionString));

// Repositories and Services
builder.Services.AddScoped<SessionRepository>();
builder.Services.AddSingleton<StatisticsService>();

// Shared singletons will be injected from the App
builder.Services.AddSingleton<SessionManager>(sp =>
    throw new InvalidOperationException("SessionManager should be provided by the host app"));

// CORS for local access only
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
                  {
                      var uri = new Uri(origin);
                      return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                  })
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TimeTrackerDbContext>();
    db.Database.Migrate();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();
