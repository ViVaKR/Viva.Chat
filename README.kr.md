# ViVaKR.Chat

ASP.NET Core에 바로 꽂아 쓰는 **SignalR 채팅 허브**입니다. 여러 사이트가 함께 쓰도록 만들었습니다.

허브 하나, 계약(Contracts) 하나, 아주 작은 저장소 인터페이스 하나. 두 줄이면 어떤 API에든 연결되고, 사이트마다 방이 섞이지 않습니다.

[![NuGet](https://img.shields.io/nuget/v/ViVaKR.Chat.svg)](https://www.nuget.org/packages/ViVaKR.Chat)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[English](README.md)

## 특징

- 강타입 허브(`Hub<IChatClient>`)로 서버 → 클라이언트 호출을 컴파일 시점에 검증
- **멀티 사이트 설계**: 방을 `{site}:{room}` 으로 구분해 `buddham:lobby` 와 `writer:lobby` 가 섞이지 않음
- 사이트 ID, 허용 오리진, 허브 경로, 메시지 길이, 이력 개수를 옵션 패턴으로 설정
- `IChatStore` 추상화: PostgreSQL, EF Core 등 원하는 저장소를 직접 연결
- 규칙과 신원을 갈아 끼우는 확장점: `IChatGuard`(입장 권한, 메시지 검사), `ISiteResolver`(연결의 사이트 판별), `IChatIdentityResolver`(JWT 클레임에서 사용자 식별). `UseGuard<T>()`, `UseSiteResolver<T>()`, `UseIdentityResolver<T>()` 로 교체
- 안전한 기본값: 아무것도 저장하지 않는 기본 저장소, 입력 검증, 입장 후에만 전송 허용, 연결당 분당 전송 제한
- SignalR 자격 증명과 함께 쓸 수 있는 CORS 정책(선택)
- ASP.NET Core 공유 프레임워크만 사용하므로 SignalR 패키지를 따로 설치할 필요 없음
- .NET 10 지원

## 설치

```bash
dotnet add package ViVaKR.Chat
```

`ViVaKR.Chat.Contracts` 는 의존성으로 함께 설치됩니다. 클라이언트나 저장소 프로젝트처럼 메시지 타입만 필요하다면 따로 참조할 수 있습니다.

```bash
dotnet add package ViVaKR.Chat.Contracts
```

## 구성 요소

| 패키지 | 구분 | 이름 | 설명 |
|--------|------|------|------|
| `ViVaKR.Chat.Contracts` | Record | `ChatMessage` | 채팅 메시지 하나 |
| `ViVaKR.Chat.Contracts` | Record | `ChatParticipant` | 입장·퇴장한 사람 (ID, 이름, 아바타) |
| `ViVaKR.Chat.Contracts` | Interface | `IChatClient` | 서버가 클라이언트에서 호출하는 메서드 |
| `ViVaKR.Chat.Contracts` | Interface | `IChatStore` | 저장소 추상화 |
| `ViVaKR.Chat.Contracts` | Interface | `IChatGuard` | 방 입장 권한과 메시지 검사 |
| `ViVaKR.Chat` | Class | `ChatHub` | SignalR 허브 |
| `ViVaKR.Chat` | Interface | `ISiteResolver` | 연결이 속한 사이트를 정하는 확장점 |
| `ViVaKR.Chat` | Class | `VivaChatOptions` | 설정 옵션 |
| `ViVaKR.Chat` | Extensions | `AddVivaChat` / `MapVivaChat` | DI 등록과 엔드포인트 매핑 |
| `ViVaKR.Chat` | Class | `VivaChatBuilder` | `AddVivaChat` 이 돌려주는 빌더 (`UseStore`, `UseGuard`, `UseSiteResolver`, `UseIdentityResolver`, `.SignalR`) |

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
| `UserJoined` | `room`, `ChatParticipant` | 누군가 입장 (0.3 부터 이름 문자열 대신 `{ userId, name, avatar }`) |
| `UserLeft` | `room`, `ChatParticipant` | 누군가 퇴장 |

### 사용자 식별 (0.3)

허브는 연결할 때 JWT 클레임에서 사용자를 한 번 읽어 둡니다. 기본값은 다음과 같고, 옵션이나 `UseIdentityResolver<T>()` 로 바꿀 수 있습니다.

| 용도 | 클레임 (기본) | 옵션 |
|------|---------------|------|
| 사용자 ID | `sub` (없으면 `NameIdentifier`) | `UserIdClaim` |
| 표시 이름 | `preferred_username` | `DisplayNameClaim` |
| 아바타 | `picture` | `AvatarClaim` |

- 표시 이름은 필명만 씁니다. `name`(실명)과 `email` 로 대신하지 않고, 필명이 없으면 `user-xxxx` 로 보입니다. 로그인하지 않았으면 `guest-xxxx` 입니다.
- 이름과 아바타는 연결 시점의 토큰 값이라서, 바꾸면 다시 연결한 뒤에 반영됩니다.
- `ChatMessage.Avatar` 는 보낸 시점의 스냅샷이고, `IChatGuard` 에는 `ChatCaller.Principal`(클레임 전체)이 넘어갑니다.

### 허브 연결 옵션 (0.3)

전송 방식, 버퍼 크기, 상태 유지 재연결 같은 연결 옵션은 `MapVivaChat` 에 넘깁니다.

```csharp
app.MapVivaChat(o =>
{
    o.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
    o.AllowStatefulReconnects = true;
});
```

### CORS (0.3)

호스트에 이미 CORS 정책이 있으면 `AllowedOrigins` 는 비워 두고 정책 이름을 줍니다. 허브에 걸리는 정책은 호스트의 전역 `UseCors` 보다 우선하므로 둘을 섞지 마세요.

```json
{ "VivaChat": { "CorsPolicy": "Origins" } }
```

### 허브 경로

허브는 `/hubs/<이름>` 에 둡니다 (`/hubs/chat`). 호스트는 이 접두어 하나로 JWT 쿼리 토큰(`access_token`) 허용, nginx 의 WebSocket 설정, 감사 로그 제외를 처리할 수 있습니다.

### 오류 처리 (0.3)

저장소나 가드에서 예외가 나면 허브가 로그를 남기고(메시지 본문은 남기지 않음) 클라이언트에는 한국어 `HubException` 메시지를 보냅니다. 저장에 실패한 메시지는 방에 전달되지 않습니다.

### 확장점

| 하고 싶은 일 | 구현할 것 | 교체 |
|---|---|---|
| 메시지를 DB 에 저장 | `IChatStore` | `.UseStore<T>()` |
| 비공개 방, 욕설·개인정보 필터 | `IChatGuard` | `.UseGuard<T>()` |
| 서브도메인 등으로 사이트를 서버가 판별 | `ISiteResolver` | `.UseSiteResolver<T>()` |
| 사용자 식별 방식 변경 | `IChatIdentityResolver` | `.UseIdentityResolver<T>()` |
| 서버를 여러 대 띄울 때 메시지 공유 | Redis 백플레인 | `.SignalR.AddStackExchangeRedis(...)` |

### VivaChatOptions

| 옵션 | 기본값 | 설명 |
|------|--------|------|
| `DefaultSiteId` | `"default"` | 연결에 `site` 쿼리 값이 없을 때 사용하며 항상 허용됩니다 |
| `AllowedSites` | `[]` | 이 호스트가 추가로 받아들일 사이트 ID |
| `AllowedOrigins` | `[]` | CORS 허용 오리진. 비워 두면 CORS 정책을 등록하지 않습니다 |
| `CorsPolicy` | `null` | 호스트가 이미 가진 CORS 정책 이름 (0.3). 지정하면 허브에 이 정책을 쓰고 자체 정책은 만들지 않습니다 |
| `HubPath` | `"/hubs/chat"` | 엔드포인트 경로 |
| `MaxMessageLength` | `2000` | 허용하는 최대 메시지 길이 |
| `HistoryOnJoin` | `50` | `Join` 이 돌려주는 메시지 수 |
| `MaxMessagesPerMinute` | `30` | 연결 하나당 분당 최대 메시지 수 (0 이면 제한 없음) |
| `RequireAuthentication` | `false` | `true` 면 허브에 `RequireAuthorization()` 을 적용합니다 |
| `AnnouncePresence` | `true` | 입장·퇴장 알림 전송 여부 |
| `UserIdClaim` | `"sub"` | 사용자 ID 를 읽는 클레임 (0.3) |
| `DisplayNameClaim` | `"preferred_username"` | 표시 이름(필명)을 읽는 클레임 (0.3) |
| `AvatarClaim` | `"picture"` | 아바타를 읽는 클레임 (0.3) |
| `MaxDisplayNameLength` | `50` | 표시 이름 최대 길이 (0.3) |

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

저장소가 없어도 허브는 동작하지만, 아무것도 저장되지 않고 `Join` 은 빈 이력을 돌려줍니다. `IChatStore` 를 구현하고 `UseStore<T>()` 로 등록하세요. (`AddVivaChat` 호출 전에 `AddScoped` 로 등록해도 동작합니다.)

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

## 알아 둘 점

- **`site` 값은 클라이언트가 정합니다.** 방을 나누는 용도이지 보안 경계가 아닙니다. 더 엄격하게 다루려면 JWT 클레임이나 요청의 `Origin` 과 대조하세요.
- **재연결하면 방 멤버십이 사라집니다.** SignalR은 재연결 시 새 연결 ID를 부여하므로 `onreconnected` 안에서 `Join` 을 다시 호출해야 합니다.
- **WebSocket 환경의 JWT** 는 `access_token` 쿼리 문자열로 전달됩니다. `/hubs` 경로에 한해 `JwtBearerEvents.OnMessageReceived` 에서 꺼내 쓰세요.
- **레플리카가 둘 이상이면** 호스트에 Redis 백플레인(`Microsoft.AspNetCore.SignalR.StackExchangeRedis`)을 추가해야 인스턴스 간에 메시지가 전달됩니다.
- 인증되지 않은 사용자는 연결이 인증되기 전까지 `guest-xxxx` 로 표시됩니다.
- **`ai-` 로 시작하는 방 이름은 예약되어 있습니다** (0.4 부터). `ViVaKR.AiChat` 이 쓰는 이름이라서 `ChatHub` 는 입장을 거절합니다. 이미 `ai-` 로 시작하는 방을 쓰고 있다면 이름을 바꾸세요.

## 요구 사항

- .NET 10
- ASP.NET Core (`Microsoft.AspNetCore.App` 공유 프레임워크)

## 관련 패키지

- [ViVaKR.AiChat](https://www.nuget.org/packages/ViVaKR.AiChat): ViVaKR.Chat 위에 얹는 AI 채팅 허브 (AiChatServer 의 답변을 SignalR 로 스트리밍)

## 라이선스

[MIT](LICENSE)

## 만든 사람

**ViVaKR** · [GitHub](https://github.com/ViVaKR/Viva.Chat)
설계·검토 동료: **제독** (Claude, Anthropic) 🪷
