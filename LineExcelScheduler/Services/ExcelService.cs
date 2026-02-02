using ClosedXML.Excel;
using LineExcelScheduler.Data;
using LineExcelScheduler.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LineExcelScheduler.Services
{
    public class ExcelService
    {
        private readonly ApplicationDbContext _context;
        private Dictionary<string, int>? _teamCache;

        public ExcelService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<string> ImportExcelToDb(Stream fileStream)
        {
            using var workbook = new XLWorkbook(fileStream);

            // 1. โหลดรายชื่อทีมทำ Cache เพื่อความเร็ว
            _teamCache = await _context.teams.ToDictionaryAsync(t => t.team_name.Trim(), t => t.id);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int year = 2026;

                // --- ส่วนที่ 1: จัดการแถบ "รายได้" (อ้างอิงโครงสร้าง: A:บริษัท, B:Team, C:Month, D:Target, E:Actual) ---
                if (workbook.TryGetWorksheet("รายได้", out var revSheet))
                {
                    foreach (var row in revSheet.RangeUsed().RowsUsed().Skip(1))
                    {
                        var companyCode = row.Cell(1).GetValue<string>().Trim();
                        var teamName = row.Cell(2).GetValue<string>().Trim();
                        var monthStr = row.Cell(3).GetValue<string>().Trim(); // คอลัมน์ C: เดือน
                        var targetStr = row.Cell(4).GetValue<string>();      // คอลัมน์ D: ยอดเป้าหมาย
                        var actualStr = row.Cell(5).GetValue<string>();      // คอลัมน์ E: ยอดจริง

                        if (string.IsNullOrEmpty(monthStr) || string.IsNullOrEmpty(teamName)) continue;

                        int month = ConvertMonthToNumber(monthStr);
                        decimal targetVal = CleanDecimalValue(targetStr);
                        decimal actualVal = CleanDecimalValue(actualStr);

                        int teamId = await GetOrCreateTeamId(teamName, companyCode);

                        // บันทึกทั้งสองยอดพร้อมกันในบรรทัดเดียว
                        await UpsertBothAmounts(teamId, year, month, targetVal, actualVal);
                    }
                }

                // --- ส่วนที่ 2: จัดการแถบ "manday" (อ้างอิงโครงสร้าง Unpivot: B:Team, C:Month, D:Value, E:Role) ---
                if (workbook.TryGetWorksheet("manday", out var manSheet))
                {
                    foreach (var row in manSheet.RangeUsed().RowsUsed().Skip(1))
                    {
                        // ปรับตำแหน่ง Cell ตามลำดับคอลัมน์จริงในไฟล์ Excel
                        // สมมติว่าไฟล์เป็นแบบ: A:Company, B:Team_ID, C:Role, D:Year, E:Month, F:Manday

                        var companyCode = row.Cell(1).GetValue<string>().Trim(); // A: SICM
                        var teamName = row.Cell(2).GetValue<string>().Trim(); // B: หยก
                        var headcountStr = row.Cell(3).GetValue<string>().Trim(); // C: 9
                        var role = row.Cell(4).GetValue<string>().Trim(); // D: PM
                        var monthStr = row.Cell(5).GetValue<string>().Trim(); // E: Jan
                        var valStr = row.Cell(6).GetValue<string>();        // F: 0 / 5

                        if (string.IsNullOrEmpty(monthStr) || string.IsNullOrEmpty(teamName)) continue;

                        int month = ConvertMonthToNumber(monthStr);
                        decimal val = CleanDecimalValue(valStr);

                        int teamId = await GetOrCreateTeamId(teamName, companyCode);
                        await UpsertManday(companyCode,teamId, year, month, role, val);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return "นำเข้าข้อมูลสำเร็จ!";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"ล้มเหลว: {ex.Message}";
            }
        }

        private async Task<int> GetOrCreateTeamId(string teamName, string companyCode)
        {
            if (string.IsNullOrWhiteSpace(teamName)) return 0;
            if (_teamCache!.TryGetValue(teamName, out int teamId)) return teamId;

            var team = await _context.teams.FirstOrDefaultAsync(t => t.team_name == teamName);
            if (team == null)
            {
                int nextCodeNumber = await _context.teams.CountAsync() + 1;
                team = new Team
                {
                    team_name = teamName,
                    team_code = nextCodeNumber.ToString(),
                    company_code = companyCode,
                    created_at = DateTime.UtcNow
                };
                _context.teams.Add(team);
                await _context.SaveChangesAsync();
            }
            _teamCache[teamName] = team.id;
            return team.id;
        }

        // เมธอดใหม่: บันทึกทั้ง Target และ Actual พร้อมกันเพื่อความแม่นยำ
        private async Task UpsertBothAmounts(int teamId, int year, int month, decimal target, decimal actual)
        {
            var sql = @"
                INSERT INTO ""Line_oa"".""fact_team_amounts"" 
                    (team_id, year, month, target_amount, actual_amount, created_at) 
                VALUES 
                    (@t, @y, @m, @target, @actual, CURRENT_TIMESTAMP) 
                ON CONFLICT (team_id, year, month) 
                DO UPDATE SET 
                    target_amount = EXCLUDED.target_amount,
                    actual_amount = EXCLUDED.actual_amount";

            await _context.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("@t", teamId),
                new NpgsqlParameter("@y", year),
                new NpgsqlParameter("@m", month),
                new NpgsqlParameter("@target", target),
                new NpgsqlParameter("@actual", actual));
        }

        private async Task UpsertManday(
    string companyCode,
    int teamId,
    int year,
    int month,
    string role,
    decimal val)
{
    var sql = @"
        INSERT INTO ""Line_oa"".""fact_team_role_mandays"" 
            (company_code, team_id, year, month, role_code, manday, created_at) 
        VALUES 
            (@c, @t, @y, @m, @r, @v, CURRENT_TIMESTAMP)
        ON CONFLICT (company_code, team_id, role_code, year, month) 
        DO UPDATE SET manday = EXCLUDED.manday";

    await _context.Database.ExecuteSqlRawAsync(sql,
        new NpgsqlParameter("@c", companyCode), // 👈 ตรงนี้
        new NpgsqlParameter("@t", teamId),
        new NpgsqlParameter("@y", year),
        new NpgsqlParameter("@m", month),
        new NpgsqlParameter("@r", role),
        new NpgsqlParameter("@v", val));
}

        private int ConvertMonthToNumber(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return 1;
            string monthPart = m.Trim().Length >= 3 ? m.Trim().Substring(0, 3).ToLower() : m.Trim().ToLower();

            return monthPart switch
            {
                "jan" => 1,
                "feb" => 2,
                "mar" => 3,
                "apr" => 4,
                "may" => 5,
                "jun" => 6,
                "jul" => 7,
                "aug" => 8,
                "sep" => 9,
                "oct" => 10,
                "nov" => 11,
                "dec" => 12,
                _ => 1
            };
        }

        private decimal CleanDecimalValue(string val)
        {
            if (string.IsNullOrWhiteSpace(val) || val == "-" || val == "a") return 0;
            return decimal.TryParse(val.Replace(",", ""), out var res) ? res : 0;
        }
    }
}