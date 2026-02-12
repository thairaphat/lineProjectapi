using Azure.Identity;
using LineExcelScheduler.Data;
using LineExcelScheduler.Services;
using Microsoft.Graph;
using Microsoft.Graph.Models.ExternalConnectors;
using System.Data;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace OneDriveFileAccess
{
    public class OneDriveGetFileService
    {
        private readonly ExcelService _excelService;
        private readonly IConfiguration _configuration;
        public OneDriveGetFileService(ExcelService excelService, IConfiguration configuration)
        {
            _excelService = excelService;
            _configuration = configuration;
        }
        public async Task GetKeySharepoint()
        {
            // ตั้งค่า
            var clientId = _configuration["AzureAd:ClientId"]; 
            var tenantId = _configuration["AzureAd:TenantId"]; 
            var clientSecret = _configuration["AzureAd:ClientSecret"];
            var fileUrl = _configuration["AzureAd:FileUrl"];

            // สร้าง ClientSecretCredential เพื่อใช้สำหรับการยืนยันตัวตน
            var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);

            // สร้าง GraphServiceClient โดยใช้ ClientSecretCredential
            var graphClient = new GraphServiceClient(clientSecretCredential);

            // ดึงไฟล์จาก OneDrive หรือ SharePoint โดยใช้ลิงก์ที่แชร์
            await GetFileFromSharedLink(graphClient, fileUrl);


            await DownloadAndSaveSharedFileToDatabase(graphClient, fileUrl, string.Empty, _excelService);
            
            await ListExcelSheetTabs(graphClient, fileUrl);
        }

        public static async Task GetFileFromSharedLink(GraphServiceClient graphClient, string fileUrl)
        {
            try
            {
                // แปลง URL ที่แชร์ให้เป็น shareId ที่ถูกต้อง
                //string shareId = "IQBvkwIuJVc2SpDko8QBke84AdQmdsDvJqDOV1_Q4XVXH9A";
                var shareId = MakeGraphShareId(fileUrl);

                if (string.IsNullOrEmpty(shareId))
                {
                    Console.WriteLine("Invalid shareId");
                    return;
                }



                // ใช้ Microsoft Graph API เพื่อดึงข้อมูลจากลิงก์ที่แชร์
                var sharedItem = await graphClient.Shares[shareId].GetAsync();

                // แสดงข้อมูลไฟล์ที่ได้จากลิงก์
                Console.WriteLine($"File Name: {sharedItem.Name}");
                Console.WriteLine($"File ID: {sharedItem.Id}");

            }
            catch (ServiceException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static string MakeGraphShareId(string sharingUrl)
        {
            // Encode full URL (including query string)
            var bytes = Encoding.UTF8.GetBytes(sharingUrl);
            var base64 = Convert.ToBase64String(bytes);

            // Make it URL-safe and trim padding
            var urlSafe = base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

            // Prefix (u! is used for user-shared links)
            return "u!" + urlSafe;
        }

        public static async Task DownloadAndSaveSharedFileToDatabase(GraphServiceClient graphClient, string fileUrl, string sqlConnectionString, LineExcelScheduler.Services.ExcelService excelService)
        {
            if (string.IsNullOrWhiteSpace(fileUrl)) throw new ArgumentException(nameof(fileUrl));
            var shareId = MakeGraphShareId(fileUrl);

            // 1) Get the shared item and ask Graph to expand driveItem
            var sharedItem = await graphClient.Shares[shareId].GetAsync(req =>
            {
                req.QueryParameters.Expand = new[] { "driveItem" };
            });

            // If we have driveItem, try SDK content download
            var contentStream = await graphClient.Shares[shareId].DriveItem.Content.GetAsync();
            if (contentStream == null)
            {
                // 2) Fallback: look for downloadUrl in returned JSON (AdditionalData)
                // The download URL may sit on sharedItem or sharedItem.DriveItem.AdditionalData
                string? downloadUrl = null;
                if (sharedItem?.AdditionalData != null && sharedItem.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out var v1))
                    downloadUrl = v1 as string;
                else if (sharedItem?.DriveItem?.AdditionalData != null && sharedItem.DriveItem.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out var v2))
                    downloadUrl = v2 as string;

                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    using var http = new HttpClient();
                    contentStream = await http.GetStreamAsync(downloadUrl);
                }
                else
                {
                    Console.WriteLine("No content returned and no downloadUrl available. Check permissions or link type.");
                    return;
                }
            }

            // Read bytes
            byte[] contentBytes;
            using (var ms = new MemoryStream())
            {
                await contentStream.CopyToAsync(ms);
                contentBytes = ms.ToArray();
            }

            // Call your Excel import service with the downloaded workbook stream
            if (excelService != null)
            {
                using var workbookStream = new MemoryStream(contentBytes);
                workbookStream.Position = 0;
                // This is the line you wanted to call:
                var result = await excelService.ImportExcelToDb(workbookStream);
                Console.WriteLine($"ImportExcelToDb result: {result}");
            }

            // ... (any DB save logic using contentBytes can continue here)
        }

        public static async Task ListExcelSheetTabs(GraphServiceClient graphClient, string fileUrl)
        {
            var shareId = MakeGraphShareId(fileUrl);
            try
            {
                // Get the driveItem to retrieve its id and parentReference
                var driveItem = await graphClient.Shares[shareId].DriveItem.GetAsync();
                if (driveItem == null || driveItem.Id == null || driveItem.ParentReference == null || driveItem.ParentReference.DriveId == null)
                {
                    Console.WriteLine("Unable to resolve drive item for the shared link.");
                    return;
                }

                // Use the driveId and itemId to access the workbook/worksheets endpoint
                var worksheets = await graphClient
                    .Drives[driveItem.ParentReference.DriveId]
                    .Items[driveItem.Id]
                    .Workbook
                    .Worksheets
                    .GetAsync();

                var sheets = worksheets?.Value;
                if (sheets == null || sheets.Count == 0)
                {
                    Console.WriteLine("No worksheets found or access denied.");
                    return;
                }
                foreach (var ws in sheets)
                    Console.WriteLine(ws.Name);
            }
            catch (ServiceException ex)
            {
                Console.WriteLine($"Graph error: {ex.Message}");
            }
        }
    }


}
