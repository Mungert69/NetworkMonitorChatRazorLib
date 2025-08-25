
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NetworkMonitor.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace NetworkMonitorChat
{
    public class WebSocketService : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private readonly ChatStateService _chatState;
        private readonly IJSRuntime _jsRuntime;
        private readonly AudioService _audioService;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ILLMService _llmService;
        private Task? _receiveTask;
        private Task? _pingTask;
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 5;
        private bool _isReconnecting = false;
        private readonly object _reconnectLock = new object();
        private string _queuedReplayMessage="";
        private bool _isConnectionReady;
        private NetConnectConfig _netConfig;

        public string QueuedReplayMessage { get => _queuedReplayMessage; set => _queuedReplayMessage = value; }

        public WebSocketService(ChatStateService chatState, IJSRuntime jsRuntime, AudioService audioService, ILLMService llmService, NetConnectConfig netConfig)
        {
            _chatState = chatState;
            _jsRuntime = jsRuntime;
            _audioService = audioService;
            _cancellationTokenSource = new CancellationTokenSource();
            _llmService = llmService;
            _netConfig = netConfig;
            _netConfig.OnAuthCompleteAsync += AuthCompleteAync;

        }

        public async Task Initialize()
        {
            QueuedReplayMessage = "<|REPLAY_HISTORY|>";
            _isConnectionReady = false;
           
            try
            {
                await ConnectWebSocket(); // This now JUST connects without sending init message
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WebSocket initialization failed: {ex}");
            }
             _netConfig.AgentUserFlow.IsChatOpened=true;

        }

        private async Task ConnectWebSocket()
        {
            try
            {
                await CleanupWebSocket();

                _webSocket = new ClientWebSocket();
                var serverUrl = _llmService.GetLLMServerUrl(_chatState.SiteId);

                // Add the auth token to the request headers if provided
                if (!string.IsNullOrEmpty(_netConfig.LocalSystemUrl.RabbitPassword))
                {
                    _webSocket.Options.SetRequestHeader(
    "Authorization",
    $"Bearer {_netConfig.LocalSystemUrl.RabbitPassword}"
);
                    serverUrl = _llmService.GetLLMServerAuthUrl(_chatState.SiteId);
                    Console.WriteLine($"Using Auth Url {serverUrl}");

                }
                await _webSocket.ConnectAsync(new Uri(serverUrl), _cancellationTokenSource.Token);
                Console.WriteLine($"WebSocket connection established to {serverUrl}");

                // Send initialization IMMEDIATELY after connection
                var timeZone = TimeZoneInfo.Local.Id;
                var sendStr = $"{timeZone},{_chatState.LLMRunnerType},{_chatState.SessionId},{_netConfig.MonitorLocation}";
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(sendStr)),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource.Token);

                Console.WriteLine($"Sent opening message to websocket: {sendStr}");

                _receiveTask = Task.Run(ReceiveMessages, _cancellationTokenSource.Token);
                _pingTask = Task.Run(PingInterval, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WebSocket connection error: {ex}");
            }
        }
      private async Task PingInterval()
{
    while (!_cancellationTokenSource.Token.IsCancellationRequested)
    {
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await Send("");
                Console.WriteLine("Sent web socket Ping");
            }
            else
            {
                await Reconnect();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ping error: {ex}");
            await HandleSendFailure(ex);
        }
        
        // Pass the cancellation token to Task.Delay
        // This will throw OperationCanceledException when cancellation is requested
        try
        {
            await Task.Delay(20000, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Exit the loop immediately when cancellation is requested
            break;
        }
    }
}
        // Add these helper methods
        private async Task HandleSendFailure(Exception ex)
        {
            Console.Error.WriteLine($"Send failure: {ex.Message}");
            if (IsConnectionLost(ex))
            {
                await Reconnect();
            }
        }



        private bool IsConnectionLost(Exception ex)
        {
            return ex is WebSocketException ||
                   ex is InvalidOperationException ||
                   _webSocket?.State == WebSocketState.Aborted;
        }
        private async Task ReceiveMessages()
        {
            var buffer = new byte[65535];
            while (!_cancellationTokenSource.Token.IsCancellationRequested )
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessage(message);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"WebSocket receive error: {ex}");
                    return;
                }
            }
        }

        private async Task ProcessMessage(string message)
        {
            try
            {

                if (message == "</llm-ready>")
                {
                    _isConnectionReady = true;
                    _chatState.IsReady = true;
                    await TrySendQueuedMessage();
                }
                // Handle audio tags first
                if (message.Contains("</audio>"))
                {
                    var parts = message.Split(new[] { "</audio>" }, StringSplitOptions.None);
                    var textPart = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    var audioFile = parts.Length > 1 ? parts[1].Trim() : string.Empty;



                    if (!string.IsNullOrEmpty(audioFile) && !_chatState.IsMuted)
                    {
                        await _audioService.PlayAudioSequentially(audioFile);
                    }
                    return;
                }

                Console.WriteLine($"Received message: {message}");
                if (message.StartsWith("<function-data>") && message.EndsWith("</function-data>"))
                {
                    // Extract the function data
                    var functionData = message.Substring("<function-data>".Length,
                        message.Length - "<function-data>".Length - "</function-data>".Length);
                    Console.WriteLine($"Found function data: {functionData}");

                    var generatedLinkData = ProcessFunctionData(functionData);
                    if (generatedLinkData != null)
                    {
                        _chatState.LinkData = generatedLinkData;
                        _chatState.IsDrawerOpen = true;
                    }
                    return;
                }
                // Handle control messages first
                if (message.StartsWith("</llm-"))
                {
                    ProcessControlMessage(message);
                    return;
                }
                else if (message == "<end-of-line>")
                {
                    _chatState.IsProcessing = false;
                    return;
                }
                else if (message.StartsWith("<history-display-name>"))
                {
                    ProcessHistoryDisplayData(message.Substring("<history-display-name>".Length,
                        message.Length - "<history-display-name>".Length - "</history-display-name>".Length));
                    return;
                }

                // Handle streaming text messages
                if (!string.IsNullOrWhiteSpace(message))
                {
                    // Accumulate the message chunks
                    _chatState.LLMFeedback += FilterLLMOutput(message);



                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing message: {ex}");
            }
            finally
            {
                await _chatState.NotifyStateChanged();
            }
        }


        private async Task TrySendQueuedMessage()
        {
            if (_isConnectionReady && !string.IsNullOrEmpty(QueuedReplayMessage))
            {
                await Send(QueuedReplayMessage);
                QueuedReplayMessage = null;
            }
        }
        private void ProcessControlMessage(string message)
        {


            if (message.StartsWith("</llm-error>"))
            {
                _chatState.Message = new SystemMessage
                {
                    Persist = true,
                    Text = message.Substring("</llm-error>".Length),
                    Success = false
                };
                
            }
            else if (message.StartsWith("</llm-info>"))
            {
                _chatState.Message = new SystemMessage
                {
                    Info = "",
                    Text = message.Substring("</llm-info>".Length)
                };
            }
            else if (message.StartsWith("</llm-warning>"))
            {
                _chatState.Message = new SystemMessage
                {
                    Warning = "",
                    Text = message.Substring("</llm-warning>".Length)
                };
            }
            else if (message.StartsWith("</llm-success>"))
            {
                _chatState.Message = new SystemMessage
                {
                    Success = true,
                    Text = message.Substring("</llm-success>".Length)
                };
            }
            else if (message == "</functioncall>")
            {
                _chatState.IsCallingFunction = true;
            }
            else if (message == "</functioncall-complete>")
            {
                _chatState.IsCallingFunction = false;
            }
            else if (message == "</llm-busy>")
            {
                _chatState.IsLLMBusy = true;
            }
            else if (message == "</llm-listening>")
            {
                _chatState.IsLLMBusy = false;
            }
        }
        private string FilterLLMOutput(string text)
        {
            var replacements = new Dictionary<string, string>
            {
                { "<\\|from\\|> user.*\\n<\\|recipient\\|> all.*\\n<\\|content\\|>", "<User:> " },
                { "<\\|from\\|> assistant\\n<\\|recipient\\|> (?!all).*<\\|content\\|>", "<Function Call:>" },
                { "<Assistant:><\\|reserved_special_token_249\\|>", "<Function Call:>" },
                { "<Assistant:><tool_call>", "<Function Call:>" },
                { "<\\|from\\|> assistant\\n<\\|recipient\\|> all\\n<\\|content\\|>", "<Assistant:>" },
                { "<\\|start_header_id\\|>assistant<\\|end_header_id\\|>\\n\\n>>>all\\n", "<Assistant:>" },
                { "<\\|start_header_id\\|>assistant<\\|end_header_id\\|>\\n\\n", "<Assistant:>" },
                { "<\\|im_start\\|>assistant\\n", "<Assistant:>" },
                { "<\\|im_start\\|>assistant<\\|im_sep\\|>\\n", "<Assistant:>" },
                { "<\\|assistant\\|>\\n", "<Assistant:>" },
                { "<\\|from\\|> (?!user|assistant).*<\\|recipient\\|> all.*\\n<\\|content\\|>", "<Function Response:> " },
                { "<\\|stop\\|>", "\n" },
                { "<\\|eot_id\\|>", "\n" },
                { "<\\|eom_id\\|>", "\n" },
                { "<\\|im_end\\|>", "\n" },
                { "<\\|end\\|>", "\n" }
            };

            var filteredText = text;
            foreach (var replacement in replacements)
            {
                filteredText = System.Text.RegularExpressions.Regex.Replace(filteredText, replacement.Key, replacement.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return filteredText;
        }

        private List<HostLink>? ProcessFunctionData(string functionData)
        {
            if (!_chatState.IsDashboard) return null;

            try
            {
                var jsonData = System.Text.Json.JsonSerializer.Deserialize<FunctionData>(functionData);
                if (jsonData == null || string.IsNullOrEmpty(jsonData.Name) || jsonData.DataJson == null)
                {
                    Console.Error.WriteLine("Malformed function data received");
                    return null;
                }

                switch (jsonData.Name)
                {
                    case "get_host_list":
                        return jsonData.DataJson.Select(host => new HostLink
                        {
                            Address = host.Address,
                            UserID = host.UserID,
                            IsHostList = host.UserID != "default",
                            DataSetID = 0,
                            DateStarted = host.DateStarted
                        }).ToList();

                    case "get_host_data":
                        return jsonData.DataJson.Select(host => new HostLink
                        {
                            Address = host.Address,
                            IsHostData = true,
                            DateStarted = host.DateStarted
                        }).ToList();

                    case "add_host":
                    case "edit_host":
                        return jsonData.DataJson.Select(host => new HostLink
                        {
                            Address = host.Address,
                            UserID = host.UserID,
                            IsHostList = host.UserID != "default",
                            DateStarted = host.DateStarted
                        }).ToList();

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing function data: {ex}");
                return null;
            }
        }

        private void ProcessHistoryDisplayData(string historyDisplayData)
        {
            try
            {


                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Handles any remaining case mismatches
                };

                var histories = JsonSerializer.Deserialize<List<ChatHistory>>(historyDisplayData, options);
                if (histories != null)
                {
                    _chatState.Histories = histories;
                }
                else
                {
                    Console.Error.WriteLine("Invalid history data format");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing history display data: {ex}");
            }
        }

        public async Task SendMessage(string message)
        {
            try
            {
                await WaitForWebSocket();
                await Send(message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error sending message: {ex}");
            }
        }

        public async Task StopLLM()
        {
            await SendMessage("<|STOP_LLM|>");
            Console.WriteLine("Message sent: <|STOP_LLM|>");
        }

        public async Task ResetLLM(bool createNewSession = true)
        {
            try
            {

                // Reset chat state
                _chatState.IsReady = false;
                _chatState.LLMFeedback = "";
                _chatState.IsProcessing = false;
                _chatState.IsLLMBusy = false;
                _chatState.IsCallingFunction = false;
                _chatState.ThinkingDots = "";
                _chatState.CallingFunctionMessage = "Processing function...";
                _chatState.ShowHelpMessage = false;
                _chatState.HelpMessage = "";
                _chatState.CurrentMessage = "";

                // Create new session if requested
                if (createNewSession)
                {
                    await _chatState.ClearSession();
                }

                await ConnectWebSocket();
                await _chatState.NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"LLM reset error: {ex}");
            }
        }

        // Modified Dispose
        public void Dispose()
        {
            try
            {
                _cancellationTokenSource.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disposing",
                        CancellationToken.None).Wait(3000);
                }
            }
            finally
            {
                _webSocket?.Dispose();
                _cancellationTokenSource.Dispose();
            }
        }



        public async Task CloseConnection()
        {
            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing connection",
                        _cancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error closing WebSocket: {ex}");
            }
            finally
            {
                _webSocket?.Dispose();
                _webSocket = null;
            }
        }



        private async Task WaitForWebSocket()
        {
            while (_webSocket?.State != WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cancellationTokenSource.Token);
                Console.Error.WriteLine($"Waiting for WebSocket");
            }
        }



        private async Task Send(string message)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    await Reconnect();
                }

                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket?.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource.Token);

                // Reset reconnect attempts on successful send
                _reconnectAttempts = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error sending message: {ex}");
                await HandleSendFailure(ex);
                throw; // Re-throw to let caller know send failed
            }
        }
     private async Task CleanupWebSocket()
{
    try
    {
        // Cancel any ongoing operations first
        if (!_cancellationTokenSource.IsCancellationRequested)
            _cancellationTokenSource.Cancel();
            
        // Wait for background tasks to complete
        if (_receiveTask != null)
        {
            try { await Task.WhenAny(_receiveTask, Task.Delay(1000)); } catch { }
            _receiveTask = null;
        }
        
        if (_pingTask != null)
        {
            try { await Task.WhenAny(_pingTask, Task.Delay(1000)); } catch { }
            _pingTask = null;
        }

        if (_webSocket != null)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Cleaning up",
                    CancellationToken.None);
            }
            
            _webSocket.Dispose();
            _webSocket = null;
        }

        // Reset cancellation token source
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WebSocket cleanup error: {ex}");
    }
}
        private async Task Reconnect()
        {
            lock (_reconnectLock)
            {
                if (_isReconnecting) return;
                _isReconnecting = true;
            }

            try
            {
                _reconnectAttempts++;
                if (_reconnectAttempts > MaxReconnectAttempts)
                {
                    Console.Error.WriteLine("Max reconnect attempts reached");
                    return;
                }

                var delay = Math.Min(30000, (int)Math.Pow(2, _reconnectAttempts) * 1000);
                await Task.Delay(delay);

                Console.WriteLine($"Attempting to reconnect (attempt {_reconnectAttempts})");


                // Establish new connection
                await ConnectWebSocket();

                _reconnectAttempts = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Reconnect attempt failed: {ex}");
                if (_reconnectAttempts < MaxReconnectAttempts)
                {
                    await Reconnect();
                }
            }
            finally
            {
                lock (_reconnectLock)
                {
                    _isReconnecting = false;
                }
            }
        }
        public async Task ResetService()
        {
            await _chatState.Initialize();
            await Initialize();

        }
        private async Task AuthCompleteAync()
        {
            await ResetService();
        }
        private class FunctionData
        {
            public string Name { get; set; }="";
            public List<HostLink> DataJson { get; set; }= new ();
        }
    }
}