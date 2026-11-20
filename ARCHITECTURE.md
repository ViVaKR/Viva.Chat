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
            L1["ViVaKR.Chat 패키지<br/>NuGet 0.3.0-preview.1"]
            G1["IChatGuard<br/>(방 권한, 내용 검사)"]
            S1["IChatStore<br/>(메시지 저장)"]
            I1["IChatIdentityResolver<br/>(JWT → 필명, 아바타)"]
            L1 --- G1 & S1 & I1
        end
        subgraph H2["AiChatHub  /hubs/ai  (사람 ↔ 법등)"]
            direction TB
            L2["우리가 만든 AiChatHub<br/>(나중에 ViVaKR.AiChat 패키지)"]
            C2["IAiChatClient<br/>(AiChat.Client, gRPC)"]
            S2["IConversationStore<br/>(Redis, 나중에 DB)"]
            K2["IAiUserLock<br/>(사용자당 동시 1개)"]
            L2 --- C2 & S2 & K2
        end
        JWT["JWT 인증 (OnMessageReceived 에서<br/>/hubs 경로는 access_token 쿼리 허용)"]
        JWT --> H1 & H2
    end

    C2 ==>|gRPC| AIS["aichat-server (공용)"]
    S1 -.-> DB[("사이트 DB")]
    S2 -.-> RD[("사이트 Redis")]
    K2 -.-> RD
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
    participant RD as Redis (사이트)
    participant AI as aichat-server (공용)
    participant OL as Ollama (M1 GPU)

    도반->>NG: WebSocket 연결 (?access_token=JWT)
    NG->>HUB: 업그레이드 (101)
    도반->>HUB: Ask(conversationId, question)
    Note over HUB: 사용자 ID 는 JWT 에서만 꺼냄<br/>(클라이언트가 보내지 않음)
    HUB->>RD: 사용자 락 획득 (SET NX)
    alt 이미 생성 중
        HUB-->>도반: error: busy
    else 락 획득
        HUB->>RD: 이전 대화 읽기
        HUB->>AI: StreamChat(페르소나, 이력 + 새 질문)
        AI->>OL: /api/chat (stream)
        loop 토큰이 생성되는 동안
            OL-->>AI: 조각
            AI-->>HUB: ChatChunk (content/thinking)
            HUB-->>도반: token / thinking 이벤트
        end
        AI-->>HUB: done + usage
        HUB->>RD: 질문/답변 한 쌍 저장 (정상 완료 때만)
        HUB-->>도반: done (토큰 수)
        HUB->>RD: 락 해제
    end
    opt 사용자가 중지 버튼
        도반-->>HUB: 구독 해제 (취소)
        HUB-->>AI: 취소 전파
        AI-->>OL: 생성 중단 (GPU 해방)
        Note over HUB,RD: 취소되면 아무것도 저장하지 않음
    end
```

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

```mermaid
flowchart TB
    START(["Viva.Chat 0.2.0-preview.1<br/>(배포됨)"])

    subgraph R03["0.3.0-preview.1 (묶어서 한 번에 배포)"]
        direction TB
        S1["① 식별 + 계약 변경<br/>IChatIdentityResolver, ChatParticipant, Avatar<br/>✅ 코드 전달 완료 (빌드 확인 대기)"]
        S2["② CORS 정책 재사용 옵션"]
        S3["③ 저장소 오류 처리 + 로깅"]
        S4["④ MapVivaChat 허브 옵션 오버로드<br/>+ 경로 /hubs/* 통일"]
        S5["⑤ 테스트 프로젝트<br/>(입장→전송→수신, 사이트 거절,<br/>입장 전 전송, 가드 거부)"]
        S6["⑥ CHANGELOG, README, deploy.yaml 정리<br/>(--skip-duplicate, 태그 v[0-9]*)"]
        S1 --> S2 --> S3 --> S4 --> S5 --> S6
    end

    USE["buddham 에서 실제로 사용<br/>(브라우저까지 완주)"]
    V1(["1.0.0 승격"])

    START --> S1
    S6 --> TAG["태그 v0.3.0-preview.1 푸시"] --> USE --> V1

    subgraph SITE["buddham API 쪽 작업 (라이브러리와 별개)"]
        direction TB
        N1["JWT OnMessageReceived 에 /hubs 추가"]
        N2["AuditLog ExcludePaths 에 /hubs 추가"]
        N3["nginx location ^~ /hubs/ 추가"]
        N4["Program.cs: AddSignalR, AddAiChatClient,<br/>MapHub, 저장소/락 등록"]
        N5["buddham-api.yaml: Services__Ai__Address"]
    end

    USE -.-> SITE
    SITE -.->|"브라우저 완주 확인"| USE
```

**지금 해야 할 일 순서**

1. ① 번 파일로 `dotnet build` 하기
2. 안 되면 에러를 알려주기 → ②~⑥ 으로 이어가기
3. buddham API 쪽 5가지는 라이브러리와 따로 진행할 수 있어
