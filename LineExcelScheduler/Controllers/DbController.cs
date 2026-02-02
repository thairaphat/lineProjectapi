using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineExcelScheduler.Data;
using LineExcelScheduler.Services;

namespace LineExcelScheduler.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DbController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly LineMessageService _lineMessageService;

        // แก้ไข Constructor ให้รับ LineMessageService เข้ามาตรงๆ
        public DbController(ApplicationDbContext context, LineMessageService lineMessageService)
        {
            _context = context;
            // ไม่ใช้คำสั่ง new แล้ว แต่รับมาจากระบบ Dependency Injection แทน
            _lineMessageService = lineMessageService;
        }

        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
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
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpGet("team-capacity-flex")]
        public async Task<IActionResult> GetTeamCapacityFlex(
     [FromQuery] string? keyword = null,
     [FromQuery] string? companyCode = null, // 👈 เพิ่มตัวแปรนี้
     [FromQuery] int skip = 0)               
        {
            try
            {
                // ส่ง keyword, companyCode (ถ้าเป็น null ให้ส่งค่าว่าง), และ skip
                var flexMessage = await _lineMessageService.CreateMessageDataAsync(
                    keyword ?? "",
                    companyCode ?? "",
                    skip
                );

                if (flexMessage == null)
                {
                    return NotFound(new { message = "ไม่พบข้อมูลสำหรับทีมหรือเงื่อนไขที่ระบุ" });
                }

                return Ok(flexMessage);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Error = ex.Message });
            }
        }
    }
}