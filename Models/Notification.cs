 namespace NetworkMonitorChat;
 public class Notification
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Message { get; set; } = string.Empty;
            public string Type { get; set; } = "info"; // info, success, warning, error
            public int Duration { get; set; } = 5000; // ms
            public bool Persist { get; set; } = false;
        }