namespace NetworkMonitorChat
{
    public class SystemMessage
    {
        public bool Success { get; set; }
        public string? Info { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool Persist { get; set; }
        public string? Warning { get; set; }
    }
}