using Dapper;
using DinkToPdf;
using DinkToPdf.Contracts;
using DocumentFormat.OpenXml.Drawing.Charts;
using LineExcelScheduler.Models;
using LineExcelScheduler.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Collections.Generic;
using System.Threading.Tasks; 
using DinkOrientation = DinkToPdf.Orientation;
using System.Text;
using System.Linq;

namespace LineExcelScheduler.Controllers.CardQuery
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExcelController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly IConverter _converter;

        public ExcelController(IConfiguration configuration, IConverter converter)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new Exception("Database connection string 'DefaultConnection' not found in appsettings.json");
            _converter = converter;
        }

        [HttpGet("generate-pdf")]
        public async Task<IActionResult> GeneratePdf([FromQuery] string? keyword = null)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                string sql = @"
                    SELECT 
                        t.company_code as CompanyCode,
                        t.team_name as TeamName,
                        t.member_count as MemberCount,
                        ftrm.role_code as RoleCode,
                        ftrm.manday as Manday,
                        fta.month as Month,
                        fta.target_amount as TargetAmount,
                        fta.actual_amount as ActualAmount 
                    FROM ""Line_oa"".fact_team_amounts fta 
                    JOIN ""Line_oa"".teams t ON t.id = fta.team_id 
                    JOIN ""Line_oa"".fact_team_role_mandays ftrm ON ftrm.team_id = fta.team_id AND ftrm.MONTH = fta.month
                    WHERE (@keyword IS NULL OR t.team_name  = @keyword)
                    ORDER BY t.company_code, t.team_name, fta.month";

                var result = await conn.QueryAsync<CardTeamData>(sql, new { keyword });
                var dataList = result.ToList();

                var groupedData = dataList
                    .GroupBy(x => new { x.CompanyCode, x.TeamName, x.MemberCount })
                    .Select(g => new TeamGroup
                    {
                        CompanyCode = g.Key.CompanyCode,
                        TeamName = g.Key.TeamName,
                        MemberCount = g.Key.MemberCount,
                        Data = g.ToList()
                    })
                    .ToList();
                var pdfBytes = CreatePdf(groupedData);

                return File(pdfBytes, "application/pdf", $"Report_{keyword}.pdf");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private byte[] CreatePdf(List<TeamGroup> groupedData)
        {
            var htmlBuilder = new StringBuilder();
            htmlBuilder.Append(@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <style>
                        @page {
                            size: A4 portrait;
                            margin: 10mm;
                        }
        
                        body {
                            font-family: 'Arial', sans-serif;
                            margin: 0;
                            font-size: 14px;
                        }
        
                        h1 {
                            text-align: center;
                            color: #333;
                            margin-bottom: 20px;
                            font-size: 16px;
                        }
        
                        .page-container {
                            page-break-after: always;
                        }
        
                        .page-container:last-child {
                            page-break-after: auto;
                        }
        
                        table {
                            width: 100%;
                            border-collapse: collapse;
                            border: 2px solid #000;
                            margin-bottom: 20px;
                        }
        
                        thead {
                            display: table-header-group;
                        }
        
                        th {
                            background-color: #1f4e78;
                            color: white;
                            padding: 8px 4px;
                            text-align: center;
                            border: 1px solid #000;
                            font-weight: bold;
                            font-size: 12px;
                        }
        
                        td {
                            padding: 6px 4px;
                            border: 1px solid #000;
                            text-align: center;
                            font-size: 12px;
                        }
        
                        .header-dark {
                            background-color: #1f4e78;
                            color: white;
                            font-weight: bold;
                        }
                        .company-cell {
                            background-color: #ffffff;
                            font-weight: bold;
                        }
                        .team-cell {
                            background-color: #ffffff;
                        }
                        .manday-total-cell {
                            background-color: #ffffff;
                            font-weight: bold;
                        }
                        .role-cell {
                            background-color: #ffffff;
                            text-align: center;
                        }
                        .label-target {
                            background-color: #00B0F0;
                            color: #000;
                            font-weight: normal;
                            text-align: left;
                            padding-left: 8px;
                        }
                        .label-actual {
                            background-color: #DAE9F8;
                            color: #000;
                            font-weight: normal;
                            text-align: left;
                            padding-left: 8px;
                        }
                        .total-cell {
                            background-color: #ffffff;
                            text-align: right;
                            padding-right: 8px;
                        }
                        .month-cell {
                            background-color: #ffffff;
                            text-align: right;
                            padding-right: 8px;
                        }
                        .value-red {
                            color: #ff0000;
                        }
                        .value-green {
                            color: #00b050;
                        }
                    </style>
                </head>
                <body>");

            const int teamsPerPage = 9;
            int totalPages = (int)Math.Ceiling((double)groupedData.Count / teamsPerPage);

            for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var teamsInPage = groupedData
                    .Skip(pageIndex * teamsPerPage)
                    .Take(teamsPerPage)
                    .ToList();

                htmlBuilder.Append("<div class='page-container'>");
                htmlBuilder.Append($"<h1>Monthly Available 2026 (Page {pageIndex + 1}/{totalPages})</h1>");
                htmlBuilder.Append(@"
                <table>
                    <thead>
                        <tr>
                            <th class='header-dark'>บริษัท</th>
                            <th class='header-dark'>Team</th>
                            <th class='header-dark'>จำนวนคน</th>
                            <th class='header-dark'>Role</th>
                            <th class='header-dark'>Total</th>
                            <th class='header-dark'>Jan</th>
                            <th class='header-dark'>Feb</th>
                            <th class='header-dark'>Mar</th>
                            <th class='header-dark'>Apr</th>
                            <th class='header-dark'>May</th>
                            <th class='header-dark'>Jun</th>
                            <th class='header-dark'>Jul</th>
                            <th class='header-dark'>Aug</th>
                            <th class='header-dark'>Sep</th>
                            <th class='header-dark'>Oct</th>
                            <th class='header-dark'>Nov</th>
                            <th class='header-dark'>Dec</th>
                        </tr>
                    </thead>
                    <tbody>");

                foreach (var teamGroup in teamsInPage)
                {
                    var teamName = teamGroup.TeamName;
                    var companyCode = teamGroup.CompanyCode;
                    var memberCount = teamGroup.MemberCount;

                    var roleGroups = teamGroup.Data
                        .GroupBy(x => x.RoleCode)
                        .OrderBy(g => g.Key)
                        .ToList();

                    int totalRows = roleGroups.Count + 2;

                    decimal grandTotalManday = teamGroup.Data.Sum(x => x.Manday);
                    decimal grandTotalTarget = teamGroup.Data.GroupBy(x => x.Month).Sum(g => g.First().TargetAmount);
                    decimal grandTotalActual = teamGroup.Data.GroupBy(x => x.Month).Sum(g => g.First().ActualAmount);

                    bool isFirstRow = true;
                    foreach (var roleGroup in roleGroups)
                    {
                        var roleCode = roleGroup.Key;
                        decimal totalManday = roleGroup.Sum(x => x.Manday);

                        var monthlyManday = new Dictionary<int, decimal>();
                        for (int j = 1; j <= 12; j++)
                        {
                            monthlyManday[j] = 0;
                        }
                        foreach (var item in roleGroup)
                        {
                            monthlyManday[item.Month] = item.Manday;
                        }

                        htmlBuilder.Append("<tr>");

                        if (isFirstRow)
                        {
                            htmlBuilder.Append($"<td class='company-cell' rowspan='{totalRows}'>{companyCode}</td>");
                            htmlBuilder.Append($"<td class='team-cell' rowspan='{totalRows}'>{teamName}</td>");
                            htmlBuilder.Append($"<td class='manday-total-cell' rowspan='{totalRows}'>{memberCount}</td>");
                            isFirstRow = false;
                        }

                        htmlBuilder.Append($"<td class='role-cell'>{roleCode}</td>");
                        htmlBuilder.Append($"<td class='total-cell'>{totalManday:0.####}</td>");

                        for (int month = 1; month <= 12; month++)
                        {
                            var manday = monthlyManday[month];
                            htmlBuilder.Append($"<td class='month-cell'>{ manday.ToString("0.####") }</td>");
                        }

                        htmlBuilder.Append("</tr>");
                    }
                    var monthlyTarget = new Dictionary<int, decimal>();
                    var monthlyActual = new Dictionary<int, decimal>();

                    for (int j = 1; j <= 12; j++)
                    {
                        monthlyTarget[j] = 0;
                        monthlyActual[j] = 0;
                    }

                    var monthGroups = teamGroup.Data.GroupBy(x => x.Month);
                    foreach (var monthGroup in monthGroups)
                    {
                        int month = monthGroup.Key;
                        monthlyTarget[month] = monthGroup.First().TargetAmount;
                        monthlyActual[month] = monthGroup.First().ActualAmount;
                    }

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append("<td class='label-target'>Amount target</td>");
                    htmlBuilder.Append($"<td class='label-target'>{grandTotalTarget:N2}</td>");

                    for (int month = 1; month <= 12; month++)
                    {
                        var target = monthlyTarget[month];
                        htmlBuilder.Append($"<td class='label-target'>{target:N2}</td>");
                    }
                    htmlBuilder.Append("</tr>");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append("<td class='label-actual'>Amount actual</td>");
                    htmlBuilder.Append($"<td class='label-actual'>{grandTotalActual:N2}</td>");

                    for (int month = 1; month <= 12; month++)
                    {
                        var target = monthlyTarget[month];
                        var actual = monthlyActual[month];
                        var valueClass = actual < target ? "value-red" : (actual > target ? "value-green" : "");
                        htmlBuilder.Append($"<td class='label-actual {valueClass}'>{actual:N2}</td>");
                    }
                    htmlBuilder.Append("</tr>");
                }

                htmlBuilder.Append(@"
                        </tbody>
                    </table>
                </div>");
            }

            htmlBuilder.Append(@"
            </body>
            </html>");

            var globalSettings = new DinkToPdf.GlobalSettings
            {
                ColorMode = DinkToPdf.ColorMode.Color,
                Orientation = DinkToPdf.Orientation.Portrait,
                PaperSize = DinkToPdf.PaperKind.A4,
                Margins = new DinkToPdf.MarginSettings { Top = 10, Bottom = 10, Left = 10, Right = 10 },
                DocumentTitle = "Monthly Available Report"
            };

            var objectSettings = new DinkToPdf.ObjectSettings
            {
                PagesCount = true,
                HtmlContent = htmlBuilder.ToString(),
                WebSettings = { DefaultEncoding = "utf-8" },
                HeaderSettings = {
            FontName = "Arial",
            FontSize = 9,
            Right = "Page [page] of [toPage]",
            Line = true
        },
                FooterSettings = {
            FontName = "Arial",
            FontSize = 9,
            Line = true,
            Center = "Generated on " + DateTime.Now.ToString("dd/MM/yyyy HH:mm")
        }
            };

            var pdf = new DinkToPdf.HtmlToPdfDocument()
            {
                GlobalSettings = globalSettings,
                Objects = { objectSettings }
            };

            return _converter.Convert(pdf);
        }

        public class CardTeamData
        {
            public string CompanyCode { get; set; }
            public string TeamName { get; set; }
            public int MemberCount { get; set; }
            public string RoleCode { get; set; }
            public decimal Manday { get; set; }
            public int Month { get; set; }
            public decimal TargetAmount { get; set; }
            public decimal ActualAmount { get; set; }
        }

        public class TeamGroup
        {
            public string CompanyCode { get; set; }
            public string TeamName { get; set; }
            public int MemberCount { get; set; }
            public List<CardTeamData> Data { get; set; }
        }
    }
}