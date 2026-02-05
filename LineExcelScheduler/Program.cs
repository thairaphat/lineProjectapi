using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using LineExcelScheduler.Data;
using LineExcelScheduler.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

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
    "0 9 * * 5", // ✅ เปลี่ยนเป็นนาทีที่  09:00 น.
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Local }
);
}

// ✅ 6. เปิดหน้า Dashboard สำหรับตรวจสอบสถานะ (เข้าผ่าน /hangfire)
app.UseHangfireDashboard("/hangfire");

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();