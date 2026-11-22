# 감천 마을 (regions 61101) 레벨

- 씬: `Client/Assets/Scenes/Single/Village_Gamcheon.unity` (`regions.scene` = `Village_Gamcheon`). Build Settings에 등록됨.
- **맵은 사람이 칠한다.** 지면·건물·충돌은 Tile Palette로 각 레이어 Tilemap에 칠하고, 건물·나무·소품 프리팹은 `Village_Gamcheon/YSortedObjects` 아래에 놓는다. **위치의 원본은 씬**이다.
- 도구([GamcheonVillageBuilder.cs](../../Client/Assets/Editor/GamcheonVillageBuilder.cs)), 둘 다 칠한 타일·놓은 프리팹은 건드리지 않는다.
  - `Tools > ProjectY > Levels > Gamcheon > Setup Scene`: 씬 뼈대를 **없는 것만** 만들고 참조를 다시 잇는다. 레이어 Tilemap(`tileLayers`), 마커, `IseungMap`, `Player`(스폰 마커 칸으로 이동), 카메라, 충돌 타일·팔레트가 대상이다. 이미 있는 마커는 옮기지 않는다.
  - `Tools > ProjectY > Levels > Gamcheon > Validate + Overview`: 칠한 Ground/Collision과 마커 위치로 규격을 검사하고, `artifacts/levels/Village_Gamcheon_Overview.png`(1칸 = 32px)를 만든다.
- 기준: Notion `레벨 디자인 가이드`(2-5 이승 지역, 2-10 타일맵 레이어), `01 감천 마을` 맵 설정. 수치는 `configs` 90001~90019, 레이어 정렬은 `tileLayers`에서 읽는다.

## 칠하는 법

- **팔레트:** `Tools > ProjectY > Levels > Gamcheon > Build Tile Palettes`가 `Tile_Gamcheon_Ground/Deco/Struct.png`를 잘라(32 PPU, Point) 시트별 `Tiles/` 타일 에셋과 `Assets/Editor/Art/Palettes/Gamcheon/Palette_Gamcheon_{Ground,Deco,Struct}`를 만든다. 모든 타일은 한 칸(32px)이다. Ground는 시트 격자 그대로, Deco·Struct는 그림마다 그 그림을 덮는 칸 묶음으로 잘라(가로 가운데, 바닥 맞춤) 팔레트에 묶음별로 늘어놓는다. 큰 그림은 팔레트에서 묶음을 드래그로 골라 한 번에 칠한다. 시트를 다시 그리면 다시 실행한다(같은 이름은 GUID 유지).
- **레이어:** Ground(지면, 없는 칸은 맵 밖으로 막힘) / Details / Structures / Props / Animated / Foreground(캐릭터 위를 덮는 것). 정렬 순서는 Setup이 `tileLayers`에서 맞춘다.
- **충돌:** `Collision` Tilemap에 `Palette_Collision` 팔레트(`Assets/Editor/Art/Palettes/Gamcheon`, Build Tile Palettes·Setup Scene이 없으면 만든다)의 빨간 타일을 칠한다. 타일이 있는 칸은 못 지나간다. 에디터에서는 반투명으로 보이고 플레이 중에는 `IseungMap`이 숨긴다.
- **Y 정렬 프리팹:** 건물·나무 등 캐릭터가 뒤로 돌아가는 것은 Tilemap 대신 프리팹으로 `YSortedObjects`에 둔다. 카메라는 Y축 정렬이고 정렬 순서는 Props(30)다. 프리팹이 놓인 칸의 충돌은 따로 칠한다.
- **마커(`Markers/*`):** 칸 안에 놓으면 그 칸이 위치다. `PlayerSpawn`, `NpcSpot_<npcID>`, `Clue_<clueID>`(Observe 단서), `Altar_61101`(IseungAltar), `Passage_62101`(BoxCollider2D 트리거, 크기가 출구 범위). 시각 표시는 `EditorOnly`라 빌드에서 빠진다.

## Validate가 검사하는 것

- 씬 이름 = `regions.scene`, 칠한 지면의 최대 변 ≤ 90018, `passages` 62101이 이승 통행 가능.
- 마커가 테이블과 맞는가: 감천 NPC 전원, Observe 단서 전원, 신단 수 = `altarCount`.
- 스폰에서 걸어서 닿는가(대각선 모서리 규칙 미확정이라 4방향으로 판정): NPC 칸, 단서 상하좌우 중 한 칸, 신단 주변, 출구.
- 신단 주변 8칸 비충돌, 출구 트리거 칸 비충돌, 조사 대상끼리 간격 ≥ 90019.
- 주 동선 폭(90007)은 칠한 길을 자동으로 판정하지 않는다. 눈으로 확인한다.

## 처음 위치 (마커가 없을 때 Setup이 쓰는 값)

| 마커 | 칸 |
|---|---|
| PlayerSpawn | (32,4) |
| NpcSpot 81101 노파 / 81102 어머니 / 81103 이장 | (52,24) / (17,24) / (36,23) |
| Clue_51102 아이의 빈 무덤 | (21,36) |
| Altar_61101 | (37,34) |
| Passage_62101 → 안개골 숲 61102 | (32,42), 3×3 |

## 조작 (이승 단독, 서버 없음)

- `Player`(스폰 칸) + [IseungPlayerController](../../Client/Assets/Scripts/Iseung/IseungPlayerController.cs): 방향키 8방향 칸 이동, 두 키 = 대각선 1칸, Shift+방향키 = 제자리 방향 전환. 속도는 `characterLevel`(1000001, 레벨 1)의 `moveSpeed`(5.2칸/초). 레벨은 로컬 세이브가 생기면 거기서 읽는다.
- 대각선 1칸 소요 시간은 `configs` 20030 배율(1.414 = √2, 2026-11-22 사용자 결정)을 따른다. 화면상 이동 속도가 직선과 같다. 멀티 클라 보간·서버 몬스터 이동 간격도 같은 값을 쓴다.
- 이동 중 입력 보정: 직선 한 칸을 시작하고 `configs` 20048(0.15초, 가정값) 안에 두 번째 방향키가 들어오면 그 칸을 대각선으로 바꾼다. 진행 방향의 정반대를 누르면 출발 칸으로 바로 돌아간다. 둘 다 한 칸에 한 번만 적용된다. 그 밖의 방향 전환은 칸 도착 때 반영되고, 이동 중에 눌렀다 뗀 방향키도 기억해 두었다가 칸 도착 때 한 칸 반영한다(`StepInputBuffer`, 키를 뗀 순간은 기억하지 않음).
- 칸 판정은 씬 루트의 [IseungMap](../../Client/Assets/Scripts/Iseung/IseungMap.cs)(Ground 범위 + Collision 마스크). 대각선은 서버 `Map.CanMove`와 같이 직선 두 칸이 모두 막혔을 때만 막는다.
- 카메라는 [IseungCameraFollow](../../Client/Assets/Scripts/Iseung/IseungCameraFollow.cs): 주인공 추적, 맵 밖 비추지 않음, 1픽셀 격자 스냅.
- 애니메이터: `Resources/Art/Characters/Protagonist/Protagonist.controller`(없으면 Setup이 IDLE/RUN 8방향 클립으로 생성).

## 결정이 필요한 것

- 신단 위치: 가이드는 "던전으로 이어지는 막힌 관문에서 20칸 안"인데, 1챕터의 막힌 관문(62103)은 안개골 숲에 있어 감천에는 해당 관문이 없다. 지금은 숲길 출구 쪽(관문 방향)에 두었다.
- 이승만의 길(챕터마다 1개 이상): 1챕터에 `passages` 행이 없다. 감천에 둘지는 '챕터별 레벨 디자인'에서 정해야 한다.
- 남쪽 어귀는 다른 지역과 연결되지 않는다(감천은 선행 지역 없음). 마을 밖으로 나가는 길을 막을 연출이 필요한지.

