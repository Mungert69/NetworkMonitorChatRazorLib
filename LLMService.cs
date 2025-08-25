using NetworkMonitor.Connection;

namespace NetworkMonitorChat
{

    public interface ILLMService
{
    List<string> GetLLMTypes();
    string GetLLMServerUrl(string siteId);
    string GetLLMServerAuthUrl(string siteId);
}
    public  class LLMService : ILLMService
    {
        private readonly NetConnectConfig _netConfig;
        public LLMService(NetConnectConfig netConfig)
        {
            _netConfig = netConfig;
        }
        public string GetLLMServerUrl(string siteId)
        {
            // Implement your logic to get the LLM server URL
            return $"wss://{_netConfig.ChatServer}/LLM/llm-stream";
        }

         public string GetLLMServerAuthUrl(string siteId)
        {
            // Implement your logic to get the LLM server URL
            return $"wss://{_netConfig.ChatServer}/LLM/llm-stream-auth";
        }

        public  List<string> GetLLMTypes()
        {
            return new List<string> { "TurboLLM", "HugLLM", "TestLLM" };
        }
    }
}