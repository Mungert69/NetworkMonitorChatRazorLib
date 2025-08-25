// Models/ChatHistory.cs
using System.Text.Json.Serialization;

namespace NetworkMonitorChat
{
    public class ChatHistory
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("history")]
        public List<ChatMessage> History { get; set; } = new List<ChatMessage>();

        [JsonPropertyName("startUnixTime")]
        public long StartUnixTime { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("llmType")] // Matches the JSON's lowercase "llmType"
        public string LLMType { get; set; } = string.Empty;

        // Helper property to convert Unix time to DateTime (not part of JSON)
        public DateTime StartTime => DateTimeOffset.FromUnixTimeSeconds(StartUnixTime).DateTime;
    }
}