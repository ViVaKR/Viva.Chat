# ViVaKR.Chat

A drop-in **SignalR chat hub** for ASP.NET Core, built to be shared across many sites.

One hub, one set of contracts, and a tiny storage interface. Wire it into any API with two lines, and keep every site's rooms neatly separated.

[![NuGet](https://img.shields.io/nuget/v/ViVaKR.Chat.svg)](https://www.nuget.org/packages/ViVaKR.Chat)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[한국어](README.kr.md)

## Features

- Strongly typed hub (`Hub<IChatClient>`), so server-to-client calls are checked at compile time
- **Multi-site by design**: rooms are scoped as `{site}:{room}`, so `buddham:lobby` and `writer:lobby` never mix
- Options pattern for site IDs, allowed origins, hub path, message length, and history size
- `IChatStore` abstraction: bring your own database (PostgreSQL, EF Core, anything)
- Safe defaults: a no-op store, input validation, and a join-before-send rule
- Optional CORS policy that is compatible with SignalR credentials
- Uses the ASP.NET Core shared framework, with no extra SignalR package to install
- Targets .NET 10

## Installation

```bash
dotnet add package ViVaKR.Chat
```

`ViVaKR.Chat.Contracts` comes along as a dependency. If you only need the message types (for example in a client or a storage project), reference it on its own:

```bash
dotnet add package ViVaKR.Chat.Contracts
```

## What's inside

| Package | Kind | Name | Description |
|---------|------|------|-------------|
| `ViVaKR.Chat.Contracts` | Record | `ChatMessage` | A single chat message |
| `ViVaKR.Chat.Contracts` | Interface | `IChatClient` | Methods the server calls on clients |
| `ViVaKR.Chat.Contracts` | Interface | `IChatStore` | Persistence abstraction |
| `ViVaKR.Chat` | Class | `ChatHub` | The SignalR hub |
| `ViVaKR.Chat` | Class | `VivaChatOptions` | Configuration |
| `ViVaKR.Chat` | Extensions | `AddVivaChat` / `MapVivaChat` | DI registration and endpoint mapping |

### Hub methods (client → server)

| Method | Returns | Description |
|--------|---------|-------------|
| `Join(room)` | `ChatMessage[]` | Joins a room and returns recent history |
| `Leave(room)` | | Leaves a room |
| `SendMessage(room, text)` | | Stores and broadcasts a message to the room |

### Client events (server → client)

| Event | Arguments | Description |
|-------|-----------|-------------|
| `ReceiveMessage` | `ChatMessage` | A new message arrived |
| `UserJoined` | `room`, `user` | Someone joined |
| `UserLeft` | `room`, `user` | Someone left |

### VivaChatOptions

| Option | Default | Description |
|--------|---------|-------------|
| `DefaultSiteId` | `"default"` | Used when the connection has no `site` query value; always allowed |
| `AllowedSites` | `[]` | Additional site IDs this host accepts |
| `AllowedOrigins` | `[]` | CORS origins. Leave empty to skip registering a CORS policy |
| `HubPath` | `"/hubs/chat"` | Endpoint path |
| `MaxMessageLength` | `2000` | Longest accepted message |
| `HistoryOnJoin` | `50` | Messages returned by `Join` |

## Quick start

### 1. Register and map

```csharp
builder.Services.AddVivaChat(o =>
{
    o.DefaultSiteId = "buddham";
    o.AllowedSites = ["buddham", "writer", "text"];
    o.AllowedOrigins =
    [
        "https://buddham.co.kr",
        "https://writer.or.kr",
        "http://localhost:4200"
    ];
});

var app = builder.Build();

app.UseCors();       // before authentication
app.MapVivaChat();   // /hubs/chat
```

### 2. Connect from a client

```ts
import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
  .withUrl('https://chat.example.com/hubs/chat?site=buddham', {
    accessTokenFactory: () => token,
  })
  .withAutomaticReconnect()
  .build();

connection.on('ReceiveMessage', (message) => console.log(message));

await connection.start();
const history = await connection.invoke('Join', 'lobby');
await connection.invoke('SendMessage', 'lobby', 'Hello!');
```

### 3. Plug in your own storage (optional)

Without a store, the hub still works, but nothing is saved and `Join` returns an empty history. Implement `IChatStore` and register it **before** calling `AddVivaChat`:

```csharp
public sealed class PgChatStore(AppDbContext db) : IChatStore
{
    public async Task SaveAsync(ChatMessage message, CancellationToken ct = default)
    {
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(
        string siteId, string room, int take, CancellationToken ct = default)
        => await db.Messages
            .Where(m => m.SiteId == siteId && m.Room == room)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);
}

builder.Services.AddScoped<IChatStore, PgChatStore>();
builder.Services.AddVivaChat(o => { /* ... */ });
```

## Good to know

- **The `site` value is chosen by the client.** It separates rooms, but it is not a security boundary. For stricter control, check it against a JWT claim or the request `Origin`.
- **Reconnects lose room membership.** SignalR assigns a new connection ID after a reconnect, so call `Join` again inside `onreconnected`.
- **JWT over WebSockets** travels as the `access_token` query string. Read it in `JwtBearerEvents.OnMessageReceived` for paths under `/hubs`.
- **More than one replica?** Add a Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) on the host, or messages will not cross instances.
- Anonymous users appear as `guest-xxxx` until the connection is authenticated.

## Requirements

- .NET 10
- ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework)

## Related

- [ViVaKR.AuthLibrary](https://www.nuget.org/packages/ViVaKR.AuthLibrary): Identity entities for JWT-based authentication

## License

[MIT](LICENSE)

## Author

**ViVaKR** · [GitHub](https://github.com/ViVaKR/Viva.Chat)
