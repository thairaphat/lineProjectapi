namespace LineExcelScheduler.Models
{
    public class LineWebhookRequest
    {
        public List<LineEvent> Events { get; set; } = new List<LineEvent>();
    }

    public class LineEvent
    {
        public string Type { get; set; } = string.Empty;
        public string ReplyToken { get; set; } = string.Empty;
        public LineSource Source { get; set; } = new LineSource();
        public LineMessage Message { get; set; } = new LineMessage();
    }

    public class LineSource { public string UserId { get; set; } = string.Empty; }
    public class LineMessage { public string Text { get; set; } = string.Empty; }
}