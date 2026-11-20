# AGENTS.md - Viva.Chat 프로젝트 가이드

반갑네! 이 문서는 **Viva.Chat**(NuGet: `ViVaKR.Chat`, `ViVaKR.Chat.Contracts`)의 목적, 구조, 규칙, 진행 상황을 적은 표준 가이드일세.
사람과 AI 에이전트 모두 작업을 시작하기 전에 이 문서부터 읽게. 새로 결정한 것이 있으면 이 문서를 먼저 고치고 적용하게.

---

## 1. 한눈에 보기

- **무엇**: ASP.NET Core에 꽂아 쓰는 SignalR **사람 대 사람 채팅 허브** 라이브러리 (여러 사이트가 함께 쓰도록 설계)
- **저장소**: https://github.com/ViVaKR/Viva.Chat (MIT)
- **플랫폼**: .NET 10 (`net10.0`), C# 12+, `Microsoft.AspNetCore.App` 공유 프레임워크만 사용
- **패키지 ID와 네임스페이스**: 패키지는 `ViVaKR.Chat` / `ViVaKR.Chat.Contracts`, 네임스페이스는 `Viva.Chat` / `Viva.Chat.Contracts`
- **현재 버전**: `0.3.0-preview.1` 준비 중 (공개 API는 1.0 이전에 바뀔 수 있음, 변경 내용은 `CHANGELOG.md`)
- **AI 채팅은 이 패키지의 범위가 아님**: 별도 패키지 `ViVaKR.AiChat`으로 만들 예정 (7절 참조)

## 2. 생태계 맥락 (왜 이렇게 생겼는가)

- 도메인 6개(buddham.co.kr, vivabm.com, vivakr.com, kimbumjun.com, text.or.kr, writer.or.kr)가 각자 **자기 API, DB, JWT 키, 네임스페이스, Redis**를 가진다. 이 라이브러리는 그 API들이 각자 올려서 쓴다.
- 그래서 **방 이름 공간을 `{site}:{room}`으로 분리**하고, 사이트 판별과 입장/내용 검사를 확장점으로 열어 두었다.
- 인프라는 Talos Linux 위의 Kubernetes(클러스터 `vivabm`)이고, 사이트마다 네임스페이스 + Redis가 있다. 공용 PostgreSQL은 `vivabm-data` 네임스페이스.
- AI 응답은 공용 gRPC 서버 `aichat-svc.ai.svc.cluster.local:50051`(Ollama 연동)이 맡는다. 이 라이브러리와는 별개다.

## 3. 프로젝트 구조

```text
Viva.Chat/
├── src/
│   ├── Viva.Chat.Contracts/   # 순수 계약 (프레임워크 참조 금지)
│   │   ├── ChatMessage.cs     # record, 끝에 UserId(선택) 있음
│   │   ├── IChatClient.cs     # 서버 -> 클라이언트 (ReceiveMessage, UserJoined, UserLeft)
│   │   ├── IChatStore.cs      # 저장소 추상화 (SaveAsync, GetRecentAsync)
│   │   └── IChatGuard.cs      # 입장 권한 + 메시지 검사 (ChatCaller, ChatVerdict 포함)
│   └── Viva.Chat/             # 구현 (FrameworkReference: Microsoft.AspNetCore.App)
│       ├── ChatHub.cs         # Hub<IChatClient>: Join / Leave / SendMessage
│       ├── ISiteResolver.cs   # 사이트 판별 확장점 + QuerySiteResolver(기본)
│       ├── AllowAllChatGuard.cs, NullChatStore.cs   # 안전한 기본 구현
│       ├── VivaChatOptions.cs # 옵션 (섹션명 "VivaChat", 시작 시 검증)
│       ├── VivaChatBuilder.cs # AddVivaChat 이 돌려주는 빌더
│       └── VivaChatExtensions.cs  # AddVivaChat / MapVivaChat
├── tests/Viva.Chat.Tests/     # xunit + TestServer (식별, 입장/전송/수신, 사이트 분리, 가드, 전송 한도, 저장소 오류 ...)
├── README.md, README.kr.md    # 영문/국문 (항상 같이 고친다)
├── ref.md                     # 배포 절차 노트
├── .github/workflows/deploy.yaml  # 태그 push 시 테스트 -> pack -> NuGet push
├── CHANGELOG.md
└── Viva.Chat.slnx
```

> 0.3 에서 `IChatIdentityResolver`(JWT 클레임 기반 사용자 식별)와 `ChatParticipant`(ID, 이름, 아바타)가 추가되었다. 정확한 파일 위치는 소스와 `CHANGELOG.md` 를 본다.

## 4. 설계 원칙 (지킬 것)

1. **Contracts는 순수하게**: `Viva.Chat.Contracts`에 ASP.NET Core, SignalR, EF Core, Redis 등 프레임워크/외부 패키지 참조를 추가하지 않는다. (`HubCallerContext`가 필요한 인터페이스는 본 프로젝트에 둔다.)
2. **Redis를 필수 의존성으로 만들지 않는다**: 백플레인은 호스트가 `AddVivaChat(...).SignalR.AddStackExchangeRedis(...)`로 붙인다. Presence나 사용자 단위 제한이 필요해지면 별도 패키지 `ViVaKR.Chat.Redis`로 분리한다.
3. **안전한 기본값**: 저장소 없음(Null), 가드 모두 허용, 입력 검증, 입장 후에만 전송, 연결당 분당 30건 제한.
4. **확장점은 DI로 교체**: `UseStore<T>()`, `UseGuard<T>()`, `UseSiteResolver<T>()`.
5. **옵션 패턴**: 코드 설정 -> appsettings `VivaChat` 섹션 순으로 적용(환경 설정이 코드를 덮어씀), `ValidateDataAnnotations().ValidateOnStart()`로 시작 시 실패.
6. **`site` 값은 보안 경계가 아님**: 기본 리졸버는 클라이언트가 보낸 쿼리를 믿는다. 멀티테넌트에서는 Host/JWT 클레임 기반 리졸버로 교체한다.
7. **매직 스트링 금지**: 키, 경로, 정책 이름은 상수/옵션으로 둔다.

## 5. 코딩 컨벤션

- 기본 생성자(Primary Constructor)와 식 본문 멤버를 기본으로 쓴다.
- `Nullable` 활성화 상태에서 경고를 무시하지 않는다.
- 주석과 사용자에게 보이는 예외 메시지(`HubException`)는 한국어로 쓴다.
- 허브의 public 메서드는 곧 클라이언트가 부를 수 있는 API다. **보조 메서드는 반드시 private**으로 둔다.
- 공개 API를 바꾸면 `CHANGELOG.md`의 "호환이 깨지는 변경" 표를 갱신한다.

## 6. 자주 쓰는 명령

```bash
dotnet build
dotnet test Viva.Chat.slnx -c Release                       # CI 와 같은 명령
dotnet pack Viva.Chat.slnx -c Release -o ./out -p:Version=0.3.0-preview.1
```

### 릴리스 (자세한 절차와 실패 대응은 `ref.md`)

- **코드 push 로는 배포되지 않는다.** `v` + 숫자로 시작하는 태그를 push 하면 `.github/workflows/deploy.yaml` 이 `dotnet test` -> `dotnet pack` -> NuGet push(OIDC 임시 키, Contracts 먼저, `--skip-duplicate`)를 실행한다.
- 테스트가 실패하면 배포되지 않는다.
- 버전은 태그에서 정해지므로 csproj 의 버전을 손으로 고치지 않는다 (`v0.3.0-preview.1` -> `0.3.0-preview.1`).
- 같은 버전은 NuGet 에 다시 올릴 수 없다. 일부만 올라간 뒤에 코드를 고쳤다면 버전을 올린다.
- 태그 오타 방지: PowerShell 에서 `$ver = '0.3.0-preview.1'` 변수로 `git tag -a "v$ver"`, `git push origin "v$ver"` 를 쓴다.
- 배포 확인: `nugetcheck ViVaKR.Chat`, `nugetcheck ViVaKR.Chat.Contracts`, 빈 `dotnet new web` 프로젝트에 `--prerelease` 로 설치해 빌드.
- 빠른 동작 확인: `Invoke-RestMethod -Method Post "http://localhost:<port>/hubs/chat/negotiate?negotiateVersion=1"`

## 7. 로드맵

- **0.2.0-preview.1**: 퇴장 알림, `IChatGuard`, `ISiteResolver`, 빌더, 옵션 검증, 전송 속도 제한, `ChatMessage.UserId`
- **0.3.0-preview.1 (준비 중)**: JWT 클레임 기반 사용자 식별(`IChatIdentityResolver`), `ChatParticipant`와 아바타, `CorsPolicy`, `MapVivaChat` 연결 옵션, 오류 처리 개선, 테스트 프로젝트와 "테스트 통과 후 배포" 워크플로
- **다음 후보**: 이력 페이징(커서), `ViVaKR.Chat.Redis`(Presence, 사용자 단위 제한), `ClientTimeoutInterval`/KeepAlive 옵션 노출
- **`ViVaKR.AiChat` (별도 패키지)**: 세션 방식 AI 채팅. 같은 틀(빌더, 옵션 검증, `ISiteResolver`, `IChatGuard`)을 공유하고, 허브는 `Open` / `Send`(서버 스트리밍) / `Cancel`, 목록/이력은 REST. 응답은 gRPC 서버에서 받고 RAG는 각 사이트 API에서 처리한다. 세션당 진행 중인 턴은 1개(Redis 락). GPU 한 장을 6개 사이트가 공유하므로 전역 큐는 gRPC 서버 쪽 몫이다.
- **vivakr(청소년 대상 사이트 생성 플랫폼)** 때문에 가드(욕설/개인정보 필터, 비공개 방)와 테넌트 기반 사이트 리졸버가 중요하다.

## 8. AI 에이전트를 위한 작업 수칙

1. 이 문서와 `CHANGELOG.md`를 먼저 읽고, 위 설계 원칙(특히 Contracts 순수성, Redis 비의존)을 어기는 변경은 제안하되 임의로 적용하지 않는다.
2. 호환이 깨지는 변경은 반드시 `CHANGELOG.md`와 두 README에 반영한다.
3. 빌드할 수 없는 환경이면 "빌드하지 못했다"고 분명히 밝힌다.
4. 새 확장점을 만들 때는 기본 구현(안전한 기본값)과 `Use...<T>()` 빌더 메서드를 함께 만든다.
5. 비밀값(토큰, 키)을 코드나 README 예제에 넣지 않는다.
