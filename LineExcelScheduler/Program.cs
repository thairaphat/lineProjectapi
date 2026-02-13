using DinkToPdf;
using DinkToPdf.Contracts;
using Hangfire;
using Hangfire.PostgreSql;
using LineExcelScheduler.Data;
using LineExcelScheduler.Services;
using Microsoft.EntityFrameworkCore;
using OneDriveFileAccess;
using System.Reflection;
using System.Runtime.Loader;
using DotNetEnv;
DotNetEnv.Env.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var host = Environment.GetEnvironmentVariable("DB_HOST");
var port = Environment.GetEnvironmentVariable("DB_PORT");
var db = Environment.GetEnvironmentVariable("DB_NAME");
var user = Environment.GetEnvironmentVariable("DB_USER");
var pass = Environment.GetEnvironmentVariable("DB_PASSWORD");


var context = new CustomAssemblyLoadContext();
var architectureFolder = (IntPtr.Size == 8) ? "64 bit" : "32 bit";
builder.Configuration.AddEnvironmentVariables();
var conn = $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
var wkHtmlToPdfPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "libwkhtmltox.dll");

var testToken = builder.Configuration["LINE_CHANNEL_ACCESS_TOKEN"];
Console.WriteLine($"Check Token: {(string.IsNullOrEmpty(testToken) ? "NOT FOUND" : "FOUND")}");
if (!File.Exists(wkHtmlToPdfPath))
{
    wkHtmlToPdfPath = Path.Combine(Directory.GetCurrentDirectory(), "libwkhtmltox.dll");
}

if (File.Exists(wkHtmlToPdfPath))
{
    context.LoadUnmanagedLibrary(wkHtmlToPdfPath);
    Console.WriteLine($" Loaded wkhtmltopdf from: {wkHtmlToPdfPath}");
}
else
{
    Console.WriteLine($" Warning: libwkhtmltox.dll not found. PDF generation will not work.");
    Console.WriteLine($"   Please download from: https://github.com/rdvojmoc/DinkToPdf/tree/master/v0.12.4/64%20bit");
    Console.WriteLine($"   And place it in: {Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")}");
}

builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
builder.Configuration["ConnectionStrings:DefaultConnection"] = conn;
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"DB Host: {host}:{port}");
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddScoped<LineMessageService>();
builder.Services.AddScoped<ExcelService>();
builder.Services.AddScoped<OneDriveGetFileService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(connectionString));
builder.Services.AddHangfireServer();

var app = builder.Build();

var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (!Directory.Exists(wwwrootPath))
{
    Directory.CreateDirectory(wwwrootPath);
    Console.WriteLine($"Created wwwroot folder at: {wwwrootPath}");
}

app.UseStaticFiles();
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    recurringJobManager.AddOrUpdate<LineMessageService>(
        "friday-all-report-broadcast",
        service => service.SendFridayBroadcastAsync(),
        "0 9 * * 5",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
    );

    recurringJobManager.AddOrUpdate<OneDriveGetFileService>(
        "daily-excel-sharepoint",
        service => service.GetKeySharepoint(),
        "0 * * * *",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
    );

}
app.UseHangfireDashboard("/hangfire");

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

internal class CustomAssemblyLoadContext : AssemblyLoadContext
{
    public IntPtr LoadUnmanagedLibrary(string absolutePath)
    {
        return LoadUnmanagedDll(absolutePath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return LoadUnmanagedDllFromPath(unmanagedDllName);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return null;
    }
}