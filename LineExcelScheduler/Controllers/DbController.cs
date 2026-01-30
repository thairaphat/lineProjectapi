using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineExcelScheduler.Data;

namespace LineExcelScheduler.Controllers
{
    public class DbController : Controller
    {
        private readonly ApplicationDbContext _context;

        // ฉีด ApplicationDbContext เข้ามาใช้งานผ่าน Constructor
        public DbController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> TestConnection()
        {
            try 
            {
                // ดึงข้อมูลตัวอย่างจากทั้ง 2 ตารางที่มีอยู่แล้ว
                var amounts = await _context.fact_team_amounts.Take(5).ToListAsync();
                var mandays = await _context.fact_team_role_mandays.Take(5).ToListAsync();

                return Ok(new 
                { 
                    Message = "Backend connected to PostgreSQL successfully!",
                    AmountData = amounts,
                    MandayData = mandays
                });
            }
            catch (System.Exception ex)
            {
                // หากเชื่อมต่อไม่ได้จะแสดงสาเหตุที่ Error
                return BadRequest(new { Error = ex.Message });
            }
        }
    }
}