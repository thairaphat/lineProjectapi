using Microsoft.AspNetCore.Mvc;
using LineExcelScheduler.Models;
using LineExcelScheduler.Services;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace LineExcelScheduler.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly LineMessageService _lineMessageService;
        private readonly string _channelAccessToken;
        private static readonly HttpClient _httpClient = new HttpClient();

        public WebhookController(LineMessageService lineMessageService, IConfiguration configuration)
        {
            _lineMessageService = lineMessageService;
            _channelAccessToken = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN")
                      ?? configuration["LINE:channelAccessToken"]
                      ?? throw new Exception("LINE channelAccessToken is not set");
        }

        [HttpPost]
        public async Task<IActionResult> HandleWebhook([FromBody] LineWebhookRequest request)
        {
            if (request?.Events == null || !request.Events.Any()) return Ok();

            var lineEvent = request.Events[0];
            var replyToken = lineEvent.ReplyToken;
            var userId = lineEvent.Source?.UserId;

            if (!string.IsNullOrEmpty(userId))
            {
                await _lineMessageService.SaveLineRecipientAsync(userId);
            }
            if (lineEvent.Type == "follow")
            {
                await SendTextWithQuickReply(replyToken, "สวัสดีครับ! ยินดีต้อนรับสู่ระบบรายงานอัตโนมัติ", CreateMainMenuQuickReply());
                return Ok();
            }

            if (lineEvent.Type != "message" || lineEvent.Message == null) return Ok();

            var keyword = lineEvent.Message.Text;

            try
            {
                if (keyword.StartsWith("ดูต่อ:"))
                {
                    var parts = keyword.Split(':');
                    string originalKeyword = parts[1];
                    int skipCount = int.Parse(parts[2]);

                    var flexResult = await _lineMessageService.CreateMessageDataAsync(originalKeyword, "", skipCount);
                    await ProcessFlexResult(replyToken, flexResult, originalKeyword);
                }
                else
                {
                    var lowerKeyword = keyword.ToLower();

                    if (lowerKeyword.StartsWith("บริษัท: "))
                    {
                        string companyCode = keyword.Replace("บริษัท: ", "").Trim();

                        var companySummary = await _lineMessageService.CreateTotalSummaryMessageAsync(companyCode);

                        var teamsInCompany = await _lineMessageService.GetTeamsByCompanyAsync(companyCode);

                        if (companySummary != null)
                        {
                            await ReplyFlexWithCustomQuickReply(replyToken, companySummary, CreateTeamInCompanyQuickReply(teamsInCompany));
                        }
                        else
                        {
                            await SendTextWithQuickReply(replyToken, $"ไม่พบข้อมูลสำหรับบริษัท {companyCode}", CreateMainMenuQuickReply());
                        }
                    }
                    else
                    {
                        switch (lowerKeyword)
                        {


                            case "เลือกทีม":
                                var companies = await _lineMessageService.GetCompanyListAsync();
                                await SendTextWithQuickReply(replyToken, "🏢 กรุณาเลือกบริษัทที่ต้องการดูข้อมูล", CreateCompanyQuickReply(companies));
                                break;


                            case "all":
                                var totalSummary = await _lineMessageService.CreateTotalSummaryMessageAsync("");

                                if (totalSummary != null)
                                {
                                    var flexMessages = ((dynamic)totalSummary).messages;

                                    var cleanPayload = new
                                    {
                                        replyToken = replyToken,
                                        messages = flexMessages
                                    };

                                    await PostToLine(cleanPayload);
                                }
                                break;

                            case "เมนูหลัก":
                            case "สวัสดี":
                            case "hello":
                                await SendTextWithQuickReply(replyToken, "สวัสดีครับ! ยินดีต้อนรับสู่ระบบ\nเลือกเมนูด้านล่างเพื่อเริ่มต้นใช้งาน", CreateMainMenuQuickReply());
                                break;

                            case "ช่วยเหลือ":
                                await SendTextWithQuickReply(replyToken, " วิธีการใช้งาน:\n1.  เลือกทีม - เลือกบริษัทและทีม\n3.  พิมพ์ 'all' - สรุปยอดรวมทั้งหมด", CreateMainMenuQuickReply());
                                break;

                            default:
                                var flexResult = await _lineMessageService.CreateMessageDataAsync(keyword, "", 0);
                                await ProcessFlexResult(replyToken, flexResult, keyword);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return Ok();
        }
        private async Task ProcessFlexResult(string replyToken, object flexResult, string keyword)
        {
            if (flexResult == null)
            {
                await SendTextWithQuickReply(replyToken, $"🔍 ไม่พบข้อมูลสำหรับ '{keyword}'", CreateMainMenuQuickReply());
                return;
            }

            var flexData = (dynamic)flexResult;
            int? nextSkip = flexData.nextSkip;

            if (!nextSkip.HasValue)
            {
                var flexMessages = flexData.messages;
                var cleanPayload = new
                {
                    replyToken = replyToken,
                    messages = flexMessages
                };
                await PostToLine(cleanPayload);
            }
            else
            {
                var nextQuickReply = new
                {
                    items = new[] {
                new {
                    type = "action",
                    action = new {
                        type = "message",
                        label = "➡ ดูข้อมูลถัดไป",
                        text = $"ดูต่อ:{keyword}:{nextSkip}"
                    }
                },
            }
                };
                await ReplyFlexWithCustomQuickReply(replyToken, flexResult, nextQuickReply);
            }
        }

        private async Task PostToLine(object payload)
        {
            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);

            Console.WriteLine("========== LINE PAYLOAD ==========");
            Console.WriteLine(json);
            Console.WriteLine("==================================");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _channelAccessToken);

            var response = await _httpClient.SendAsync(request);

            Console.WriteLine($"[LINE STATUS] {(int)response.StatusCode} {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();

                Console.WriteLine("========== LINE ERROR ==========");
                Console.WriteLine(errorBody);
                Console.WriteLine("================================");
            }
            else
            {
                Console.WriteLine("[LINE SUCCESS] Message sent successfully");
            }
        }


        private async Task SendTextWithQuickReply(string replyToken, string text, object quickReply)
        {
            var payload = new
            {
                replyToken = replyToken,
                messages = new[] { new { type = "text", text = text, quickReply = quickReply } }
            };
            await PostToLine(payload);
        }

        private async Task ReplyFlexWithQuickReply(string replyToken, object flexData)
        {
            await ReplyFlexWithCustomQuickReply(replyToken, flexData, CreateMainMenuQuickReply());
        }

        private async Task ReplyFlexWithCustomQuickReply(string replyToken, object flexData, object quickReply)
        {
            var flexMessages = ((dynamic)flexData).messages;
            var messagesWithQuickReply = new List<object>();

            foreach (var msg in flexMessages)
            {
                messagesWithQuickReply.Add(new
                {
                    type = msg.type,
                    altText = msg.altText,
                    contents = msg.contents,
                    quickReply = quickReply
                });
            }

            var payload = new { replyToken = replyToken, messages = messagesWithQuickReply };
            await PostToLine(payload);
        }

        private object CreateMainMenuQuickReply() => new
        {
            items = new[] {
        new { type = "action", action = new { type = "message", label = " เลือกทีม", text = "เลือกทีม" } },
        new { type = "action", action = new { type = "message", label = " สรุปทั้งหมด", text = "all" } }, 
        new { type = "action", action = new { type = "message", label = " ช่วยเหลือ", text = "ช่วยเหลือ" } }
    }
        };

        private object CreateMonthQuickReply()
        {
            var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            return new { items = months.Select(m => new { type = "action", action = new { type = "message", label = m, text = m } }).ToArray() };
        }

        private object CreateTeamQuickReply() => new
        {
            items = new[] {
                new { type = "action", action = new { type = "message", label = "พี่แหม่ม", text = "พี่แหม่ม" } },
                new { type = "action", action = new { type = "message", label = "หยก", text = "หยก" } },
                new { type = "action", action = new { type = "message", label = "แอน", text = "แอน" } },
                new { type = "action", action = new { type = "message", label = "กบ", text = "กบ" } },
                new { type = "action", action = new { type = "message", label = "ดา", text = "ดา" } },
                new { type = "action", action = new { type = "message", label = "ดาว", text = "ดาว" } },
                new { type = "action", action = new { type = "message", label = "บอย", text = "บอย" } },
                new { type = "action", action = new { type = "message", label = "อ้น", text = "อ้น" } },
                new { type = "action", action = new { type = "message", label = "เปิ้ล", text = "เปิ้ล" } },
                new { type = "action", action = new { type = "message", label = "แม็ก", text = "แม็ก" } },
                new { type = "action", action = new { type = "message", label = "แชมป์", text = "แชมป์" } },
                new { type = "action", action = new { type = "message", label = "Project Co", text = "Project Co" } },
                new { type = "action", action = new { type = "message", label = " กลับเมนูหลัก", text = "เมนูหลัก" } }
            }
        };

        private object CreateCompanyQuickReply(List<string> companies) => new
        {
            items = companies
        .Where(c => !string.IsNullOrWhiteSpace(c)) 
        .Distinct()
        .Take(12)
        .Select(c => new
        {
            type = "action",
            action = new
            {
                type = "message",
                label = c,
                text = $"บริษัท: {c}"
            }
        })
        .ToArray()
        };

        private object CreateTeamInCompanyQuickReply(List<string> teams) => new
        {
            items = teams
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Distinct()
        .Take(12)
        .Select(t => new
        {
            type = "action",
            action = new
            {
                type = "message",
                label = t,
                text = t
            }
        })
        .Concat(new[] {
            new {
                type = "action",
                action = new {
                    type = "message",
                    label = " เปลี่ยนบริษัท",
                    text = "เลือกทีม"
                }
            }
        })
        .ToArray()
        };

    }
}