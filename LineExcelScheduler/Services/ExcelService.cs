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

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Line_oa"".""fact_team_role_mandays""");
                await _context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Line_oa"".""fact_team_amounts""");
                await _context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Line_oa"".""teams""");

                await _context.Database.ExecuteSqlRawAsync(@"ALTER SEQUENCE ""Line_oa"".""teams_id_seq"" RESTART WITH 1");
                await _context.Database.ExecuteSqlRawAsync(@"ALTER SEQUENCE ""Line_oa"".""fact_team_amounts_id_seq"" RESTART WITH 1");
                await _context.Database.ExecuteSqlRawAsync(@"ALTER SEQUENCE ""Line_oa"".""fact_team_role_mandays_id_seq"" RESTART WITH 1");

                _teamCache = new Dictionary<string, int>();

                int year = 2026;

                if (workbook.TryGetWorksheet("Table4", out var revSheet))
                {
                    foreach (var row in revSheet.RangeUsed().RowsUsed().Skip(1))
                    {
                        var companyCode = row.Cell(1).GetValue<string>().Trim();
                        var teamName = row.Cell(2).GetValue<string>().Trim();
                        var monthStr = row.Cell(3).GetValue<string>().Trim();
                        var targetStr = row.Cell(4).GetValue<string>();
                        var actualStr = row.Cell(5).GetValue<string>();

                        if (string.IsNullOrEmpty(monthStr) || string.IsNullOrEmpty(teamName)) continue;

                        int month = ConvertMonthToNumber(monthStr);
                        decimal targetVal = CleanDecimalValue(targetStr);
                        decimal actualVal = CleanDecimalValue(actualStr);

                        int teamId = await GetOrCreateTeamId(teamName, companyCode);

                        await InsertBothAmounts(teamId, year, month, targetVal, actualVal);
                    }
                }

                if (workbook.TryGetWorksheet("Table5", out var manSheet))
                {
                    foreach (var row in manSheet.RangeUsed().RowsUsed().Skip(1))
                    {
                        var companyCode = row.Cell(1).GetValue<string>().Trim();
                        var teamName = row.Cell(2).GetValue<string>().Trim();
                        var memberCountStr = row.Cell(3).GetValue<string>().Trim();
                        var role = row.Cell(4).GetValue<string>().Trim();
                        var monthStr = row.Cell(5).GetValue<string>().Trim();
                        var valStr = row.Cell(6).GetValue<string>();

                        if (string.IsNullOrEmpty(monthStr) || string.IsNullOrEmpty(teamName)) continue;

                        int month = ConvertMonthToNumber(monthStr);
                        decimal val = CleanDecimalValue(valStr);

                        int memberCount = 0;
                        if (int.TryParse(memberCountStr, out int mc))
                        {
                            memberCount = mc;
                        }

                        int teamId = await GetOrCreateTeamId(teamName, companyCode, memberCount);
                        await InsertManday(companyCode, teamId, year, month, role, val);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return "นำเข้าข้อมูลสำเร็จ! (ลบข้อมูลเก่าทั้งหมดและรีเซ็ต ID แล้ว)";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return $"ล้มเหลว: {ex.Message}";
            }
        }

        private async Task<int> GetOrCreateTeamId(string teamName, string companyCode)
        {
            return await GetOrCreateTeamId(teamName, companyCode, 0);
        }

        private async Task<int> GetOrCreateTeamId(string teamName, string companyCode, int memberCount)
        {
            if (string.IsNullOrWhiteSpace(teamName)) return 0;

            if (_teamCache!.TryGetValue(teamName, out int teamId))
            {
                if (memberCount > 0)
                {
                    await UpdateTeamMemberCount(teamId, memberCount);
                }
                return teamId;
            }

            int nextCodeNumber = await _context.teams.CountAsync() + 1;
            var team = new Team
            {
                team_name = teamName,
                team_code = nextCodeNumber.ToString(),
                company_code = companyCode,
                member_count = memberCount,  
                created_at = DateTime.UtcNow
            };
            _context.teams.Add(team);
            await _context.SaveChangesAsync();

            _teamCache[teamName] = team.id;
            return team.id;
        }

        private async Task UpdateTeamMemberCount(int teamId, int memberCount)
        {
            var sql = @"
                UPDATE ""Line_oa"".""teams"" 
                SET member_count = @mc 
                WHERE id = @id";

            await _context.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("@mc", memberCount),
                new NpgsqlParameter("@id", teamId));
        }

        private async Task InsertBothAmounts(
            int teamId,
            int year,
            int month,
            decimal? target,
            decimal? actual)
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
                new NpgsqlParameter("@target", (object?)target ?? DBNull.Value),
                new NpgsqlParameter("@actual", (object?)actual ?? DBNull.Value));
        }

        private async Task InsertManday(
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
                new NpgsqlParameter("@c", companyCode),
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