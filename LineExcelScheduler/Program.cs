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
using System.Runtime.InteropServices; // เพิ่มสำหรับเช็ค OS
using DotNetEnv;

DotNetEnv.Env.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(connectionString)
);

var context = new CustomAssemblyLoadContext();
bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
string libName = isWindows ? "libwkhtmltox.dll" : "libwkhtmltox.so";

// ใช้ Path เต็มเสมอ
string wkHtmlToPdfPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", libName);

Console.WriteLine($"🔍 Final Library Search Path: {wkHtmlToPdfPath}");

if (File.Exists(wkHtmlToPdfPath))
{
    try 
    {
        context.LoadUnmanagedLibrary(wkHtmlToPdfPath);
        Console.WriteLine($" ✅ Native Library Loaded Successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($" ❌ Load Error: {ex.Message}");
    }
}
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddScoped<LineMessageService>();
builder.Services.AddScoped<ExcelService>();
builder.Services.AddScoped<OneDriveGetFileService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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