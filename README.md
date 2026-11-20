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
- Extension points for rules and identity: `IChatGuard` (join permission and message checks), `ISiteResolver` (which site a connection belongs to), and `IChatIdentityResolver` (who the user is, read from JWT claims). Swap them with `UseGuard<T>()`, `UseSiteResolver<T>()`, and `UseIdentityResolver<T>()`
- Safe defaults: a no-op store, input validation, a join-before-send rule, and a per-connection send rate limit
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
| `ViVaKR.Chat.Contracts` | Record | `ChatParticipant` | Someone who joined or left (ID, name, avatar) |
| `ViVaKR.Chat.Contracts` | Interface | `IChatClient` | Methods the server calls on clients |
| `ViVaKR.Chat.Contracts` | Interface | `IChatStore` | Persistence abstraction |
| `ViVaKR.Chat.Contracts` | Interface | `IChatGuard` | Room join permission and message checks |
| `ViVaKR.Chat` | Class | `ChatHub` | The SignalR hub |
| `ViVaKR.Chat` | Interface | `ISiteResolver` | Decides which site a connection belongs to |
| `ViVaKR.Chat` | Class | `VivaChatOptions` | Configuration |
| `ViVaKR.Chat` | Extensions | `AddVivaChat` / `MapVivaChat` | DI registration and endpoint mapping |
| `ViVaKR.Chat` | Class | `VivaChatBuilder` | The builder returned by `AddVivaChat` (`UseStore`, `UseGuard`, `UseSiteResolver`, `UseIdentityResolver`, `.SignalR`) |

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
| `UserJoined` | `room`, `ChatParticipant` | Someone joined (since 0.3: `{ userId, name, avatar }` instead of a name string) |
| `UserLeft` | `room`, `ChatParticipant` | Someone left |

### User identity (0.3)

The hub reads the user from the JWT claims once, when the connection is made. The defaults are below, and you can change them with options or `UseIdentityResolver<T>()`.

| Purpose | Claim (default) | Option |
|---------|-----------------|--------|
| User ID | `sub` (falls back to `NameIdentifier`) | `UserIdClaim` |
| Display name | `preferred_username` | `DisplayNameClaim` |
| Avatar | `picture` | `AvatarClaim` |

- The display name uses the pen name only. It never falls back to `name` (the real name) or `email`; without a pen name it shows as `user-xxxx`. Users who are not signed in show as `guest-xxxx`.
- The name and avatar are the values in the token at connect time, so a change takes effect after reconnecting.
- `ChatMessage.Avatar` is a snapshot taken when the message was sent, and `IChatGuard` receives `ChatCaller.Principal` (all claims).

### Hub connection options (0.3)

Connection options such as transports, buffer sizes, and stateful reconnect go to `MapVivaChat`.

```csharp
app.MapVivaChat(o =>
{
    o.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
    o.AllowStatefulReconnects = true;
});
```

### CORS (0.3)

If the host already has a CORS policy, leave `AllowedOrigins` empty and give the policy name instead. A policy attached to the hub takes precedence over the host's global `UseCors`, so do not mix the two.

```json
{ "VivaChat": { "CorsPolicy": "Origins" } }
```

### Hub path

Put hubs under `/hubs/<name>` (`/hubs/chat`). With that single prefix the host can handle the JWT query token (`access_token`), the nginx WebSocket settings, and audit-log exclusion.

### Error handling (0.3)

If the store or the guard throws, the hub logs it (never the message body) and sends the client a Korean `HubException` message. A message that failed to save is not delivered to the room.

### Extension points

| What you want | Implement | Replace with |
|---|---|---|
| Save messages to a database | `IChatStore` | `.UseStore<T>()` |
| Private rooms, profanity and personal-data filters | `IChatGuard` | `.UseGuard<T>()` |
| Decide the site on the server (for example by subdomain) | `ISiteResolver` | `.UseSiteResolver<T>()` |
| Identify users some other way | `IChatIdentityResolver` | `.UseIdentityResolver<T>()` |
| Share messages across several server instances | Redis backplane | `.SignalR.AddStackExchangeRedis(...)` |

### VivaChatOptions

| Option | Default | Description |
|--------|---------|-------------|
| `DefaultSiteId` | `"default"` | Used when the connection has no `site` query value; always allowed |
| `AllowedSites` | `[]` | Additional site IDs this host accepts |
| `AllowedOrigins` | `[]` | CORS origins. Leave empty to skip registering a CORS policy |
| `CorsPolicy` | `null` | Name of a CORS policy the host already has (0.3). When set, the hub uses it and no policy of its own is created |
| `HubPath` | `"/hubs/chat"` | Endpoint path |
| `MaxMessageLength` | `2000` | Longest accepted message |
| `HistoryOnJoin` | `50` | Messages returned by `Join` |
| `MaxMessagesPerMinute` | `30` | Most messages one connection may send per minute (0 means no limit) |
| `RequireAuthentication` | `false` | When `true`, `RequireAuthorization()` is applied to the hub |
| `AnnouncePresence` | `true` | Whether join and leave notifications are sent |
| `UserIdClaim` | `"sub"` | Claim that holds the user ID (0.3) |
| `DisplayNameClaim` | `"preferred_username"` | Claim that holds the display name, the pen name (0.3) |
| `AvatarClaim` | `"picture"` | Claim that holds the avatar (0.3) |
| `MaxDisplayNameLength` | `50` | Longest display name (0.3) |

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

Without a store, the hub still works, but nothing is saved and `Join` returns an empty history. Implement `IChatStore` and register it with `UseStore<T>()`. (Registering it with `AddScoped` before calling `AddVivaChat` works too.)

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

builder.Services.AddVivaChat(o => { /* ... */ })
    .UseStore<PgChatStore>();
```

## Good to know

- **The `site` value is chosen by the client.** It separates rooms, but it is not a security boundary. For stricter control, check it against a JWT claim or the request `Origin`.
- **Reconnects lose room membership.** SignalR assigns a new connection ID after a reconnect, so call `Join` again inside `onreconnected`.
- **JWT over WebSockets** travels as the `access_token` query string. Read it in `JwtBearerEvents.OnMessageReceived` for paths under `/hubs`.
- **More than one replica?** Add a Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) on the host, or messages will not cross instances.
- Users who are not signed in appear as `guest-xxxx`.

## Requirements

- .NET 10
- ASP.NET Core (the `Microsoft.AspNetCore.App` shared framework)

## Related

- [ViVaKR.AuthLibrary](https://www.nuget.org/packages/ViVaKR.AuthLibrary): Identity entities for JWT-based authentication

## License

[MIT](LICENSE)

## Author

**ViVaKR** · [GitHub](https://github.com/ViVaKR/Viva.Chat)
Design & review partner: Claude (Anthropic)
