# 아키텍처

## 구성

- **Client (Unity)**: 입력, 렌더링, UI, 연출. 게임 상태의 원본이 아니다.
- **Server (.NET 10)**: 게임 상태의 원본. `ServerCore`(소켓·세션·버퍼) 위에 `Server`(게임 로직)가 올라간다.
  - `Game/Room`: `GameRoom`, `Map`, `RoomManager` — 공간 단위 로직. 삼천도(공용 로비)·저승 던전도 룸 단위로 다룬다.
  - `Game/Object`: `GameObject`, `Player`, `Monster`, `Projectile` 등.
  - `Game/Job`: `JobSerializer`, `JobTimer` — 룸 단위 직렬 실행(락 대신 잡 큐).
- **Common**: `Protocol.proto`(패킷 정의), `MapData`.

## 패킷 흐름

1. `Common/protoc-3.12.3-win64/bin/Protocol.proto` 수정
2. 같은 폴더 `GenProto.bat` 실행 → `Protocol.cs`, 패킷 매니저/핸들러 생성
3. 클라(`Client/Assets/Scripts/Packet`)·서버(`Server/Server/Packet`)로 복사/생성되는지 확인
4. 핸들러(`PacketHandler.cs`)에 로직 추가

> GenProto.bat이 실제로 복사하는 대상 경로는 첫 사용 시 확인해 이 문서에 보정한다.

## 서버 권위 원칙

| 항목 | 서버 | 클라 |
|---|---|---|
| 이동 | 칸 이동 유효성(충돌·맵 경계) 판정, 결과 브로드캐스트 | 입력 전송, 즉시 이동 연출(예측), 서버 결과로 보정 |
| 평타·스킬 | 사거리·대상·쿨타임·자원·피해 계산 | 입력/대상 선택 UI, 이펙트·애니메이션 |
| 대상 선택 | 요청된 대상의 유효성 검증 | 방향키 대상 전환 UI |
| 스탯·레벨·드랍 | 전부 서버 | 표시만 |

- 클라가 보낸 값(좌표, 데미지, 대상 ID)은 **신뢰하지 않고 검증**한다.
- 수치(스킬 사거리, 쿨타임, 스탯)는 서버·클라가 **같은 JSON 테이블**을 읽는다. 클라는 표시·예측용으로만 쓴다.

## 단계별 확장 지점

- 저장소(계정·캐릭터·인벤토리)는 인터페이스로 분리해 두고, 초기에는 메모리 구현을 쓴다. DB 도입 시 구현체만 교체한다.
- 웹서버(로그인·패치·랭킹 등)는 게임 서버와 분리된 프로세스로 둔다. 도입 시점은 [roadmap.md](roadmap.md) 참고.
