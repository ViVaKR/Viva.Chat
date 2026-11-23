# ViVaKR.AiChat

An AI chat hub for [ViVaKR.Chat](https://www.nuget.org/packages/ViVaKR.Chat). It streams replies from an
[AiChatServer](https://github.com/ViVaKR/AiChatServer) (gRPC) to signed-in users over SignalR.

[![NuGet](https://img.shields.io/nuget/v/ViVaKR.AiChat.svg)](https://www.nuget.org/packages/ViVaKR.AiChat)

## Install

```bash
dotnet add package ViVaKR.AiChat
```

`ViVaKR.Chat`, `AiChatServer.Contracts` and the gRPC client are installed as dependencies. You do not add them yourself.

## Quick start

```csharp
using Viva.Chat;
using Viva.Chat.Ai;

builder.Services.AddVivaChat(o => o.DefaultSiteId = "buddham")
    .UseStore<PgChatStore>()
    .AddAiChat(o => o.ServerUrl = "http://aichat-svc.ai.svc.cluster.local:50051");

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapVivaChat();     // /hubs/chat
app.MapVivaAiChat();   // /hubs/ai  (signed-in users only)
```

A host without authentication fails at startup, because `/hubs/ai` requires authorization.

## Hub methods (client → server)

| Method | Returns | Description |
|---|---|---|
| `Open(sessionId?)` | `{ sessionId, history }` | Opens a session. Leave `sessionId` empty to create one (the server issues the ID). |
| `Send(sessionId, text)` | `{ userMessageId, streamId }` | Sends a question and returns immediately. One running turn per session. |
| `Cancel(sessionId)` | `bool` | Stops the running turn. |
| `Close(sessionId)` | | Stops receiving events for the session on this connection. |

## Client events (server → client)

| Event | Arguments |
|---|---|
| `ReceiveMessage` | `sessionId`, `message` (echo of your question) |
| `ReceiveChunk` | `sessionId`, `streamId`, `kind` (`thinking` / `content`), `delta` |
| `StreamCompleted` | `sessionId`, `streamId`, `message` (`message.id === streamId`) |
| `StreamFailed` | `sessionId`, `streamId`, `reason` (not saved) |
| `StreamCancelled` | `sessionId`, `streamId`, `partial` (saved text, or `null`) |

```ts
connection.on('ReceiveChunk', (sessionId, streamId, kind, delta) => { /* append to the bubble for streamId */ });
connection.on('StreamCompleted', (sessionId, streamId, message) => { /* replace the bubble with message */ });

await connection.start();
const { sessionId, history } = await connection.invoke('Open');
await connection.invoke('Send', sessionId, 'Hello');
```

After a reconnect, call `Open(sessionId)` again. Group membership does not survive reconnects.

## Extension points

| Goal | Implement | Replace |
|---|---|---|
| Call the AI server differently | `IAiChatGateway` | `.UseAiGateway<T>()` |
| Several replicas (lock and cancel across instances) | `IAiTurnGate` | `.UseAiTurnGate<T>()` |
| Prompt filtering, age rules | `IChatGuard` (from ViVaKR.Chat) | `.UseGuard<T>()` |

## Options (`VivaAiChat` section)

| Option | Default | Description |
|---|---|---|
| `ServerUrl` | (required) | AiChatServer gRPC address |
| `DefaultPersona` | `Beopdeung` | Persona sent to the server |
| `AiDisplayName` | `"법등"` | Name shown for AI messages |
| `HubPath` | `"/hubs/ai"` | Endpoint path |
| `EmitThinking` | `true` | Send the model's thinking chunks (never saved) |
| `MaxPromptLength` | `2000` | Maximum question length |
| `ContextHistoryCount` | `20` | Earlier messages sent as context |
| `HistoryOnOpen` | `50` | Messages returned by `Open` |
| `MaxTurnsPerMinute` | `6` | Per connection (0 disables) |
| `SavePartialOnCancel` | `true` | Keep the text received before a cancel |
| `RequestTimeoutSeconds` | `300` | gRPC deadline |

## License

[MIT](https://github.com/ViVaKR/Viva.Chat/blob/main/LICENSE)
