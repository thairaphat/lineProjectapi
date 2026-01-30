using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using LineExcelScheduler.Models;
using Microsoft.Extensions.Configuration;

namespace LineExcelScheduler.Services
{
    public class GroupedTeamData
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string MonthYear { get; set; } = string.Empty;
        public List<TeamDataRow> Roles { get; set; } = new List<TeamDataRow>();
    }

    public class TeamDataRow
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
        public decimal Manday { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public decimal TargetAmount { get; set; }
        public decimal ActualAmount { get; set; }
    }

    public class LineMessageService
    {
        private readonly string _connectionString;

        public LineMessageService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new Exception("Database connection string 'DefaultConnection' not found in appsettings.json");
        }

        // เพิ่มพารามิเตอร์ skip เพื่อรองรับการดูหน้าถัดไป
        public async Task<object?> CreateMessageDataAsync(string keyword, string companyCode, int skip = 0)
        {
            try
            {
                int year = 2026;
                int take = 12; // ดึงครั้งละ 10 รายการเพื่อป้องกัน Carousel เกิน 12 ใบ
                var teamData = await GetTeamDataFromDb(keyword, companyCode, year, skip, take);
                var dataList = teamData?.ToList() ?? new List<TeamDataRow>();

                if (!dataList.Any()) return null;

                var groupedData = dataList
                    .GroupBy(x => new { x.TeamId, x.Month, x.Year })
                    .Select(g => new GroupedTeamData
                    {
                        TeamId = g.Key.TeamId,
                        TeamName = g.First().TeamName,
                        MonthYear = $"{g.Key.Month}/{g.Key.Year}",
                        Roles = g.ToList()
                    }).ToList();

                string[] roleColors = { "#1E88E5", "#2E7D32", "#EF6C00", "#9C27B0", "#F57C00", "#5E35B1" };

                var carouselContents = groupedData.Select(group =>
                {
                    var firstRow = group.Roles.FirstOrDefault();
                    var totalTarget = firstRow?.TargetAmount ?? 0;
                    var totalActual = firstRow?.ActualAmount ?? 0;
                    var remaining = totalTarget - totalActual;
                    bool isOverTarget = totalActual > totalTarget;

                    var roleItems = group.Roles.Select((role, index) => (object)new
                    {
                        type = "box",
                        layout = "baseline",
                        contents = new object[] {
                            new { type = "text", text = role.RoleCode ?? "N/A", color = roleColors[index % roleColors.Length], flex = 3, size = "sm" },
                            new { type = "text", text = $"{role.Manday:N2} MDs", align = "end", weight = "bold", flex = 5, size = "sm" }
                        }
                    }).ToList();

                    return new
                    {
                        type = "bubble",
                        size = "mega",
                        header = new
                        {
                            type = "box",
                            layout = "vertical",
                            contents = new object[] {
                                new { type = "text", text = $"Team {group.TeamName}", weight = "bold", size = "xl", color = "#111111" },
                                new { type = "text", text = $"Period: {group.MonthYear}", size = "sm", color = "#666666" }
                            }
                        },
                        body = new
                        {
                            type = "box",
                            layout = "vertical",
                            spacing = "md",
                            contents = BuildFlexBody(roleItems, totalTarget, totalActual, remaining, isOverTarget).ToArray()
                        },
                        footer = new
                        {
                            type = "box",
                            layout = "vertical",
                            contents = new object[] {
                                new {
                                    type = "button",
                                    style = "primary",
                                    color = "#1E88E5",
                                    action = new { type = "uri", label = "View Full Report", uri = "https://your-dashboard-url.com" }
                                }
                            }
                        }
                    };
                }).Cast<object>().ToList();

                // ตรวจสอบว่ายังมีข้อมูลหน้าถัดไปหรือไม่
                bool hasMore = groupedData.Count >= take;

                return new
                {
                    messages = new[] {
                        new {
                            type = "flex",
                            altText = "Team Capacity Report",
                            contents = new {
                                type = "carousel",
                                contents = carouselContents
                            }
                        }
                    },
                    nextSkip = hasMore ? skip + take : (int?)null // ส่งตำแหน่งเริ่มต้นของหน้าถัดไปกลับไป
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] LineMessageService: {ex.Message}");
                return null;
            }
        }

        private async Task<IEnumerable<TeamDataRow>> GetTeamDataFromDb(
     string keyword,
     string companyCode,
     int year,
     int skip,
     int take)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            int? monthNum = GetMonthNumber(keyword);

            string sql;

            if (monthNum.HasValue)
            {
                // 🔵 กรณีเลือกเดือน: กรองตามเดือน และกรองบริษัท (ถ้ามีการระบุ)
                sql = @"
        WITH teams_page AS (
            SELECT DISTINCT rm.team_id
            FROM ""Line_projrct"".fact_team_role_mandays rm
            JOIN ""Line_projrct"".teams t ON t.id = rm.team_id
            WHERE rm.month = @monthNum
            AND rm.year = @year
            -- 👇 แก้ไข: ถ้า @companyCode เป็นค่าว่างให้ดึงทุกบริษัท ถ้ามีค่าให้กรองตามบริษัทนั้น
            AND (@companyCode = '' OR t.company_code = @companyCode)
            ORDER BY rm.team_id
            LIMIT @take OFFSET @skip
        )
        SELECT 
            t.id AS TeamId,
            t.team_name AS TeamName,
            rm.role_code AS RoleCode,
            rm.manday AS Manday,
            rm.month AS Month,
            rm.year AS Year,
            fa.target_amount AS TargetAmount,
            fa.actual_amount AS ActualAmount
        FROM teams_page tp
        JOIN ""Line_projrct"".fact_team_role_mandays rm ON tp.team_id = rm.team_id
        JOIN ""Line_projrct"".teams t ON t.id = rm.team_id
        LEFT JOIN ""Line_projrct"".fact_team_amounts fa 
            ON t.id = fa.team_id AND rm.year = fa.year AND rm.month = fa.month
        WHERE rm.month = @monthNum 
        AND rm.year = @year
        ORDER BY t.team_name, rm.role_code";
            }
            else
            {
                // 🟢 กรณีค้นหาชื่อทีม (เช่น 'หยก'): ค้นหาแบบ ILIKE และกรองบริษัท (ถ้ามีการระบุ)
                sql = @"
        SELECT 
            t.id AS TeamId, 
            t.team_name AS TeamName, 
            rm.role_code AS RoleCode, 
            rm.manday AS Manday, 
            rm.month AS Month, 
            rm.year AS Year,
            fa.target_amount AS TargetAmount,
            fa.actual_amount AS ActualAmount
        FROM ""Line_projrct"".teams t
        JOIN ""Line_projrct"".fact_team_role_mandays rm ON t.id = rm.team_id
        LEFT JOIN ""Line_projrct"".fact_team_amounts fa 
            ON t.id = fa.team_id AND rm.year = fa.year AND rm.month = fa.month
        WHERE (@companyCode = '' OR t.company_code = @companyCode) -- 👈 แก้ไขตรงนี้
        AND t.team_name ILIKE @kw
        AND rm.year = @year
        ORDER BY rm.month, rm.role_code";
            }

            return await conn.QueryAsync<TeamDataRow>(sql, new
            {
                kw = $"%{keyword}%",
                companyCode = companyCode ?? "", // ป้องกันค่า Null
                monthNum,
                year,
                take,
                skip
            });
        }


        private int? GetMonthNumber(string monthName)
        {
            var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                {"Jan", 1}, {"Feb", 2}, {"Mar", 3}, {"Apr", 4}, {"May", 5}, {"Jun", 6},
                {"Jul", 7}, {"Aug", 8}, {"Sep", 9}, {"Oct", 10}, {"Nov", 11}, {"Dec", 12}
            };
            return months.TryGetValue(monthName, out int month) ? month : (int?)null;
        }

        private List<object> BuildFlexBody(List<object> roleItems, decimal target, decimal actual, decimal remaining, bool isOver)
        {
            var contents = new List<object>();
            contents.AddRange(roleItems);
            contents.Add(new { type = "separator", margin = "lg" });
            contents.Add(new { type = "text", text = "SUMMARY", weight = "bold", size = "xs", color = "#aaaaaa", margin = "md" });

            contents.Add(CreateDataRow("Target Amount", target.ToString("N2"), "#1E88E5"));
            contents.Add(CreateDataRow("Actual Amount", actual.ToString("N2"), "#2E7D32"));
            contents.Add(new { type = "separator", margin = "md" });
            contents.Add(CreateDataRow("Remaining", Math.Abs(remaining).ToString("N2"), isOver ? "#2E7D32" : "#E64A19"));

            contents.Add(new
            {
                type = "box",
                layout = "vertical",
                margin = "md",
                contents = new object[] {
                    new {
                        type = "text",
                        text = isOver ? "▲ OVER TARGET" : "▼ UNDER TARGET",
                        color = isOver ? "#2E7D32" : "#C62828",
                        align = "center",
                        weight = "bold",
                        size = "sm"
                    }
                }
            });

            return contents;
        }

        private object CreateDataRow(string label, string value, string color) => new
        {
            type = "box",
            layout = "baseline",
            contents = new object[] {
                new { type = "text", text = label, color = "#666666", flex = 4, size = "sm" },
                new { type = "text", text = value, align = "end", weight = "bold", color = color, flex = 4, size = "sm" }
            }
        };

        public async Task<List<string>> GetCompanyListAsync()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            var sql = @"SELECT DISTINCT company_code FROM ""Line_projrct"".teams WHERE company_code IS NOT NULL ORDER BY company_code";
            var result = await conn.QueryAsync<string>(sql);
            return result.ToList();
        }
    }
}