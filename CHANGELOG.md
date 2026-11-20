# 0.2.0-preview.1

> 공개 API 는 1.0 이전에 바뀔 수 있습니다. 아래는 0.1.0-preview.1 대비 변경입니다.

## 호환이 깨지는 변경

| 변경 | 이전 | 이후 |
|---|---|---|
| `VivaChatOptions` 위치 | `Viva.Chat.Contracts` (Contracts 프로젝트) | `Viva.Chat` (ViVaKR.Chat 프로젝트) |
| `AddVivaChat` 반환형 | `IServiceCollection` | `VivaChatBuilder` (`.Services`, `.SignalR`, `UseStore/UseGuard/UseSiteResolver`) |
| `ChatMessage` | 6개 필드 | 끝에 `string? UserId = null` 추가 (생성자는 소스 호환, 저장소는 컬럼 추가 필요) |
| `ChatHub` 생성자 | `IChatStore`, `IOptions<>` | `IChatGuard`, `ISiteResolver`, `TimeProvider` 추가 (DI 가 채움) |

## 동작 변경

- 연결이 끊기면 입장했던 방에 `UserLeft` 를 보냅니다 (이전에는 유령 사용자가 남음).
- 같은 방에 `Join` 을 반복해도 `UserJoined` 는 첫 입장 때만 갑니다.
- 연결 하나당 분당 30건 전송 제한이 기본으로 켜졌습니다 (`MaxMessagesPerMinute`, 0 이면 끔).
- 메시지 ID 가 `Guid.CreateVersion7` (시간순) 으로 바뀌었습니다.
- 메시지 저장은 보낸 사람의 연결 끊김과 무관하게 끝까지 진행됩니다.
- 옵션은 appsettings 의 `VivaChat` 섹션으로도 설정할 수 있고, 시작할 때 검증됩니다.
- 저장소를 `AddVivaChat` 앞에 등록할 필요가 없어졌습니다 (`UseStore<T>()` 사용).

## 추가

- `IChatGuard` (Contracts): 방 입장 권한과 메시지 검사(욕설, 개인정보 등) 확장점
- `ISiteResolver`: 사이트(방 이름 공간)를 서버가 정하는 확장점, 기본은 `?site=` 쿼리
- `VivaChatOptions.RequireAuthentication`, `AnnouncePresence`, `MaxMessagesPerMinute`

## 사용 예

```csharp
builder.Services.AddVivaChat(o => o.DefaultSiteId = "vivakr")
    .UseStore<PgChatStore>()
    .UseSiteResolver<TenantSiteResolver>()   // {slug}.vivakr.com 의 slug 를 사이트로
    .UseGuard<YouthChatGuard>()              // 욕설, 개인정보 필터, 비공개 방
    .SignalR.AddStackExchangeRedis(redisConnection, o =>
        o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("vivakr:signalr"));
```

```csharp
public sealed class TenantSiteResolver(ITenantAccessor tenant) : ISiteResolver
{
    public string? Resolve(HubCallerContext context) => tenant.CurrentSlug; // 서버가 정하므로 위조 불가
}
```
