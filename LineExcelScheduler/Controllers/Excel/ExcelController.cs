using Microsoft.AspNetCore.Mvc;
using LineExcelScheduler.Services;

namespace LineExcelScheduler.Controllers.Excel
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExcelController : ControllerBase
    {
        private readonly ExcelService _excelService;

        public ExcelController(ExcelService excelService)
        {
            _excelService = excelService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("กรุณาเลือกไฟล์ Excel");

            try
            {
                using var stream = file.OpenReadStream();
                var result = await _excelService.ImportExcelToDb(stream);
                return Ok(new { Message = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
    }
}