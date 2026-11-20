## [0.3.0-preview.1] - 준비 중

호환이 깨지는 변경이 있는 프리뷰입니다. 1.0.0 이전이라서 계약을 지금 바로잡습니다.

### 추가
- `IChatIdentityResolver` 와 기본 구현(JWT 클레임). `UseIdentityResolver<T>()` 로 교체합니다.
- 옵션 `UserIdClaim`(`sub`), `DisplayNameClaim`(`preferred_username`), `AvatarClaim`(`picture`), `MaxDisplayNameLength`(50)
- `ChatParticipant(UserId, Name, Avatar)`
- `ChatMessage.Avatar` (보낸 시점의 스냅샷, 선택)
- `ChatCaller.Avatar`, `ChatCaller.Principal` (가드가 나이·역할 같은 클레임을 직접 볼 수 있음)

### 바뀜
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
