# 관계도 모음 (Mermaid)

지금까지 이야기한 것을 그림으로 정리한 문서야. VS Code라면 Mermaid 미리보기 확장이 있어야 그림으로 보이고, GitHub에 올리면 그대로 그려져.

1. 전체 구조: 사이트 6개와 AI 서버, M1
2. 사이트 API 안의 허브 두 개 (사람 채팅 / AI 채팅)
3. AI 질문 한 번이 지나가는 길 (순서도)
4. Viva.Chat 라이브러리 구조 (클래스)
5. 사용자 식별 흐름 (JWT → 이름, 아바타)
6. 방 입장과 메시지 전송 (0.3 기준 순서도)
7. 이번 수정의 지도 (1~6단계)

---

## 1. 전체 구조

```mermaid
flowchart LR
    subgraph U["사용자 (브라우저, Angular)"]
        B1["buddham.co.kr"]
        B2["vivabm.com"]
        B3["vivakr / kimbumjun / text / writer"]
    end

    NG["nginx (외부 우분투)<br/>/hubs/* 에 WebSocket 설정"]

    subgraph K["Talos Kubernetes (M1 위의 VM)"]
        subgraph NB["네임스페이스 buddham"]
            API1["buddham-api<br/>ChatHub + AiChatHub"]
            R1[("buddham-redis")]
        end
        subgraph NV["네임스페이스 vivabm"]
            API2["vivabm-api<br/>ChatHub + AiChatHub"]
            R2[("vivabm-redis")]
        end
        NX["나머지 4개 사이트 API<br/>(같은 구조, 아직 미구성)"]
        subgraph NA["네임스페이스 ai (공용)"]
            AI["aichat-server (gRPC)<br/>페르소나, Ollama 호출"]
        end
        PG[("PostgreSQL<br/>vivabm-data")]
    end

    OL["Ollama (M1 호스트, GPU)<br/>dhamma-chat / bge-m3"]

    B1 & B2 & B3 -->|HTTPS, WebSocket, JWT| NG
    NG --> API1 & API2 & NX
    API1 --> R1
    API2 --> R2
    API1 & API2 & NX -->|"gRPC :50051"| AI
    API1 & API2 -.->|"DB 단계에서 연결"| PG
    AI -->|"HTTP :11434"| OL
```

**핵심**: 허브는 사이트마다 따로 있고(인증, 데이터가 사이트별), AI 서버와 GPU는 하나를 모두가 나눠 써.

---

## 2. 사이트 API 안의 허브 두 개

```mermaid
flowchart TB
    subgraph SITE["사이트 API 프로세스 (예: buddham-api)"]
        direction TB
        subgraph H1["ChatHub  /hubs/chat  (사람 ↔ 사람)"]
            direction TB
            L1["ViVaKR.Chat 패키지"]
            G1["IChatGuard<br/>(방 권한, 내용 검사)"]
            S1["IChatStore<br/>(메시지 저장)"]
            I1["IChatIdentityResolver<br/>(JWT → 필명, 아바타)"]
            L1 --- G1 & S1 & I1
        end
        subgraph H2["AiChatHub  /hubs/ai  (사람 ↔ 법등)"]
            direction TB
            L2["ViVaKR.AiChat 패키지<br/>Open / Send / Cancel / Close"]
            R2["AiChatSessionRunner<br/>(턴을 백그라운드로 실행, 그룹에 스트리밍)"]
            C2["IAiChatGateway<br/>(기본: gRPC)"]
            K2["IAiTurnGate<br/>(세션당 턴 1개, 취소 · 기본은 메모리)"]
            L2 --- R2 & K2
            R2 --- C2
        end
        SHARED["두 허브가 같이 쓰는 확장점<br/>IChatStore · IChatGuard · ISiteResolver · IChatIdentityResolver"]
        JWT["JWT 인증 (OnMessageReceived 에서<br/>/hubs 경로는 access_token 쿼리 허용)"]
        JWT --> H1 & H2
        L1 & L2 -.-> SHARED
    end

    C2 ==>|gRPC| AIS["aichat-server (공용)"]
    S1 -.-> DB[("사이트 DB")]
    L2 -.->|"AI 대화 이력도 같은 저장소 (ai- 방)"| DB

```

**읽는 법**: 위 칸(ChatHub)은 사람끼리 대화방이고 패키지로 공유해. 아래 칸(AiChatHub)은 법등과의 1:1 대화고, AI 서버를 부르는 것만 다르고 틀은 같아. 두 허브는 섞지 않아.

---

## 3. AI 질문 한 번이 지나가는 길

```mermaid
sequenceDiagram
    autonumber
    actor 도반 as 도반 (브라우저)
    participant NG as nginx
    participant HUB as AiChatHub (사이트 API)
    participant GATE as IAiTurnGate (메모리)
    participant ST as IChatStore (사이트 DB)
    participant RUN as AiChatSessionRunner
    participant AI as aichat-server (공용)
    participant OL as Ollama (M1 GPU)

    도반->>NG: WebSocket 연결 (?access_token=JWT)
    NG->>HUB: 업그레이드 (101)
    Note over HUB: 로그인하지 않은 연결은 거절<br/>(손님은 AI 를 쓸 수 없음)

    도반->>HUB: Open(sessionId?)
    HUB->>ST: 이력 읽기 (방 = ai-사용자해시-세션ID)
    HUB-->>도반: { sessionId, history }

    도반->>HUB: Send(sessionId, 질문)
    Note over HUB: 사용자 ID 는 JWT 에서만 꺼냄<br/>(클라이언트가 보내지 않음)<br/>길이, 속도, IChatGuard 검사
    HUB->>GATE: TryBeginAsync(세션)
    alt 이미 답변 진행 중
        HUB-->>도반: 오류 (끝난 뒤에 다시 보내세요)
    else 자리 확보
        HUB->>ST: 질문 저장
        HUB-->>도반: ReceiveMessage (에코, 같은 사용자의 모든 탭)
        HUB->>RUN: Start(턴) 기다리지 않음
        HUB-->>도반: { userMessageId, streamId } 바로 반환
        RUN->>ST: 이전 대화 읽기 (맥락)
        RUN->>AI: StreamChat(페르소나, 이력 + 새 질문)
        AI->>OL: /api/chat (stream)
        loop 토큰이 생성되는 동안
            OL-->>AI: 조각
            AI-->>RUN: ChatChunk (content / thinking)
            RUN-->>도반: ReceiveChunk (streamId, kind, delta)
        end
        alt 정상 완료
            RUN->>ST: 답변 저장 (Id = streamId)
            RUN-->>도반: StreamCompleted
        else 실패
            RUN-->>도반: StreamFailed (저장하지 않음)
        end
        RUN->>GATE: 턴 반납
    end
    opt 사용자가 중지 버튼
        도반->>HUB: Cancel(sessionId)
        HUB->>GATE: Cancel(세션)
        GATE-->>RUN: 취소 신호
        RUN-->>AI: 취소 전파
        AI-->>OL: 생성 중단 (GPU 해방)
        RUN->>ST: 지금까지 받은 답 저장 (SavePartialOnCancel)
        RUN-->>도반: StreamCancelled (partial)
    end
```

**읽는 법**: `Send` 는 바로 돌아오고 답변은 이벤트로 흘러와. 중지는 `Cancel` 로만 해. 탭을 닫거나 `Close` 를 불러도 답변은 끝까지 만들어져서 저장돼(GPU 는 최대 300초까지 써). 취소하면 지금까지 받은 답이 저장되고(기본값), 실패한 턴은 저장하지 않아.## 3. AI 질문 한 번이 지나가는 길

---

## 4. Viva.Chat 라이브러리 구조 (0.3)

`★` 표시는 0.3에서 새로 생기거나 바뀐 것이야.

```mermaid
classDiagram
    direction LR

    namespace Contracts {
        class ChatMessage {
            <<record>>
            string Id
            string SiteId
            string Room
            string User
            string Text
            DateTimeOffset SentAt
            string UserId
            string Avatar ★
        }
        class ChatParticipant {
            <<record ★>>
            string UserId
            string Name
            string Avatar
        }
        class IChatClient {
            <<interface>>
            ReceiveMessage(ChatMessage)
            ★ UserJoined(room, ChatParticipant)
            ★ UserLeft(room, ChatParticipant)
        }
        class IChatStore {
            <<interface>>
            SaveAsync(ChatMessage)
            GetRecentAsync(site, room, take)
        }
        class IChatGuard {
            <<interface>>
            CanJoinAsync(ChatCaller, site, room)
            InspectAsync(ChatCaller, site, room, text)
        }
        class ChatCaller {
            <<record struct>>
            string UserId
            string DisplayName
            bool IsAuthenticated
            string Avatar ★
            ClaimsPrincipal Principal ★
        }
    }

    namespace ViVaKR_Chat {
        class ChatHub {
            Join(room)
            Leave(room)
            SendMessage(room, text)
        }
        class IChatIdentityResolver {
            <<interface ★>>
            Resolve(HubCallerContext) ChatIdentity
        }
        class ClaimsChatIdentityResolver {
            <<기본 구현 ★>>
        }
        class ChatIdentity {
            <<record ★>>
            ToParticipant()
            ToCaller()
        }
        class ISiteResolver {
            <<interface>>
            Resolve(HubCallerContext)
        }
        class VivaChatOptions {
            UserIdClaim ★
            DisplayNameClaim ★
            AvatarClaim ★
            MaxDisplayNameLength ★
        }
        class VivaChatBuilder {
            UseStore~T~()
            UseGuard~T~()
            UseSiteResolver~T~()
            ★ UseIdentityResolver~T~()
        }
    }

    ChatHub --> IChatStore : 저장
    ChatHub --> IChatGuard : 검사
    ChatHub --> ISiteResolver : 사이트 판별
    ChatHub --> IChatIdentityResolver : 누구인가 ★
    ChatHub ..> IChatClient : 방 전체에 전달
    IChatIdentityResolver <|.. ClaimsChatIdentityResolver
    ClaimsChatIdentityResolver --> VivaChatOptions : 클레임 이름
    IChatIdentityResolver ..> ChatIdentity : 만듦
    ChatIdentity ..> ChatParticipant
    ChatIdentity ..> ChatCaller
    IChatClient ..> ChatMessage
    IChatClient ..> ChatParticipant
    IChatGuard ..> ChatCaller
    VivaChatBuilder ..> ChatHub : 호스트가 교체
```

---

## 5. 사용자 식별 흐름 (JWT → 이름, 아바타)

```mermaid
flowchart LR
    subgraph T["로그인 때 만든 JWT (TokenService)"]
        c1["sub / nameid = 사용자 GUID"]
        c2["preferred_username = 필명 (Pseudonym)"]
        c3["picture = 아바타 경로"]
        c4["role = 역할들"]
        c5["name = 실명(법명)"]
        c6["email = 이메일"]
    end

    RES["ClaimsChatIdentityResolver<br/>(연결할 때 한 번만 실행)"]

    c1 -->|"sub, 없으면 NameIdentifier"| RES
    c2 -->|"필명만 사용 (50자, 제어문자 제거)"| RES
    c3 -->|"500자 제한, 비면 null"| RES
    c4 -->|"Principal 에 그대로"| RES
    c5 -.-x|"쓰지 않음 (실명 보호)"| RES
    c6 -.-x|"쓰지 않음 (개인정보)"| RES

    RES --> ID["ChatIdentity<br/>(연결 상태에 보관)"]

    ID --> P["ChatParticipant<br/>→ UserJoined / UserLeft"]
    ID --> M["ChatMessage.User, UserId, Avatar<br/>→ ReceiveMessage + 저장"]
    ID --> G["ChatCaller (+Principal)<br/>→ IChatGuard"]

    NOAUTH["로그인 안 함"] -.-> GUEST["guest-xxxx<br/>(UserId, Avatar 없음)"]
    NONAME["필명이 비어 있음"] -.-> USERX["user-xxxx"]
```

**기억할 것**: 이름과 아바타는 연결 시점의 JWT 스냅샷이야. 필명을 바꾸면 다시 연결한 뒤에 반영돼. 액세스 토큰은 5분이지만, 허브는 연결할 때만 토큰을 검사해서 열린 대화가 끊기지는 않아.

---

## 6. 방 입장과 메시지 전송 (0.3 기준)

```mermaid
sequenceDiagram
    autonumber
    actor A as 도반 A
    participant HUB as ChatHub
    participant SITE as ISiteResolver
    participant IDR as IChatIdentityResolver
    participant G as IChatGuard
    participant ST as IChatStore
    actor B as 같은 방의 도반 B

    A->>HUB: 연결 (?site=buddham&access_token=...)
    HUB->>SITE: 사이트 판별
    alt 허용되지 않은 사이트
        HUB-->>A: 연결 거절
    else 허용
        HUB->>IDR: 필명, 아바타, ID 해석 (한 번)
        Note over HUB: 연결 상태 = 사이트 + ChatIdentity<br/>+ 입장한 방 + 전송 기록
    end

    A->>HUB: Join(lobby)
    HUB->>G: CanJoinAsync(caller)
    alt 거절
        HUB-->>A: "입장할 수 없는 방입니다."
    else 허용
        HUB->>HUB: 그룹 "buddham:lobby" 에 추가
        HUB-->>B: UserJoined(lobby, {userId, name, avatar})
        HUB->>ST: GetRecentAsync
        HUB-->>A: 최근 대화 이력
    end

    A->>HUB: SendMessage(lobby, 텍스트)
    Note over HUB: 입장했는지, 길이, 분당 전송 한도를 먼저 검사
    HUB->>G: InspectAsync(caller, 텍스트)
    alt 거절
        HUB-->>A: 거절 사유
    else 허용 (고쳐 쓴 문장일 수도)
        HUB->>ST: SaveAsync(메시지 + UserId + Avatar)
        HUB-->>A: ReceiveMessage
        HUB-->>B: ReceiveMessage
    end

    A--xHUB: 연결 끊김
    HUB-->>B: UserLeft(lobby, {userId, name, avatar})
```

---

## 7. 이번 수정의 지도

## 7. 이번 수정의 지도

```mermaid
flowchart TB
    START(["Viva.Chat 0.3.0-preview.1<br/>(배포됨)"])

    subgraph R04["0.4.0-preview.1 (세 패키지를 한 태그로 배포)"]
        direction TB
        S1["① ViVaKR.AiChat 추가<br/>AiChatHub (Open / Send / Cancel / Close), 스트리밍 이벤트,<br/>사용자별 세션 격리, IAiChatGateway, IAiTurnGate"]
        S2["② ChatRoomRules<br/>예약 방 접두어 ai- (ChatHub 가 거절)"]
        S3["③ AiChatServer.Contracts 0.1.0-preview.2 로 의존 갱신"]
        S4["④ 배포 워크플로: Contracts → Chat → AiChat,<br/>하나라도 없으면 아무것도 올리지 않음"]
        S5["⑤ 테스트 37개 통과, CHANGELOG, README, AGENTS 정리"]
        S1 --> S2 --> S3 --> S4 --> S5
    end

    USE["buddham 에서 실제로 사용<br/>(브라우저까지 완주)"]
    NEXT["다음 버전: AI 허브 테스트, 연결 끊김 시 취소 옵션,<br/>사용자 단위 제한, Redis 턴 게이트"]
    V1(["1.0.0 승격<br/>(AiChatServer.Contracts 정식 필요)"])

    START --> S1
    S5 --> TAG["태그 v0.4.0-preview.1 푸시"] --> USE --> NEXT --> V1

    subgraph SITE["buddham API 쪽 작업 (라이브러리와 별개)"]
        direction TB
        N1["JWT OnMessageReceived 에 /hubs 추가"]
        N2["AuditLog ExcludePaths 에 /hubs 추가"]
        N3["nginx location ^~ /hubs/ 추가 (/hubs/ai 도 포함)"]
        N4["Program.cs: AddVivaChat(...).UseStore(...).AddAiChat(...),<br/>MapVivaChat, MapVivaAiChat (로그인 필수)"]
        N5["buddham-api.yaml: VivaAiChat__ServerUrl<br/>(appsettings 의 VivaAiChat 섹션)"]
    end

    USE -.-> SITE
    SITE -.->|"브라우저 완주 확인"| USE
```

**지금 해야 할 일 순서**

1. 문서 정리를 마치고 `git add` 로 `src/Viva.Chat.Ai/`, `ChatRoomRules.cs` 가 들어갔는지 확인하기
2. `publish.md` 순서대로 pack → nuspec 확인 → 커밋 → 태그 push
3. buddham API 쪽 5가지는 라이브러리와 따로 진행할 수 있어 (단, `MapVivaAiChat` 은 인증이 설정되어 있어야 시작돼)
