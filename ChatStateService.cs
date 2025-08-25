
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace NetworkMonitorChat
{
    public class ChatStateService
    {
        // Audio and UI state
        public bool IsMuted { get; set; } = true;
        public bool IsExpanded { get; set; } = true;
        public bool IsDrawerOpen { get; set; } = false;
        public bool AutoScrollEnabled { get; set; } = true;
        public string InitRunnerType = "TurboLLM";
        public string SiteId { get; set; } = "";

        // Processing and loading states
        public bool IsReady { get; set; } = true;
        public int LoadCount { get; set; } = 0;
        public string LoadWarning { get; set; } = "";
        public bool IsProcessing { get; set; } = false;
        public bool IsCallingFunction { get; set; } = false;
        public bool IsLLMBusy { get; set; } = false;
        public bool IsToggleDisabled { get; set; } = false;

        // Message and feedback states
        public string ThinkingDots { get; set; } = "";
        public string CallingFunctionMessage { get; set; } = "Calling function...";
        public bool ShowHelpMessage { get; set; } = false;
        public string HelpMessage { get; set; } = "";
        public string CurrentMessage { get; set; } = "";
        public bool IsDashboard { get; set; }
        private string _lLMRunnerType = "TurboLLM";
        private string _sessionId;

        public string SessionId
        {
            get => _sessionId;
            set
            {
                _sessionId = value;
                _ = NotifyStateChanged();
            }
        }
        

        // In ChatStateService.cs
        public string LLMFeedback
        {
            get => _llmFeedback;
            set
            {
                _llmFeedback = value;
                _ = NotifyStateChanged();
            }
        }
        private string _llmFeedback = string.Empty;

        public List<ChatHistory> Histories
        {
            get => _histories;
            set
            {
                _histories = value;
                _ = NotifyStateChanged();
            }
        }
        public string LLMRunnerType
        {
            get => _lLMRunnerType;
            set
            {
                _lLMRunnerType = value;
                _ = NotifyStateChanged();
            }
        }

        private List<ChatHistory> _histories = new();
        public List<HostLink> LinkData { get; set; } = new List<HostLink>();

        public bool IsHoveringMessages { get; set; } = false;
        public bool IsInputFocused { get; set; } = false;

        // Session management
        public string OpenMessage { get; set; }
        public bool AutoClickedRef { get; set; } = false;

        private readonly IJSRuntime _jsRuntime;

        public ChatStateService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }


        public async Task Initialize()
        {
            LLMRunnerType = InitRunnerType;
            await GetSessionId();
        }
        public async Task ClearSession()
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "sessionId");
            await GetSessionId();
        }


        public async Task StoreNewSessionID(string newSessionId)
        {
            SessionId = newSessionId;
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "sessionId", newSessionId);
        }

        private async Task GetSessionId()
        {
            // Check if we have a session in localStorage
            var storedSessionId = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "sessionId");

            if (!string.IsNullOrEmpty(storedSessionId))
            {
                SessionId = storedSessionId;
                return ;
            }

            // Create new session
            var newSessionId = Guid.NewGuid().ToString();
            await StoreNewSessionID(newSessionId);

        }

        public event Func<Task>? OnChange = null; // Changed from Action to Func<Task>


        public async Task NotifyStateChanged()
        {
            if (OnChange != null)
            {
                try
                {
                    await OnChange.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in state notification: {ex}");
                }
            }
        }
        // Add to your ChatStateService class
       

        public List<Notification> Notifications { get; } = new List<Notification>();

        private SystemMessage? _message;
        public SystemMessage? Message
        {
            get => _message;
            set
            {
                _message = value;
                if (value != null)
                {
                    AddNotification(value);
                }
                NotifyStateChanged();
            }
        }

        private void AddNotification(SystemMessage message)
        {
            var notification = new Notification
            {
                Message = message.Text,
                Persist = message.Persist,
                Duration = message.Persist ? 60000 : 10000 // Longer duration for persistent messages
            };

            // Determine notification type based on SystemMessage properties
            if (message.Success)
            {
                notification.Type = "success";
            }
            else if (!string.IsNullOrEmpty(message.Warning))
            {
                notification.Type = "warning";
            }
            else if (!string.IsNullOrEmpty(message.Info))
            {
                notification.Type = "info";
            }
            else if (!message.Success) // Error case
            {
                notification.Type = "error";
            }

            Notifications.Add(notification);

            // Auto-remove after duration if not persistent
            if (!notification.Persist)
            {
                _ = RemoveNotificationAfterDelay(notification.Id, notification.Duration);
            }
        }

        private async Task RemoveNotificationAfterDelay(string id, int delay)
        {
            await Task.Delay(delay);
            Notifications.RemoveAll(n => n.Id == id);
            await NotifyStateChanged();
        }

        public async Task DismissNotification(string id)
        {
            Notifications.RemoveAll(n => n.Id == id);
            await NotifyStateChanged();
        }
    }
}
