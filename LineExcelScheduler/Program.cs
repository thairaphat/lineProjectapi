
using Microsoft.EntityFrameworkCore;
using LineExcelScheduler.Data;
using LineExcelScheduler.Services;
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// 1. ดึง Connection String จากข้อ 1
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// 2. บอกให้ระบบใช้ Npgsql (PostgreSQL) สำหรับ .NET 9
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 3. ลงทะเบียน MVC และ Services อื่นๆ
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<ExcelService>();

var app = builder.Build();

// ... (Middleware อื่นๆ)
app.UseStaticFiles();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();