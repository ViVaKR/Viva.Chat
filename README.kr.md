# Viva.Chat

ASP.NET Core에 바로 꽂아 쓰는 **SignalR 채팅 허브**입니다. 여러 사이트가 함께 쓰도록 만들었습니다.

허브 하나, 계약(Contracts) 하나, 아주 작은 저장소 인터페이스 하나. 두 줄이면 어떤 API에든 연결되고, 사이트마다 방이 섞이지 않습니다.

[![NuGet](https://img.shields.io/nuget/v/Viva.Chat.svg)](https://www.nuget.org/packages/Viva.Chat)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[English](README.md)

## 특징

- 강타입 허브(`Hub<IChatClient>`)로 서버 → 클라이언트 호출을 컴파일 시점에 검증
- **멀티 사이트 설계**: 방을 `{site}:{room}` 으로 구분해 `buddham:lobby` 와 `writer:lobby` 가 섞이지 않음
- 사이트 ID, 허용 오리진, 허브 경로, 메시지 길이, 이력 개수를 옵션 패턴으로 설정
- `IChatStore` 추상화: PostgreSQL, EF Core 등 원하는 저장소를 직접 연결
- 안전한 기본값: 아무것도 저장하지 않는 기본 저장소, 입력 검증, 입장 후에만 전송 허용
- SignalR 자격 증명과 함께 쓸 수 있는 CORS 정책(선택)
- ASP.NET Core 공유 프레임워크만 사용하므로 SignalR 패키지를 따로 설치할 필요 없음
- .NET 10 지원

## 설치

```bash
dotnet add package Viva.Chat
```

`Viva.Chat.Contracts` 는 의존성으로 함께 설치됩니다. 클라이언트나 저장소 프로젝트처럼 메시지 타입만 필요하다면 따로 참조할 수 있습니다.

```bash
dotnet add package Viva.Chat.Contracts
```

## 구성 요소

| 패키지 | 구분 | 이름 | 설명 |
|--------|------|------|------|
| `Viva.Chat.Contracts` | Record | `ChatMessage` | 채팅 메시지 하나 |
| `Viva.Chat.Contracts` | Interface | `IChatClient` | 서버가 클라이언트에서 호출하는 메서드 |
| `Viva.Chat.Contracts` | Interface | `IChatStore` | 저장소 추상화 |
| `Viva.Chat` | Class | `ChatHub` | SignalR 허브 |
| `Viva.Chat` | Class | `VivaChatOptions` | 설정 옵션 |
| `Viva.Chat` | Extensions | `AddVivaChat` / `MapVivaChat` | DI 등록과 엔드포인트 매핑 |

### 허브 메서드 (클라이언트 → 서버)

| 메서드 | 반환 | 설명 |
|--------|------|------|
| `Join(room)` | `ChatMessage[]` | 방에 입장하고 최근 이력을 돌려줍니다 |
| `Leave(room)` | | 방에서 나갑니다 |
| `SendMessage(room, text)` | | 메시지를 저장하고 방 전체에 전달합니다 |

### 클라이언트 이벤트 (서버 → 클라이언트)

| 이벤트 | 인자 | 설명 |
|--------|------|------|
| `ReceiveMessage` | `ChatMessage` | 새 메시지 도착 |
| `UserJoined` | `room`, `user` | 누군가 입장 |
| `UserLeft` | `room`, `user` | 누군가 퇴장 |

### VivaChatOptions

| 옵션 | 기본값 | 설명 |
|------|--------|------|
| `DefaultSiteId` | `"default"` | 연결에 `site` 쿼리 값이 없을 때 사용하며 항상 허용됩니다 |
| `AllowedSites` | `[]` | 이 호스트가 추가로 받아들일 사이트 ID |
| `AllowedOrigins` | `[]` | CORS 허용 오리진. 비워 두면 CORS 정책을 등록하지 않습니다 |
| `HubPath` | `"/hubs/chat"` | 엔드포인트 경로 |
| `MaxMessageLength` | `2000` | 허용하는 최대 메시지 길이 |
| `HistoryOnJoin` | `50` | `Join` 이 돌려주는 메시지 수 |

## 빠른 시작

### 1. 등록과 매핑

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

app.UseCors();       // 인증 미들웨어보다 앞에
app.MapVivaChat();   // /hubs/chat
```

### 2. 클라이언트 연결

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
await connection.invoke('SendMessage', 'lobby', '안녕하세요!');
```

### 3. 저장소 연결 (선택)

저장소가 없어도 허브는 동작하지만, 아무것도 저장되지 않고 `Join` 은 빈 이력을 돌려줍니다. `IChatStore` 를 구현하고 `AddVivaChat` 호출 **전에** 등록하세요.

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

## 알아 둘 점

- **`site` 값은 클라이언트가 정합니다.** 방을 나누는 용도이지 보안 경계가 아닙니다. 더 엄격하게 다루려면 JWT 클레임이나 요청의 `Origin` 과 대조하세요.
- **재연결하면 방 멤버십이 사라집니다.** SignalR은 재연결 시 새 연결 ID를 부여하므로 `onreconnected` 안에서 `Join` 을 다시 호출해야 합니다.
- **WebSocket 환경의 JWT** 는 `access_token` 쿼리 문자열로 전달됩니다. `/hubs` 경로에 한해 `JwtBearerEvents.OnMessageReceived` 에서 꺼내 쓰세요.
- **레플리카가 둘 이상이면** 호스트에 Redis 백플레인(`Microsoft.AspNetCore.SignalR.StackExchangeRedis`)을 추가해야 인스턴스 간에 메시지가 전달됩니다.
- 인증되지 않은 사용자는 연결이 인증되기 전까지 `guest-xxxx` 로 표시됩니다.

## 요구 사항

- .NET 10
- ASP.NET Core (`Microsoft.AspNetCore.App` 공유 프레임워크)

## 관련 패키지

- [ViVaKR.AuthLibrary](https://www.nuget.org/packages/ViVaKR.AuthLibrary): JWT 인증용 Identity 엔티티 모음

## 라이선스

[MIT](LICENSE)

## 만든 사람

**ViVaKR** · [GitHub](https://github.com/ViVaKR/Viva.Chat)
