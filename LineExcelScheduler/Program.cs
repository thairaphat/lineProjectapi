using DinkToPdf;
using DinkToPdf.Contracts;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Runtime.Loader;
using LineExcelScheduler.Services;
using LineExcelScheduler.Data;


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ⭐ เพิ่มส่วนนี้ - Load wkhtmltopdf library
var context = new CustomAssemblyLoadContext();
var architectureFolder = (IntPtr.Size == 8) ? "64 bit" : "32 bit";

// ลองหาไฟล์ใน wwwroot ก่อน ถ้าไม่มีค่อยหาที่ root
var wkHtmlToPdfPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "libwkhtmltox.dll");
if (!File.Exists(wkHtmlToPdfPath))
{
    wkHtmlToPdfPath = Path.Combine(Directory.GetCurrentDirectory(), "libwkhtmltox.dll");
}

// ถ้ายังไม่เจอ ให้แสดง warning
if (File.Exists(wkHtmlToPdfPath))
{
    context.LoadUnmanagedLibrary(wkHtmlToPdfPath);
    Console.WriteLine($"✅ Loaded wkhtmltopdf from: {wkHtmlToPdfPath}");
}
else
{
    Console.WriteLine($"⚠️ Warning: libwkhtmltox.dll not found. PDF generation will not work.");
    Console.WriteLine($"   Please download from: https://github.com/rdvojmoc/DinkToPdf/tree/master/v0.12.4/64%20bit");
    Console.WriteLine($"   And place it in: {Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")}");
}

// ⭐ Register DinkToPdf service
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));

// 1. ดึง Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// 2. ลงทะเบียน Services (ต้องทำก่อน builder.Build())
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient(); // ✅ จำเป็นสำหรับ LineMessageService
builder.Services.AddScoped<LineMessageService>();
builder.Services.AddScoped<ExcelService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// ✅ 3. ลงทะเบียน Hangfire เพื่อป้องกัน Error "No service for type IRecurringJobManager"
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(connectionString));
builder.Services.AddHangfireServer();

var app = builder.Build();

// ⭐ สร้าง wwwroot ถ้ายังไม่มี
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (!Directory.Exists(wwwrootPath))
{
    Directory.CreateDirectory(wwwrootPath);
    Console.WriteLine($"✅ Created wwwroot folder at: {wwwrootPath}");
}

// 4. ตั้งค่า Middleware
app.UseStaticFiles();

// ✅ 5. ตั้งค่าการรันงานอัตโนมัติ (Recurring Job)
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    // ตั้งค่าส่งรายงาน "all" ทุกวันศุกร์ เวลา 09:00 น.
    // ระบบจะคำนวณ Actual - Target และแสดงสีแดงเมื่อติดลบโดยอัตโนมัติ
    recurringJobManager.AddOrUpdate<LineMessageService>(
        "friday-all-report-broadcast",
        service => service.SendFridayBroadcastAsync(),
        "0 9 * * 5", // ทุกวันศุกร์ เวลา 09:00 น.
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
    );

    // ตั้งค่าสำหรับ ExcelImportJob ทุกวัน เวลา 00:00 น.
    //recurringJobManager.AddOrUpdate<ExcelImportJob>(
    //    "daily-excel-import",
    //    job => job.RunDailyImportAsync(),
    //    "0 0 * * *", // ทุกวัน เวลา 00:00 น.
    //    new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
    //);

    //recurringJobManager.Trigger("daily-excel-import"); // รันได้ทันทีเมื่อเปิดบรรทัดนี้แล้วรันใหม่
}

// ✅ 6. เปิดหน้า Dashboard สำหรับตรวจสอบสถานะ (เข้าผ่าน /hangfire)
app.UseHangfireDashboard("/hangfire");

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ⭐ เพิ่ม Class นี้สำหรับ load wkhtmltopdf library
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