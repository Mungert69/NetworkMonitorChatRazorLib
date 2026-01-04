# NetworkMonitorChatRazorLib

Reusable Razor component library that implements the chat UI used by NetworkMonitor
frontends. It encapsulates streaming WebSocket messaging, audio capture/playback,
message rendering, and session history management.

## Key pieces
- `Chat.razor` and `MessageDisplay.razor` render the core chat experience.
- `WebSocketService.cs` handles streaming LLM responses and reconnection.
- `AudioService.cs` records audio input and plays TTS responses.
- `ChatStateService.cs` stores sessions, notifications, and UI state.
- `Models/` defines chat messages, history, and notification models.

## Usage
Reference the library from a Blazor or MAUI project and register the services in DI.
The components expect a running LLM backend (see `NetworkMonitorLLM`) and the
monitor service for function-call execution.

## Tests
`Tests/` contains component and service-level tests for chat state and WebSocket logic.
