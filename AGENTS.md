# AGENTS.md - Viva.Chat 프로젝트 가이드

반갑네! 이 문서는 **Viva.Chat**(NuGet: `ViVaKR.Chat`, `ViVaKR.Chat.Contracts`)의 목적, 구조, 규칙, 진행 상황을 적은 표준 가이드일세.
사람과 AI 에이전트 모두 작업을 시작하기 전에 이 문서부터 읽게. 새로 결정한 것이 있으면 이 문서를 먼저 고치고 적용하게.

---

## 1. 한눈에 보기

- **무엇**: ASP.NET Core에 꽂아 쓰는 SignalR **채팅 허브 라이브러리 모음** (여러 사이트가 함께 쓰도록 설계). 사람 대 사람 채팅(`ViVaKR.Chat`)과, 그 위에 얹는 AI 채팅(`ViVaKR.AiChat`)으로 이루어진다.
- **저장소**: https://github.com/ViVaKR/Viva.Chat (MIT)
- **플랫폼**: .NET 11 (`net11.0`), C# 15.0, `Microsoft.AspNetCore.App` 공유 프레임워크. AI 패키지만 gRPC 클라이언트(`Grpc.Net.ClientFactory`)와 `AiChatServer.Contracts`가 더해진다.
- **패키지 ID와 네임스페이스**:

  | 프로젝트 폴더 | 패키지 ID | 네임스페이스 |
  |---|---|---|
  | `src/Viva.Chat.Contracts` | `ViVaKR.Chat.Contracts` | `Viva.Chat.Contracts` |
  | `src/Viva.Chat` | `ViVaKR.Chat` | `Viva.Chat` |
  | `src/Viva.Chat.Ai` | `ViVaKR.AiChat` | `Viva.Chat.Ai` |

  AI 패키지는 폴더 이름(`Viva.Chat.Ai`)과 패키지 ID(`ViVaKR.AiChat`)가 다르다. 헷갈리지 말 것. (패키지 ID는 한번 올리면 바꿀 수 없다.)
- **현재 버전**: `0.4.0-preview.1` 준비 중. 세 패키지가 한 태그로 같은 버전에 함께 나간다. (공개 API는 1.0 이전에 바뀔 수 있음, 변경 내용은 `CHANGELOG.md`)
- **AI 채팅**은 `ViVaKR.AiChat`이 맡는다 (7절). 답변을 만드는 것은 공용 gRPC 서버(`AiChatServer`)이고, 이 라이브러리는 그 답변을 SignalR로 흘려보낸다.

## 2. 생태계 맥락 (왜 이렇게 생겼는가)

- 도메인 6개(buddham.co.kr, vivabm.com, vivakr.com, kimbumjun.com, text.or.kr, writer.or.kr)가 각자 **자기 API, DB, JWT 키, 네임스페이스, Redis**를 가진다. 이 라이브러리는 그 API들이 각자 올려서 쓴다.
- 그래서 **방 이름 공간을 `{site}:{room}`으로 분리**하고, 사이트 판별과 입장/내용 검사를 확장점으로 열어 두었다.
- 인프라는 Talos Linux 위의 Kubernetes(클러스터 `vivabm`)이고, 사이트마다 네임스페이스 + Redis가 있다. 공용 PostgreSQL은 `vivabm-data` 네임스페이스.
- AI 응답은 공용 gRPC 서버 `aichat-svc.ai.svc.cluster.local:50051`(Ollama 연동)이 만들고, `ViVaKR.AiChat`이 그 서버를 부른다.

## 3. 프로젝트 구조
```text
Viva.Chat/
├── src/
│   ├── Viva.Chat.Contracts/   # 순수 계약 (프레임워크 참조 금지)
│   │   ├── ChatMessage.cs, ChatParticipant.cs
│   │   ├── IChatClient.cs     # 서버 -> 클라이언트 (ReceiveMessage, UserJoined, UserLeft)
│   │   ├── IChatStore.cs      # 저장소 추상화 (SaveAsync, GetRecentAsync)
│   │   └── IChatGuard.cs      # 입장 권한 + 메시지 검사 (ChatCaller, ChatVerdict 포함)
│   ├── Viva.Chat/             # 구현 (FrameworkReference: Microsoft.AspNetCore.App)
│   │   ├── ChatHub.cs         # Hub<IChatClient>: Join / Leave / SendMessage
│   │   ├── ChatIdentity.cs    # ChatIdentity, IChatIdentityResolver, ClaimsChatIdentityResolver
│   │   ├── ChatRoomRules.cs   # 예약 방 이름 접두어 "ai-" (ChatHub 가 거절)
│   │   ├── ISiteResolver.cs   # 사이트 판별 확장점 + QuerySiteResolver(기본)
│   │   ├── AllowAllChatGuard.cs, NullChatStore.cs   # 안전한 기본 구현
│   │   ├── VivaChatOptions.cs # 옵션 (섹션명 "VivaChat", 시작 시 검증)
│   │   ├── VivaChatBuilder.cs # AddVivaChat 이 돌려주는 빌더
│   │   └── VivaChatExtensions.cs  # AddVivaChat / MapVivaChat
│   └── Viva.Chat.Ai/          # ViVaKR.AiChat — Viva.Chat 위에 얹는 AI 채팅 허브
│       ├── AiChatHub.cs       # Hub<IAiChatClient>: Open / Send / Cancel / Close
│       ├── IAiChatClient.cs   # 서버 -> 클라이언트 이벤트 + AiSessionOpened, AiTurnAccepted
│       ├── AiChatSessionRunner.cs  # 턴 하나를 백그라운드에서 실행해 그룹으로 스트리밍
│       ├── AiTurnGate.cs      # IAiTurnGate(세션당 1턴, 취소) + InMemoryAiTurnGate(기본)
│       ├── AiChatGateway.cs   # IAiChatGateway + GrpcAiChatGateway(기본)
│       ├── AiRooms.cs         # 방 이름 규칙 (사용자 해시 포함, internal)
│       ├── AiChatDefaults.cs  # 방 접두어, AI UserId 규칙, 청크 종류 상수
│       ├── VivaAiChatOptions.cs        # 옵션 (섹션명 "VivaAiChat", 시작 시 검증)
│       ├── VivaAiChatExtensions.cs     # AddAiChat / UseAiGateway / UseAiTurnGate / MapVivaAiChat
│       └── README.md          # NuGet 패키지 설명 (영문)
├── tests/Viva.Chat.Tests/     # xunit + TestServer (식별, 입장/전송/수신, 사이트 분리, 예약 방, 가드, 전송 한도, 저장소 오류 ...)
│                              # 아직 없는 것: AI 허브 테스트 (7절 다음 후보)
├── README.md, README.kr.md    # 영문/국문 (항상 같이 고친다)
├── ref.md                     # 배포 절차 노트
├── .github/workflows/deploy.yaml  # 태그 push 시 테스트 -> pack -> NuGet push (Contracts, Chat, AiChat 순)
├── CHANGELOG.md
└── Viva.Chat.slnx
```

> 사용자 식별(`IChatIdentityResolver`, `ChatParticipant`)은 0.3, AI 채팅과 예약 방은 0.4 에서 추가되었다. 정확한 내용은 소스와 `CHANGELOG.md` 를 본다.

## 4. 설계 원칙 (지킬 것)

1. **Contracts는 순수하게**: `Viva.Chat.Contracts`에 ASP.NET Core, SignalR, EF Core, Redis 등 프레임워크/외부 패키지 참조를 추가하지 않는다. (`HubCallerContext`가 필요한 인터페이스는 본 프로젝트에 둔다.)
2. **Redis를 필수 의존성으로 만들지 않는다**: 백플레인은 호스트가 `AddVivaChat(...).SignalR.AddStackExchangeRedis(...)`로 붙인다. Presence나 사용자 단위 제한이 필요해지면 별도 패키지 `ViVaKR.Chat.Redis`로 분리한다.
3. **안전한 기본값**: 저장소 없음(Null), 가드 모두 허용, 입력 검증, 입장 후에만 전송, 연결당 분당 30건 제한.
4. **확장점은 DI로 교체**: `UseStore<T>()`, `UseGuard<T>()`, `UseSiteResolver<T>()`.
5. **옵션 패턴**: 코드 설정 -> appsettings `VivaChat` 섹션 순으로 적용(환경 설정이 코드를 덮어씀), `ValidateDataAnnotations().ValidateOnStart()`로 시작 시 실패.
6. **`site` 값은 보안 경계가 아님**: 기본 리졸버는 클라이언트가 보낸 쿼리를 믿는다. 멀티테넌트에서는 Host/JWT 클레임 기반 리졸버로 교체한다.
7. **매직 스트링 금지**: 키, 경로, 정책 이름은 상수/옵션으로 둔다.
8. **예약 방 이름**: `ai-` 로 시작하는 방은 다른 패키지(`ViVaKR.AiChat`)의 몫이다. `ChatHub` 는 `Join`/`Leave`/`SendMessage` 에서 이런 방을 거절한다. AI 패키지의 접두어는 `ChatRoomRules.ReservedPrefix` 하나만 가리키게 하고, 값을 따로 적지 않는다.
9. **AI 세션 격리는 방 이름에서 끝낸다**: 방 이름에 사용자 해시와 서버가 발급한 난수 세션 ID를 넣어서, 별도의 소유권 저장소 없이도 남의 세션을 열거나 취소할 수 없게 한다. `Open`/`Send`/`Cancel` 은 모두 JWT 에서 읽은 호출자 본인의 `UserId` 로 방 이름을 만들고, 클라이언트가 보낸 방 이름이나 사용자 ID 를 믿지 않는다.
10. **AI 발화는 이름이 아니라 `UserId` 로 구분한다** (`ai:<persona>`, `AiChatDefaults`). 사용자가 필명을 "법등" 으로 지어도 AI 로 취급되지 않는다.

## 5. 코딩 컨벤션

- 기본 생성자(Primary Constructor)와 식 본문 멤버를 기본으로 쓴다.
- `Nullable` 활성화 상태에서 경고를 무시하지 않는다.
- 주석과 사용자에게 보이는 예외 메시지(`HubException`)는 한국어로 쓴다.
- 허브의 public 메서드는 곧 클라이언트가 부를 수 있는 API다. **보조 메서드는 반드시 private**으로 둔다.
- 공개 API를 바꾸면 `CHANGELOG.md`의 "호환이 깨지는 변경" 표를 갱신한다.

## 6. 자주 쓰는 명령

```bash
dotnet build
dotnet test --solution Viva.Chat.slnx -c Release            # CI 와 같은 명령
dotnet pack Viva.Chat.slnx -c Release -o ./out -p:Version=0.4.0-preview.1
```

### 릴리스 (자세한 절차와 실패 대응은 `ref.md`)

- **코드 push 로는 배포되지 않는다.** `v` + 숫자로 시작하는 태그를 push 하면 `.github/workflows/deploy.yaml` 이 `dotnet test` -> `dotnet pack` -> NuGet push(OIDC 임시 키, **Contracts -> Chat -> AiChat 순**, 셋 중 하나라도 파일이 없으면 아무것도 올리지 않음, `--skip-duplicate`)를 실행한다.
- 배포 확인: `nugetcheck ViVaKR.Chat`, `nugetcheck ViVaKR.Chat.Contracts`, `nugetcheck ViVaKR.AiChat`, 빈 `dotnet new web` 프로젝트에 `--prerelease` 로 설치해 빌드. pack 직후에는 `unzip -p ./out/ViVaKR.AiChat.$ver.nupkg ViVaKR.AiChat.nuspec` 로 의존성(`ViVaKR.Chat`, `AiChatServer.Contracts`)을 확인한다.
- 빠른 동작 확인: `Invoke-RestMethod -Method Post "http://localhost:<port>/hubs/chat/negotiate?negotiateVersion=1"` (AI 허브 `/hubs/ai` 는 로그인이 필요하니 토큰 없이 부르면 401 이 정상)
- 테스트가 실패하면 배포되지 않는다.
- 버전은 태그에서 정해지므로 csproj 의 버전을 손으로 고치지 않는다 (`v0.3.0-preview.1` -> `0.3.0-preview.1`).
- 같은 버전은 NuGet 에 다시 올릴 수 없다. 일부만 올라간 뒤에 코드를 고쳤다면 버전을 올린다.
- 태그 오타 방지: PowerShell 에서 `$ver = '0.3.0-preview.1'` 변수로 `git tag -a "v$ver"`, `git push origin "v$ver"` 를 쓴다.

## 7. 로드맵

## 7. 로드맵

- **0.2.0-preview.1** (배포됨): 퇴장 알림, `IChatGuard`, `ISiteResolver`, 빌더, 옵션 검증, 전송 속도 제한, `ChatMessage.UserId`
- **0.3.0-preview.1** (배포됨): JWT 클레임 기반 사용자 식별(`IChatIdentityResolver`), `ChatParticipant`와 아바타, `CorsPolicy`, `MapVivaChat` 연결 옵션, 오류 처리 개선, 테스트 프로젝트와 "테스트 통과 후 배포" 워크플로
- **0.4.0-preview.1 (준비 중)**: AI 채팅 패키지 `ViVaKR.AiChat` 추가, 예약 방 이름 접두어(`ChatRoomRules`, `ai-`), 배포 워크플로가 세 패키지(Contracts -> Chat -> AiChat)를 올림
- **다음 후보**: 이력 페이징(커서), `ViVaKR.Chat.Redis`(Presence, 사용자 단위 제한, AI 턴 게이트의 Redis 구현), `ClientTimeoutInterval`/KeepAlive 옵션 노출, AI 허브 테스트(사용자 격리, 세션당 1턴, 취소), 연결이 끊기면 답변을 취소하는 옵션, AI 턴의 사용자 단위 제한
- **`ViVaKR.AiChat` 설계 (0.4 에 반영)**:
  - 허브 `/hubs/ai`: `Open`(세션 열기, ID 는 서버 발급) / `Send`(질문. 바로 돌아오고 답변은 이벤트로 스트리밍) / `Cancel` / `Close`. 이벤트는 `ReceiveMessage`, `ReceiveChunk`, `StreamCompleted`, `StreamFailed`, `StreamCancelled`. 목록·이력 조회는 호스트의 REST 로 제공한다.
  - 로그인한 사용자만 쓴다. 세션은 사용자별로 격리된다 (방 이름 `ai-<사용자 해시 16자>-<세션 ID 32자>`, 4절 8~9번).
  - 같은 틀을 공유한다: `AddVivaChat(...).AddAiChat(...)`, `ISiteResolver`, `IChatGuard`, `IChatIdentityResolver`, `IChatStore`. AI 대화 이력도 사람 채팅과 같은 `IChatStore` 에 `ai-` 방으로 저장한다.
  - 응답은 gRPC 서버(`AiChatServer`)에서 받고 RAG 는 각 사이트 API 에서 처리한다. GPU 한 장을 6개 사이트가 공유하므로 전역 큐는 gRPC 서버 쪽 몫이다.
  - 세션당 진행 중인 턴은 1개이고 `IAiTurnGate` 가 지킨다. **기본 구현은 메모리라서 레플리카가 1개일 때만 보장된다.** 여럿이면 `UseAiTurnGate<T>()` 로 공유 저장소(Redis 등) 구현을 붙인다.
  - `Close` 나 연결 끊김은 진행 중인 답변을 멈추지 않는다 (끝까지 생성해서 저장). 멈추려면 `Cancel`. 취소하면 지금까지 받은 답을 저장하고(`SavePartialOnCancel`), 실패한 턴은 저장하지 않는다.
  - `MaxTurnsPerMinute` 는 연결 단위 제한이다. 사용자 단위 제한은 호스트(`IChatGuard`, `IAiTurnGate` 구현)의 몫이다.
- **1.0.0 승격 조건 (예정)**: `AiChatServer.Contracts` 가 정식 버전이 되고(프리릴리스 의존성은 정식 패키지에 둘 수 없다), buddham 에서 브라우저까지 완주한 뒤 `1.0.0-rc.1` 을 거쳐 `1.0.0`.
- **vivakr(청소년 대상 사이트 생성 플랫폼)** 때문에 가드(욕설/개인정보 필터, 비공개 방)와 테넌트 기반 사이트 리졸버가 중요하다.

## 8. AI 에이전트를 위한 작업 수칙

1. 이 문서와 `CHANGELOG.md`를 먼저 읽고, 위 설계 원칙(특히 Contracts 순수성, Redis 비의존)을 어기는 변경은 제안하되 임의로 적용하지 않는다.
2. 호환이 깨지는 변경은 반드시 `CHANGELOG.md`와 두 README에 반영한다.
3. 빌드할 수 없는 환경이면 "빌드하지 못했다"고 분명히 밝힌다.
4. 새 확장점을 만들 때는 기본 구현(안전한 기본값)과 `Use...<T>()` 빌더 메서드를 함께 만든다.
5. 비밀값(토큰, 키)을 코드나 README 예제에 넣지 않는다.
6. `ViVaKR.AiChat` 의 `src/Viva.Chat.Ai/README.md` 는 NuGet 패키지 페이지에 그대로 올라간다. AI 쪽 공개 API 나 동작을 바꾸면 이 파일도 함께 고친다.
7. 문서에 적은 이름(허브 메서드, 이벤트, 확장점, 옵션)은 실제 코드의 이름과 같아야 한다. 옛 이름이 남아 있으면 코드를 기준으로 문서를 고친다.
