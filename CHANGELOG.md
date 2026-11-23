## [0.4.0-preview.1] - 2026-11-23

AI 채팅 허브를 담은 새 패키지 `ViVaKR.AiChat` 이 추가된 프리뷰입니다. Contracts, Chat, AiChat 세 패키지가 같은 버전으로 함께 배포됩니다. 호환이 깨지는 변경이 하나 있고, 공개 API 는 1.0 이전에 바뀔 수 있습니다.

### 추가
- 새 패키지 `ViVaKR.AiChat` (`src/Viva.Chat.Ai`): `ViVaKR.Chat` 위에 얹는 사람 ↔ AI 채팅 허브. 공용 gRPC 서버(`AiChatServer`)의 답변을 SignalR 로 스트리밍합니다.
- 등록과 매핑: `AddVivaChat(...).AddAiChat(...)`, `app.MapVivaAiChat()` (`/hubs/ai`, 로그인한 사용자만). CORS 는 사람 채팅과 같은 규칙을 씁니다.
- 허브 메서드 `Open` / `Send` / `Cancel` / `Close`, 클라이언트 이벤트 `ReceiveMessage` / `ReceiveChunk` / `StreamCompleted` / `StreamFailed` / `StreamCancelled`
- 확장점: `IAiChatGateway`(`UseAiGateway<T>()`), `IAiTurnGate`(`UseAiTurnGate<T>()`)
- 옵션 `VivaAiChatOptions` (appsettings `VivaAiChat` 섹션): `ServerUrl`(필수), `DefaultPersona`, `AiDisplayName`, `HubPath`, `EmitThinking`, `MaxPromptLength`, `ContextHistoryCount`, `HistoryOnOpen`, `MaxTurnsPerMinute`(6), `SavePartialOnCancel`(true), `RequestTimeoutSeconds`(300)
- 사용자가 중단하면 지금까지 받은 답을 저장합니다(`SavePartialOnCancel`로 끌 수 있음). 모델의 사고 과정(thinking)과 실패한 턴은 저장하지 않습니다.
- `ChatRoomRules` (`ViVaKR.Chat`): 다른 패키지가 쓰는 방 이름 접두어(`ai-`)를 예약합니다. `ReservedPrefix`, `IsReserved(room)`
- `ViVaKR.AiChat` 은 `ViVaKR.Chat`, `AiChatServer.Contracts`(0.1.0-preview.2), `Grpc.Net.ClientFactory` 에 의존하고, `dotnet add package ViVaKR.AiChat --prerelease` 한 줄이면 함께 설치됩니다.
- 세션은 사용자별로 분리됩니다. 세션 ID 는 서버가 발급하는 난수이고, 방 이름(`ai-` + 사용자 해시 + 세션 ID)에 사용자 해시가 들어가서 다른 사용자의 세션을 열거나 `Cancel` 할 수 없습니다. 한 연결에서 열 수 있는 세션은 20개입니다.
- 로그인하지 않은 연결은 거절됩니다 (`/hubs/ai`).

### 바뀜
- `ChatHub` 는 `ai-` 로 시작하는 방(대소문자 무시)의 입장·퇴장·전송을 거절합니다. AI 대화 이력을 사람 채팅에서 읽지 못하게 하기 위해서입니다.
- 허브 경로 규약에 AI 채팅 `/hubs/ai` 를 추가했습니다.
- 배포 워크플로가 세 패키지(Contracts → Chat → AiChat)를 올리고, 하나라도 없으면 아무것도 올리지 않습니다.
- 대상 프레임워크를 `net10.0` → `net11.0` 으로 올렸습니다. ‹다중 대상으로 하면: `net10.0` 과 `net11.0` 을 함께 지원합니다›
- 테스트가 xunit v3 + Microsoft Testing Platform 으로 바뀌었습니다 (`dotnet test --solution`, 저장소 루트의 `global.json` 필요). 라이브러리 사용자에게는 영향이 없습니다.

### 호환이 깨지는 변경

| 변경 | 영향 | 고치는 법 |
|------|------|-----------|
| `ai-` 로 시작하는 방 이름 예약 | 기존에 `ai-news` 같은 방을 쓰던 호스트는 `Join`/`Leave`/`SendMessage` 가 거절됨 | 방 이름을 `ai-` 로 시작하지 않게 변경 |
| 대상 프레임워크 `net10.0` → `net11.0` (다중 대상이면 이 줄은 생략) | .NET 10 프로젝트에서 0.4 이후를 설치할 수 없음 (`NU1202`) | 프로젝트를 .NET 11 로 올리거나 0.3.0-preview.1 을 유지 |

### 알아 둘 점
- 기본 턴 게이트(`InMemoryAiTurnGate`)는 프로세스 메모리에서만 동작합니다. "세션당 진행 중인 턴 1개"와 `Cancel` 은 **레플리카가 1개일 때만** 보장되며, 여럿이면 `UseAiTurnGate<T>()` 로 공유 저장소 기반 구현을 연결해야 합니다. (Redis 구현은 아직 제공하지 않습니다.)
- `MaxTurnsPerMinute` 는 연결 단위 제한이고, 사용자 단위 제한은 호스트의 몫입니다.
- `ViVaKR.AiChat` 은 프리릴리스 패키지(`AiChatServer.Contracts 0.1.0-preview.1`)에 의존하므로, 1.0.0 정식 승격은 그 패키지가 정식이 된 뒤에 가능합니다.
- `Close` 와 연결 끊김은 진행 중인 답변을 멈추지 않습니다. 답변은 끝까지 생성되어 저장되며(최대 `RequestTimeoutSeconds`), 멈추려면 `Cancel` 을 부르세요.


## [0.3.0-preview.1] - 2026-11-20

호환이 깨지는 변경이 있는 프리뷰입니다. 1.0.0 이전이라서 계약을 지금 바로잡습니다.

### 추가
- `IChatIdentityResolver` 와 기본 구현(JWT 클레임). `UseIdentityResolver<T>()` 로 교체합니다.
- 옵션 `UserIdClaim`(`sub`), `DisplayNameClaim`(`preferred_username`), `AvatarClaim`(`picture`), `MaxDisplayNameLength`(50)
- `ChatParticipant(UserId, Name, Avatar)`
- `ChatMessage.Avatar` (보낸 시점의 스냅샷, 선택)
- `ChatCaller.Avatar`, `ChatCaller.Principal` (가드가 나이·역할 같은 클레임을 직접 볼 수 있음)

### 추가 (3단계)
- 테스트 프로젝트 `tests/Viva.Chat.Tests` (xunit, `TestServer`): 식별, 입장/전송/수신, 입장·퇴장 알림, 사이트 분리와 거절, 가드, 전송 한도, 저장소 오류, `MapVivaChat` 연결 옵션을 검증합니다.
- 배포 워크플로: 태그가 달리면 `dotnet test` 를 먼저 돌리고, 통과해야 pack/push 합니다. push 에 `--skip-duplicate` 를 추가했고, 태그 트리거를 `v[0-9]*` 로 좁혔습니다.

### 추가 (2단계)
- 옵션 `CorsPolicy`: 호스트의 CORS 정책 이름을 그대로 허브에 씁니다. 지정하면 `AllowedOrigins` 로 만드는 자체 정책(`VivaChat`)은 만들지 않습니다.
- `MapVivaChat(Action<HttpConnectionDispatcherOptions>? configure)` 오버로드 (전송 방식, 버퍼 크기, `AllowStatefulReconnects` 등). 기존 `MapVivaChat()` 은 그대로 동작합니다.
- 허브에 `ILogger` 를 주입해서 연결 거절, 저장소/가드 오류를 기록합니다 (메시지 본문은 기록하지 않음).

### 바뀜 (2단계)
- 저장소와 가드의 예외를 한국어 `HubException` 으로 바꿔서 보냅니다. 전에는 "예기치 않은 오류" 로만 보였습니다.
- **저장에 실패한 메시지는 방에 전달하지 않습니다.** 전에는 저장 예외가 그대로 올라가서 같은 결과였지만, 이제 사용자에게 이유가 전달되고 로그가 남습니다.
- `Join` 은 이력을 읽은 뒤에 입장 알림을 보냅니다. 이력을 읽지 못하면 입장을 되돌리고 알림은 나가지 않습니다.
- 허브 경로 규약을 `/hubs/<이름>` 으로 문서화했습니다 (기본값 `/hubs/chat` 은 그대로).

### 바뀜 (1단계)
- 표시 이름을 `Identity.Name` 이 아니라 필명 클레임에서 읽습니다. `sub` 를 `NameClaimType` 으로 쓰는 호스트에서 이름이 GUID 로 나오던 문제가 사라집니다.
- 사용자 ID 를 `Context.UserIdentifier` 대신 `sub` → `NameIdentifier` 순서로 읽습니다.
- 표시 이름은 제어문자를 제거하고 50자로 자릅니다. 필명이 없으면 `user-xxxx`, 손님은 `guest-xxxx` 입니다.

### 호환이 깨지는 변경

| 변경 | 영향 | 고치는 법 |
|------|------|-----------|
| `IChatClient.UserJoined(string room, string user)` → `UserJoined(string room, ChatParticipant user)` | 클라이언트가 받는 두 번째 인자가 문자열에서 객체 `{ userId, name, avatar }` 로 바뀜 | 클라이언트 핸들러에서 `user.name` 을 쓰도록 수정 |
| `IChatClient.UserLeft` 도 같음 | 위와 같음 | 위와 같음 |
| `ChatMessage` 끝에 `Avatar` 추가 | 이름 있는 인자로 만들거나 `with` 를 쓰면 영향 없음. 위치 인자 8개로 `new` 하거나 분해(deconstruct) 하던 코드는 수정 필요 | `Avatar` 를 채우거나 생략 |
| `ChatCaller` 에 `Avatar`, `Principal` 추가 | 3개 인자로 만들던 코드는 그대로 동작. 분해하던 코드는 수정 필요 | 필요한 값만 꺼내서 사용 |
| `ChatHub` 생성자에 `IChatIdentityResolver` 추가 | 허브를 직접 상속하거나 생성하던 코드만 영향 | `AddVivaChat` 만 쓰면 변경 없음 |

---

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
