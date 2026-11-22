# 멀티 프로토타입: 감천 → 삼천도 → 산신당 저승길

이승(감천) 신단에서 강림해 삼천도로 가고, 나루터 사공에게 말을 걸어 던전 룸으로 들어가는 흐름의 첫 구현. 전투는 다음 단계.

## 만드는 법 (Unity 메뉴)

1. `Tools > ProjectY > Levels > Build Village_Gamcheon` — 감천 신단에 `IseungAltar`가 붙는다. `Protagonist.controller`도 여기서 만든다.
2. `Tools > ProjectY > Levels > Build Multi Prototype` ([MultiPrototypeBuilder.cs](../../Client/Assets/Editor/MultiLevels/MultiPrototypeBuilder.cs))
   - 씬: `Assets/Scenes/Multi/Samcheondo.unity`, `Dungeon_87101.unity` (Build Settings 등록)
   - 프리팹: `Resources/Prefabs/Creature/MudangMyPlayer`, `MudangPlayer` (주인공 8방향 스프라이트) — 삼천도용
   - 원혼 프리팹: `Resources/Prefabs/Creature/Spirit/PC_Spirit_01~05`(`_My` = 내 캐릭터) — 던전용. 이름은 `spirits.prefabName`, 생성은 `Tools > ProjectY > Build Spirit Player Prefabs`
   - 서버 맵: `Common/MapData/<씬>.txt`(충돌) + `<씬>.markers.json`(입장 영역·시설 칸)
3. 서버 재시작. 서버는 시작할 때 `Samcheondo.txt`가 없으면 종료한다.

배치 좌표는 빌더 파일이 원본이다. 씬을 손으로 고치면 다시 빌드할 때 덮어쓴다.

## 흐름

| 단계 | 클라 | 서버 |
|---|---|---|
| 강림 | 감천 신단 앞 칸에서 F → 확인(Enter/Esc) → configs 10001초 페이드 → `Samcheondo` 씬 | — (이승은 서버 없음) |
| 삼천도 입장 | `MultiScene`이 타일맵으로 칸 판정 생성, 접속, 접속되면 `C_EnterGame` | 접속 시 `PendingRoom = 삼천도`, `C_EnterGame`에 강가 도착 영역(6×6, configs 90020) 빈 칸에 배치 |
| 출항 | 사공(8802) 앞 칸에서 F → 원혼 선택(←→, Enter, Esc) → `C_EnterDungeon(87101, spiritId)` | 로비 룸·던전·원혼 존재·사공 옆 칸(체비셰프 1) 검증 → 던전 룸에서 원혼 예약(같은 룸 안 중복이면 `S_EnterDungeonReject`) → 로비에서 퇴장 → `S_ChangeScene("Dungeon_87101")`. 던전에서는 `ObjectInfo.spiritId`의 원혼 프리팹으로 스폰 |
| 던전 입장 | 씬 로딩 후 `C_EnterGame` | 던전 룸(던전별 공용 1개)의 입장 영역에 배치 |

- 씬 이름 = 서버 맵 이름: `Samcheondo`, `Dungeon_<dungeonID>` (서버 `RoomManager`, 클라 빌더가 같은 규칙).
- 삼천도는 전투 없음: 클라 Space 비활성·체력 막대 숨김, 서버도 로비 룸의 `C_Skill`을 버린다.
- 아군 겹침(configs 90005): 서버 `Map`은 플레이어를 칸 점유 격자에 올리지 않고 칸별 인원만 센다. 몬스터는 플레이어를 막는다. 클라는 `ObjectManager.FindBlocker`로 같은 규칙을 쓴다.

## 임시로 정한 것 (정식 기획·데이터가 오면 교체)

- **강림 해금 조건 없음**: 감천 신단에서 바로 강림한다. Notion 기준 첫 강림은 안개골 무너진 다리 앞 신단(64103)이다.
- **매칭 없음, 원혼 선택은 임시 화면**: 사공 → 원혼 선택(IMGUI 임시 화면) → 던전 87101 테스트 룸. 모든 플레이어가 같은 룸을 쓰고, 그 룸 안에서 원혼 중복만 막는다(인원 제한·큐·수락·30초 제한·원혼별 스탯 배율 미적용).
- **삼천도 배치**: Notion에 배치가 없어 규격(60×34, 강가 6×6, 시설 간 40칸)으로 임시 배치했다. 강은 북쪽, 잔교 끝에 사공(30,28).
- **던전 배치**: 구간·연결로·불씨·보스방 좌표는 Notion '1챕터 숲' 2-6을 옮겼다(왼쪽 위 원점 → Unity 왼쪽 아래 원점으로 변환). 원본인 xlsx `levelMaps`/`levelObjects` 시트가 **저장소 xlsx에 없다.** 산신당 앞마당(x1~20, y46~54)·보스방 입구(x21, y49~51)·입장 영역·단서 51103 위치는 문서에 없어 임의로 정했고, 망자의 다리·붉은 실 장막은 아직 없다.
- **강림 확인 창**: OnGUI 플레이스홀더. '인게임 HUD' 구현 때 교체.
- **해금 검증**: 서버는 던전 해금 여부를 보지 않는다(이승 진행도가 로컬 세이브라 서버에 원본이 없음 — architecture.md 미확정 항목).

## strings 시트에 추가할 행

| stringID | KR (제안) |
|---|---|
| `Ui_Descent_Confirm` | 신단 앞에서 스스로 죽음을 택합니다. 삼천도로 건너가시겠습니까? |
| `Ui_Confirm_Keys` | Enter 건너간다 · Esc 그만둔다 |

추가 전에는 화면에 `[Ui_Descent_Confirm]`처럼 ID가 보인다.

## 리소스 요청 (Codex/PixelLab, 규격은 art-pipeline.md v1)

지금은 `Assets/Placeholders/Multi/`의 단색 타일이다.

| 이름 | 크기 | 용도 |
|---|---|---|
| Samcheondo 강가 지면(모래·자갈) Wang 세트 | 32×32 | Ground |
| 삼도천 물 + 물결 4프레임, 물안개 가장자리 | 32×32 | Structures / Animated |
| 나루터 잔교 직선·끝, 나룻배 | 32×32 모듈, 배 6×2칸 | Structures |
| 주막 건물(몸체/지붕 분리), 명부 게시판 | 8×6칸, 2×1칸 | Structures / Foreground |
| 저승 신단 | 1×1칸 + 높이 여유 | Props |
| NPC 스승·사공·주모 | 캐릭터 규격 | NPC |
| 산신당 저승길 저승판 바닥·벽(안개골 저승 프리셋), 불씨 제단 | 32×32 | Ground / Structures / Props |
