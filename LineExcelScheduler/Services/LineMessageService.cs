using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using LineExcelScheduler.Models;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Net.Http.Headers;
using Newtonsoft.Json;
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
        public int Quarter => ((Month - 1) / 3) + 1;
    }

    public class LineMessageService
    {
        private readonly string _connectionString;

        private readonly string _channelAccessToken = "BQ9QdG9ty3xemX7fl/4JM1MQIK9BwzC9Y9+7riLmCwvpJPE5/+uAyJ7kE5Eif4aySPAcFqotjDaxLl4+I+VVaHRL6PR0hpAOvrTfQgJbaWF2ZITqUf0p8/mrseb49uu3Ne04mWennnml3naZjOCkigdB04t89/1O/w1cDnyilFU=";
        private static readonly HttpClient _httpClient = new HttpClient();
        public LineMessageService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new Exception("Database connection string 'DefaultConnection' not found in appsettings.json");
        }

        public async Task<object?> CreateMessageDataAsync(string keyword, string companyCode, int skip = 0)
        {
            try
            {
                int year = 2026;
                int take = 12;
                var teamData = await GetTeamDataFromDb(keyword, companyCode, year, skip, take);
                var dataList = teamData?.ToList() ?? new List<TeamDataRow>();
                if (!dataList.Any()) return null;

                bool isMonthSearch = GetMonthNumber(keyword).HasValue;

                var groupedData = dataList
                .GroupBy(x => isMonthSearch
                    ? new { x.TeamId, Period = x.Month.ToString(), x.Year }
                    : new { x.TeamId, Period = $"Q{x.Quarter}", x.Year }) // ตรงนี้จะรวมเดือน 1,2,3 เข้ามาใน Key เดียวกัน (เช่น Q1)
                .Select(g => new GroupedTeamData
                {
                    TeamId = g.Key.TeamId,
                    TeamName = g.First().TeamName,
                    MonthYear = isMonthSearch ? $"{keyword}/{g.Key.Year}" : $"{g.Key.Period}/{g.Key.Year}",
                    Roles = g
                        .GroupBy(r => r.RoleCode)
                        .Select(rg => new TeamDataRow
                        {
                            RoleCode = rg.Key,
                            Manday = rg.Sum(x => x.Manday), // รวม Manday ของทั้ง 3 เดือน
                            // ✅ แก้ไข: Target/Actual ของ "ไตรมาส" ต้องเกิดจาก "ผลรวมของทั้ง 3 เดือน"
                            // เราต้อง Group ตามเดือนก่อนเพื่อเอาค่า Max ของแต่ละเดือน แล้วค่อย Sum 3 เดือนเข้าด้วยกัน
                            TargetAmount = rg.GroupBy(m => m.Month).Sum(m => m.Max(x => x.TargetAmount)),
                            ActualAmount = rg.GroupBy(m => m.Month).Sum(m => m.Max(x => x.ActualAmount))
                        }).ToList()
                }).ToList();

                string[] roleColors = { "#1E88E5", "#2E7D32", "#EF6C00", "#9C27B0", "#F57C00", "#5E35B1" };

                var carouselContents = groupedData.Select(group =>
                {
                    string teamNameKeyword = Uri.EscapeDataString(group.TeamName);
                    decimal totalTarget = 0;
                    decimal totalActual = 0;

                    string reportUrl = $"https://774a-183-88-236-116.ngrok-free.app/api/excel/generate-pdf?teamNameKeyword={teamNameKeyword}";

                    if (isMonthSearch)
                    {
                        // ถ้ารายเดือน: ทุก Role ในทีมเดือนนั้นจะมีค่าเท่ากัน ใช้ Max ตัวเดียวจบ
                        totalTarget = group.Roles.Max(x => x.TargetAmount);
                        totalActual = group.Roles.Max(x => x.ActualAmount);
                    }
                    else
                    {
                        // ถ้ารายไตรมาส: ต้องเอายอดสูงสุดของแต่ละเดือนมาบวกกัน (เพราะเป้าไตรมาส = เป้าเดือน 1+2+3)
                        // เนื่องจากโครงสร้าง GroupedTeamData ของคุณ Roles เก็บยอดที่ Sum มาจาก SQL แล้ว 
                        // หาก SQL คืนค่ามาถูกต้อง (Target ต่อเดือนต่อทีม) ให้ดึงค่าจาก Role แรกมาตัวเดียวเพื่อป้องกันการบวกซ้ำราย Role
                        totalTarget = group.Roles.Max(x => x.TargetAmount);
                        totalActual = group.Roles.Max(x => x.ActualAmount);
                    }

                    var statusValue = totalActual - totalTarget;

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
                                                new { type = "text", text = $"ช่วงเวลา: {group.MonthYear}", size = "sm", color = "#666666" }
                                            }
                        },
                        body = new
                        {
                            type = "box",
                            layout = "vertical",
                            spacing = "md",
                            contents = BuildFlexBody(roleItems, totalTarget, totalActual, statusValue, totalActual > totalTarget).ToArray()
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
                                                    action = new { type = "uri", label = "View Full Report", uri = reportUrl }
                                                }
                                            }
                        }
                    };
                }).Cast<object>().ToList();

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
                    nextSkip = groupedData.Count >= take ? skip + take : (int?)null
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] LineMessageService: {ex.Message}");
                return null;
            }
        }

        public async Task<object?> CreateTotalSummaryMessageAsync(string companyCode)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                int year = 2026;
                bool isYearlyAll = string.IsNullOrEmpty(companyCode);

                // SQL ดึงข้อมูลพื้นฐาน (เหมือนเดิม)
                string sql = @"
                WITH amount_per_month AS (
                    SELECT
                        team_id,
                        year,
                        month,
                        MAX(target_amount) AS target_amount,
                        MAX(actual_amount) AS actual_amount
                    FROM ""Line_oa"".fact_team_amounts
                    WHERE year = @year
                    GROUP BY team_id, year, month
                )
                SELECT 
                    ((rm.month - 1) / 3) + 1 AS Quarter,
                    rm.role_code AS RoleCode,
                    rm.month AS Month,
                    SUM(rm.manday) AS TotalManday,
                    SUM(apm.target_amount) AS TotalTarget,
                    SUM(apm.actual_amount) AS TotalActual
                FROM ""Line_oa"".fact_team_role_mandays rm
                JOIN ""Line_oa"".teams t ON t.id = rm.team_id
                LEFT JOIN amount_per_month apm
                    ON rm.team_id = apm.team_id
                AND rm.year = apm.year
                AND rm.month = apm.month
                WHERE rm.year = @year
                AND (@companyCode = '' OR t.company_code = @companyCode)
                GROUP BY Quarter, rm.role_code, rm.month
                ORDER BY Quarter, rm.role_code";

                var rawData = await conn.QueryAsync(sql, new { year, companyCode });
                var dataList = rawData.ToList();
                if (!dataList.Any()) return null;

                var bodyContents = new List<object>();
                string[] roleColors = { "#1E88E5", "#2E7D32", "#EF6C00", "#9C27B0", "#F57C00", "#5E35B1" };
                if (isYearlyAll)
                {
                    // ==========================================
                    // CASE: สรุปยอดรวมทั้งปี (สำหรับปุ่ม All)
                    // ==========================================
                    var roleGroups = dataList.GroupBy(x => x.rolecode)
                        .Select(g => new { RoleCode = g.Key, TotalMD = g.Sum(x => (decimal)x.totalmanday) });

                    int idx = 0;
                    var roleBoxContents = new List<object>();
                    foreach (var role in roleGroups)
                    {
                        roleBoxContents.Add(new
                        {
                            type = "box",
                            layout = "baseline",
                            margin = "xs",
                            contents = new object[] {
            // แก้ตรงนี้: ใส่ color ตามลำดับ และปรับ size เป็น sm ให้เหมือนหน้าแรก
            new { type = "text", text = (string)role.RoleCode, color = roleColors[idx % roleColors.Length], weight = "bold", size = "sm", flex = 3 },
            new { type = "text", text = $"{role.TotalMD:N2} MDs", align = "end", weight = "bold", size = "sm", flex = 5 }
        }
                        });
                        idx++; // นับลำดับสีต่อไป
                    }
                    bodyContents.Add(new { type = "box", layout = "vertical", margin = "md", paddingAll = "md", backgroundColor = "#F8F9FA", cornerRadius = "md", contents = roleBoxContents.ToArray() });
                }
                else
                {
                    // ==========================================
                    // CASE: แยกตามไตรมาส (สำหรับเลือกบริษัท)
                    // ==========================================
                    var quarterGroups = dataList.GroupBy(x => x.quarter);
                    foreach (var group in quarterGroups)
                    {
                        var itemsInQuarter = group.ToList();
                        // คำนวณยอดเงินรายไตรมาส (ใช้ Max ต่อเดือนเพื่อความแม่นยำ)
                        var qAmounts = itemsInQuarter.GroupBy(x => x.month)
                            .Select(mg => new { T = mg.Max(x => (decimal)(x.totaltarget ?? 0)), A = mg.Max(x => (decimal)(x.totalactual ?? 0)) });

                        decimal qTarget = qAmounts.Sum(x => x.T);
                        decimal qActual = qAmounts.Sum(x => x.A);
                        decimal qStatus = qActual - qTarget;

                        var quarterBoxContents = new List<object> {
                    new { type = "text", text = $"Quarter {group.Key}", weight = "bold", size = "md", color = "#1E88E5" }
                };

                        // แสดง Mandays แยกตาม Role ในไตรมาสนั้น
                        var roleInQ = itemsInQuarter.GroupBy(x => x.rolecode)
                            .Select(rg => new { Role = rg.Key, MD = rg.Sum(x => (decimal)x.totalmanday) });

                        int qIdx = 0;
                        foreach (var role in roleInQ)
                        {
                            quarterBoxContents.Add(new
                            {
                                type = "box",
                                layout = "baseline",
                                margin = "xs",
                                contents = new object[] {
            // แก้ตรงนี้: ใส่ color และปรับ size เป็น sm เหมือนกัน
            new { type = "text", text = (string)role.Role, color = roleColors[qIdx % roleColors.Length], weight = "bold", size = "sm", flex = 3 },
            new { type = "text", text = $"{role.MD:N2} MDs", align = "end", weight = "bold", size = "sm", flex = 5 }
        }
                            });
                            qIdx++; // นับลำดับสีต่อไป
                        }

                        quarterBoxContents.Add(new { type = "separator", margin = "sm" });
                        quarterBoxContents.Add(new
                        {
                            type = "box",
                            layout = "vertical",
                            margin = "sm",
                            spacing = "xs",
                            contents = new object[] {
                        new { type = "box", layout = "baseline", contents = new object[] {
                            new { type = "text", text = "Q-Target", size = "xs", color = "#aaaaaa", flex = 3 },
                            new { type = "text", text = qTarget.ToString("N2"), align = "end", size = "xs", weight = "bold", flex = 5, color = "#1E88E5" }
                        }},
                        new { type = "box", layout = "baseline", contents = new object[] {
                            new { type = "text", text = "Q-Actual", size = "xs", color = "#aaaaaa", flex = 3 },
                            new { type = "text", text = qActual.ToString("N2"), align = "end", size = "xs", weight = "bold", flex = 5, color = "#2E7D32" }
                        }},
                        new { type = "box", layout = "baseline", contents = new object[] {
                            new { type = "text", text = "Q-Status", size = "xs", color = "#aaaaaa", flex = 3 },
                            new { type = "text", text = qStatus.ToString("N2"), align = "end", size = "xs", weight = "bold", flex = 5, color = qStatus < 0 ? "#FF0000" : "#2E7D32" }
                        }}
                    }
                        });

                        bodyContents.Add(new { type = "box", layout = "vertical", margin = "md", paddingAll = "md", backgroundColor = "#F8F9FA", cornerRadius = "md", contents = quarterBoxContents.ToArray() });
                    }
                }

                // --- ส่วนท้าย: GRAND TOTAL (แสดงเหมือนกันทั้งสองกรณี) ---
                bodyContents.Add(new { type = "separator", margin = "xl" });
                bodyContents.Add(new { type = "text", text = "GRAND TOTAL (YEARLY)", weight = "bold", size = "xs", color = "#aaaaaa", margin = "md" });

                var yearlySummaryData = dataList
                .GroupBy(x => new { x.teamid, x.month })
                .Select(g => new
                {
                    T = g.Max(x => (decimal)(x.totaltarget ?? 0)),
                    A = g.Max(x => (decimal)(x.totalactual ?? 0))
                })
                .ToList();

                decimal totalT = yearlySummaryData.Sum(x => x.T);
                decimal totalA = yearlySummaryData.Sum(x => x.A);
                decimal totalStatus = totalA - totalT;

                bodyContents.Add(CreateDataRow("Target Amount", totalT.ToString("N2"), "#1E88E5"));
                bodyContents.Add(CreateDataRow("Actual Amount", totalA.ToString("N2"), "#2E7D32"));
                bodyContents.Add(new { type = "separator", margin = "sm" });
                bodyContents.Add(CreateDataRow("Overall Status", totalStatus.ToString("N2"), totalStatus < 0 ? "#FF0000" : "#2E7D32"));

                string reportUrl = $"https://774a-183-88-236-116.ngrok-free.app/api/excel/generate-pdf?companyCodeKeyword={companyCode}";

                var summaryBubble = new
                {
                    type = "bubble",
                    size = "mega",
                    header = new
                    {
                        type = "box",
                        layout = "vertical",
                        contents = new object[] {
                new { type = "text", text = isYearlyAll ? "Yearly Summary" : $"Company: {companyCode}", weight = "bold", size = "xl" },
                new { type = "text", text = $"ปีงบประมาณ: {year}", size = "sm", color = "#666666" }
            }
                    },
                    body = new { type = "box", layout = "vertical", spacing = "sm", contents = bodyContents.ToArray() },
                    footer = new
                    {
                        type = "box",
                        layout = "vertical",
                        contents = new object[] {
                new { type = "button", style = "primary", color = "#1E88E5", action = new { type = "uri", label = "View Full Report",uri = reportUrl} } 
            }
                    }
                };

                return new { messages = new[] { new { type = "flex", altText = "สรุปภาพรวมรายปี", contents = summaryBubble } }, nextSkip = (int?)null };
            }
            catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); return null; }
        }

        private async Task<IEnumerable<TeamDataRow>> GetTeamDataFromDb(string keyword, string companyCode, int year, int skip, int take)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            int? monthNum = GetMonthNumber(keyword);
            string sql;

            if (monthNum.HasValue)
            {
                sql = @"WITH teams_page AS (SELECT DISTINCT rm.team_id FROM ""Line_oa"".fact_team_role_mandays rm JOIN ""Line_oa"".teams t ON t.id = rm.team_id WHERE rm.month = @monthNum AND rm.year = @year AND (@companyCode = '' OR t.company_code = @companyCode) ORDER BY rm.team_id LIMIT @take OFFSET @skip)
                        SELECT t.id AS TeamId, t.team_name AS TeamName, rm.role_code AS RoleCode, rm.manday AS Manday, rm.month AS Month, rm.year AS Year, fa.target_amount AS TargetAmount, fa.actual_amount AS ActualAmount
                        FROM teams_page tp JOIN ""Line_oa"".fact_team_role_mandays rm ON tp.team_id = rm.team_id JOIN ""Line_oa"".teams t ON t.id = rm.team_id LEFT JOIN ""Line_oa"".fact_team_amounts fa ON t.id = fa.team_id AND rm.year = fa.year AND rm.month = fa.month
                        WHERE rm.month = @monthNum AND rm.year = @year ORDER BY t.team_name, rm.role_code";
            }
            else
            {
                sql = @"SELECT t.id AS TeamId, t.team_name AS TeamName, rm.role_code AS RoleCode, rm.manday AS Manday, rm.month AS Month, rm.year AS Year, fa.target_amount AS TargetAmount, fa.actual_amount AS ActualAmount
                        FROM ""Line_oa"".teams t JOIN ""Line_oa"".fact_team_role_mandays rm ON t.id = rm.team_id LEFT JOIN ""Line_oa"".fact_team_amounts fa ON t.id = fa.team_id AND rm.year = fa.year AND rm.month = fa.month
                        WHERE (@companyCode = '' OR t.company_code = @companyCode) AND t.team_name = @kw AND rm.year = @year ORDER BY rm.month, rm.role_code";
            }
            return await conn.QueryAsync<TeamDataRow>(sql, new { kw = keyword, companyCode, monthNum, year, take, skip });
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


            string statusColor = remaining < 0 ? "#FF0000" : "#2E7D32";
            contents.Add(CreateDataRow("Status", remaining.ToString("N2"), statusColor));

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

        private int? GetMonthNumber(string monthName)
        {
            var months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "Jan", 1 }, { "Feb", 2 }, { "Mar", 3 }, { "Apr", 4 }, { "May", 5 }, { "Jun", 6 }, { "Jul", 7 }, { "Aug", 8 }, { "Sep", 9 }, { "Oct", 10 }, { "Nov", 11 }, { "Dec", 12 } };
            return months.TryGetValue(monthName, out int month) ? month : (int?)null;
        }

        public async Task<List<string>> GetCompanyListAsync()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            return (await conn.QueryAsync<string>(@"SELECT DISTINCT company_code FROM ""Line_oa"".teams WHERE company_code IS NOT NULL ORDER BY company_code")).ToList();
        }

        public async Task<List<string>> GetTeamsByCompanyAsync(string companyCode)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            return (await conn.QueryAsync<string>(@"SELECT DISTINCT team_name FROM ""Line_oa"".teams WHERE company_code = @companyCode ORDER BY team_name", new { companyCode })).ToList();
        }

        public async Task SaveLineRecipientAsync(string userId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            // ใช้ ON CONFLICT เพื่อป้องกันการบันทึกซ้ำ
            var sql = @"INSERT INTO ""Line_oa"".line_recipients (line_user_id) 
                VALUES (@userId) 
                ON CONFLICT (line_user_id) DO NOTHING";
            await conn.ExecuteAsync(sql, new { userId });
        }

        public async Task<List<string>> GetAllActiveRecipientsAsync()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            var sql = @"SELECT line_user_id FROM ""Line_oa"".line_recipients WHERE active = true";
            var result = await conn.QueryAsync<string>(sql);
            return result.ToList();
        }

        private async Task PushMessageToLineAsync(string toUserId, object flexData)
        {
            try
            {
                var flexMessages = ((dynamic)flexData).messages;
                var payload = new { to = toUserId, messages = flexMessages };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _channelAccessToken);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Push Error] To: {toUserId} Status: {response.StatusCode} Error: {errorBody}");

                    // ✅ หากสถานะเป็น 400 (Bad Request) มักหมายถึง User บล็อกบอท หรือ UserId ไม่ถูกต้อง
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        using var conn = new NpgsqlConnection(_connectionString);
                        await conn.ExecuteAsync(@"UPDATE ""Line_oa"".line_recipients SET active = false WHERE line_user_id = @toUserId", new { toUserId });
                        Console.WriteLine($"[DB Update] Set active=false for blocked user: {toUserId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Push Exception] {ex.Message}");
            }
        }

        public async Task SendFridayBroadcastAsync()
        {
            // 1. ดึงข้อมูลรายงาน All (ตรรกะติดลบสีแดงที่คุณทำไว้)
            var reportData = await CreateTotalSummaryMessageAsync("");

            if (reportData != null)
            {
                // 2. ดึงรายชื่อผู้รับจากตาราง line_recipients
                var recipients = await GetAllActiveRecipientsAsync();

                // 3. ส่งหาทุกคน (Push Message)
                foreach (var userId in recipients)
                {
                    // เรียกใช้ฟังก์ชันยิง HTTP Client ไปยัง LINE API
                    await PushMessageToLineAsync(userId, reportData);
                }
            }
        }
    }
}