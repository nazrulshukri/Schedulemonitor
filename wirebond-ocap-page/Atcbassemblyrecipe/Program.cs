using Atcbassemblyrecipe.Data;
using Atcbassemblyrecipe.Infrastructure;
using Atcbassemblyrecipe.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("LDAP login uses System.DirectoryServices and must run on Windows.");
}

// One timestamped line per log entry, so the terminal reads as a running trace
// of what the app is doing rather than a wall of two-line blocks.
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IOracleConnectionFactory, OracleConnectionFactory>();
// Table shapes are read from the data dictionary once per process and cached, so
// the schema provider is a singleton while everything that opens a connection
// stays scoped to the request.
builder.Services.AddSingleton<ITableSchemaProvider, TableSchemaProvider>();
builder.Services.AddScoped<IRecipeAuditRepository, RecipeAuditRepository>();
builder.Services.AddScoped<IAuditSchemaInstaller, AuditSchemaInstaller>();
builder.Services.AddScoped<IAccessRepository, AccessRepository>();
builder.Services.AddScoped<IAppSettingRepository, AppSettingRepository>();
// The UI text, media and design tokens come from TBLAPPSETTING through this
// provider, cached in the shared memory cache rather than per request.
builder.Services.AddScoped<IAppSettings, AppSettingsProvider>();
builder.Services.AddScoped<IAccessEvaluator, AccessEvaluator>();
builder.Services.AddScoped<IDatabaseChangeRequestRepository, DatabaseChangeRequestRepository>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IUserDirectory, UserDirectory>();
builder.Services.AddScoped<IAvatarStorage, AvatarStorage>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAwacsWstypeService, AwacsWstypeService>();
builder.Services.AddScoped<IAwacsLfService, AwacsLfService>();
builder.Services.AddScoped<IWireBondService, WireBondService>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Home/Privacy";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Point the SQL tracer at the app's logger factory before anything can run a
// statement - including the --init-db command below.
SqlTrace.Configure(app.Services.GetRequiredService<ILoggerFactory>());

// "dotnet run -- --init-db" creates TBLRECIPEHISTORY and TBLRECIPETRASH from
// Database/tblrecipeaudit.sql, as the same Oracle user the app connects as, then
// exits without starting the web host. Re-running it is safe.
if (args.Contains("--init-db", StringComparer.OrdinalIgnoreCase))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var installer = scope.ServiceProvider.GetRequiredService<IAuditSchemaInstaller>();

        try
        {
            foreach (var line in await installer.InstallAsync())
            {
                Console.WriteLine(line);
            }

            var installed = await installer.GetStatusAsync();
            Console.WriteLine(installed.IsComplete
                ? "Undo tables are ready. Edit and Delete will work now."
                : $"Still missing: {string.Join(", ", installed.MissingTables)}");

            Environment.ExitCode = installed.IsComplete ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"--init-db failed: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<RequestTraceMiddleware>();

// MapStaticAssets only serves assets known at BUILD time (from its manifest).
// Profile photos are written to wwwroot/uploads/avatars while the app is running,
// so they are not in that manifest - UseStaticFiles is what actually serves them.
app.UseStaticFiles();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// A start-up probe so the missing-tables problem shows up in the terminal at
// launch rather than as a popup the first time somebody presses Delete. It never
// stops the app: the database being unreachable at start-up is not fatal.
await using (var startupScope = app.Services.CreateAsyncScope())
{
    var logger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var status = await startupScope.ServiceProvider
            .GetRequiredService<IAuditSchemaInstaller>()
            .GetStatusAsync();

        if (status.IsComplete)
        {
            logger.LogInformation("Undo tables present: {History} and {Trash}.", "TBLRECIPEHISTORY", "TBLRECIPETRASH");
        }
        else
        {
            logger.LogWarning(
                "Undo tables missing: {Missing}. Edit and Delete will be refused until they exist. Run: dotnet run -- --init-db",
                string.Join(", ", status.MissingTables));
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not check the undo tables at start-up. The app will start anyway.");
    }

    // Same idea for the UI settings: say once, at launch, whether the pages are
    // being drawn from TBLAPPSETTING or from the built-in defaults.
    try
    {
        var settings = await startupScope.ServiceProvider
            .GetRequiredService<IAppSettings>()
            .GetAsync();

        if (settings.FromDatabase)
        {
            logger.LogInformation("UI settings loaded from {Table}.", "TBLAPPSETTING");
        }
        else
        {
            logger.LogWarning(
                "UI settings are the built-in defaults. Run Database/tblappsetting.sql to edit the branding, media and theme from the database.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not read the UI settings at start-up. The built-in defaults will be used.");
    }
}

app.Run();
