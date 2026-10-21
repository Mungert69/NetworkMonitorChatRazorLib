# Chat setup and WebSocket protocol

This documents the existing `WebSocketService`, `ChatStateService`, and
`Chat.razor` handling. The protocol carries independent message types, not one
continuous string containing all notifications and conversation output.

## Connect and initialize

`ConnectWebSocket` connects, then sends the handshake before starting the
receive task:

```text
timeZone,runnerType,requestSessionId,chatAgentLocation,encodedDeviceContext
```

The last two fields provide optional client context. Empty subsequent messages
are keepalives.

`Initialize` queues `<|REPLAY_HISTORY|>` and clears the internal
`_isConnectionReady` flag. On a complete message exactly equal to
`</llm-ready>`, `ProcessMessage` sets `_isConnectionReady` and `ChatState.IsReady`,
then sends and clears the queued replay request.

Do not infer that every send is gated by these flags: `SendMessage` currently
waits for an open WebSocket. `ChatState.IsReady` also has a default value of
`true`; explicit reset sets it to `false`. The readiness flags have different
roles from the underlying socket state.

## Receive complete messages, then classify

`ReceiveMessages` accumulates bytes until `EndOfMessage`, decodes UTF-8 once,
and awaits `ProcessMessage` for that complete message. Preserve this boundary
when implementing another client.

| Payload | Existing handling |
| --- | --- |
| Exactly `</llm-ready>` | Update readiness and release queued replay; do not append to chat text. |
| Prefix `</llm-success>`, `</llm-info>`, `</llm-warning>`, or `</llm-error>` | Create a `SystemMessage`; `ChatStateService` adds a notification. |
| Exactly `<end-of-line>` | Clear `IsProcessing`. |
| `<history-display-name>...</history-display-name>` | Update saved-history metadata. |
| `<function-data>...</function-data>` | Process structured function/link data. |
| Ordinary conversation text/chunks | Filter and append to `LLMFeedback`. |

The prefixes are literal markers, not balanced XML/HTML. A startup greeting
prefixed with `</llm-success>` is a notification, not conversation content.
Notifications render separately from `LLMFeedback`.

## Readiness does not impose notification order

The backend emits readiness after the primary's startup acknowledgement and
session registration. Startup notifications travel through a separate backend
consumer and may arrive before or after readiness. Late notifications do not
reset readiness or alter conversation text.

Readiness does not mean all experts are ready, all notifications have arrived,
or history replay has completed. History/conversation output after readiness is
normal because replay is requested at readiness.

Repeated readiness leaves an already-cleared replay queue empty. It is not an
instruction to reset the conversation. `Reconnect` reconnects without calling
`ResetLLM`, clearing chat state, or explicitly queuing another replay.
`ResetLLM` is a distinct operation that clears feedback, draft, and other UI
state. The current code retains readiness flags during automatic reconnect;
socket-open and primary-readiness states are not interchangeable.

The protocol has no setup sequence number, universal 20-second setup timeout,
or `socket-ready` message. A startup error can arrive without readiness, and
missed response chunks are not automatically recovered.
