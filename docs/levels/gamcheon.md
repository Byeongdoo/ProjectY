# 감천 마을 (regions 61101) 레벨

- 씬: `Client/Assets/Scenes/Single/Village_Gamcheon.unity` (`regions.scene` = `Village_Gamcheon`). Build Settings에 등록됨.
- 생성·검증: `Tools > ProjectY > Levels > Build Village_Gamcheon` ([GamcheonVillageBuilder.cs](../../Client/Assets/Editor/GamcheonVillageBuilder.cs)). **배치 좌표는 이 스크립트가 원본**이다. 씬을 손으로 고치면 다시 돌릴 때 덮어쓰므로 좌표를 스크립트에 반영한다. 저장 안 된 씬이 열려 있으면 실행하지 않는다.
- 미리보기: `artifacts/levels/Village_Gamcheon_Overview.png` (1칸 = 32px).
- 기준: Notion `레벨 디자인 가이드`(2-5 이승 지역, 2-10 타일맵 레이어), `01 감천 마을` 맵 설정. 수치는 `configs` 90001~90019, 레이어 정렬은 `tileLayers`에서 읽는다.

## 배치 (64×44칸, 왼쪽 아래 원점)

| 요소 | 위치 | 비고 |
|---|---|---|
| 플레이어 시작 | (32,4) 마을 어귀 | `Markers/PlayerSpawn` |
| 주 동선 | 남북 큰길 3칸(x 31~33), 동서 길 2칸(y 21~22) | 90007(2칸) 이상 |
| 마을 우물 (랜드마크) | 광장 중앙 3×3 (31~33, 21~23) | 통행 불가 |
| 노파 집 (랜드마크) | 동쪽, 돌담 안 7×5 + 마당 + 장독대 | 돌담 서쪽 틈으로 출입 |
| 숲길 입구 (랜드마크) | 북쪽 끝 x 31~33 | `Markers/Passage_62101`(트리거) → 안개골 숲 61102, 앞 3×3 비움 |
| 이장 집(기와), 아이 어머니 집(초가) | 서쪽 길 북편 | 일반 초가 2채는 남쪽 길 양옆 |
| 이승 신단 (altarCount 1) | (37,34) 북쪽 길 동편 | 주변 8칸 비충돌 |
| 단서 51102 아이의 빈 무덤 (Observe) | (21,36) 무덤 언덕, 상호작용 칸 (22,36) | 주 동선에서 9칸 |
| NPC 81101 노파 / 81102 어머니 / 81103 이장 | (52,24) / (17,24) / (36,23) | `Markers/NpcSpot_<npcID>` |

- 레이어: `tileLayers` 8종을 Tilemap으로 만들고 sortOrder를 그대로 쓴다. Collision은 렌더러를 끈 마스크(이승판만). 나무·바위는 발밑 Y 정렬 오브젝트(Props 순서).
- 빌더가 검사하는 것: 최대 변(90018), 주 동선 폭(90007), 조사 대상 간격(90019)·앞 칸, 신단 주변, 관문 앞 3×3, NPC·단서·신단·출구가 스폰에서 걸어서 닿는지(대각선 규칙 미확정이라 4방향으로 판정), 테이블의 감천 NPC·Observe 단서 목록과 배치 일치.
- `Markers`의 시각 표시는 `EditorOnly`라 빌드에서 빠진다. NPC 생성·상호작용·출구 이동은 이승 시스템 구현 때 마커 이름으로 연결한다.

## 조작 (이승 단독, 서버 없음)

- `Player`(스폰 칸) + [IseungPlayerController](../../Client/Assets/Scripts/Iseung/IseungPlayerController.cs): 방향키 8방향 칸 이동, 두 키 = 대각선 1칸, Shift+방향키 = 제자리 방향 전환. 속도는 `characterLevel`(1000001, 레벨 1)의 `moveSpeed`(4칸/초). 레벨은 로컬 세이브가 생기면 거기서 읽는다.
- 칸 판정은 씬 루트의 [IseungMap](../../Client/Assets/Scripts/Iseung/IseungMap.cs)(Ground 범위 + Collision 마스크). 대각선은 서버 `Map.CanMove`와 같이 직선 두 칸이 모두 막혔을 때만 막는다.
- 카메라는 [IseungCameraFollow](../../Client/Assets/Scripts/Iseung/IseungCameraFollow.cs): 주인공 추적, 맵 밖 비추지 않음, 1픽셀 격자 스냅.
- 애니메이터: `Resources/Art/Characters/Protagonist/Protagonist.controller`(빌더가 IDLE/RUN 8방향 클립으로 생성).

## 결정이 필요한 것

- 신단 위치: 가이드는 "던전으로 이어지는 막힌 관문에서 20칸 안"인데, 1챕터의 막힌 관문(62103)은 안개골 숲에 있어 감천에는 해당 관문이 없다. 지금은 숲길 출구 쪽(관문 방향)에 두었다.
- 이승만의 길(챕터마다 1개 이상): 1챕터에 `passages` 행이 없다. 감천에 둘지는 '챕터별 레벨 디자인'에서 정해야 한다.
- 남쪽 어귀는 다른 지역과 연결되지 않는다(감천은 선행 지역 없음). 마을 밖으로 나가는 길을 막을 연출이 필요한지.

## 리소스 요청 (Codex/PixelLab, 규격은 art-pipeline.md v1)

현재는 `Client/Assets/Placeholders/Gamcheon/`의 단색 타일. 받으면 빌더의 해당 타일만 바꾼다.

| 이름 | 크기 | 용도 / 레이어 |
|---|---|---|
| Gamcheon_ThatchHouse (몸체·벽·문 / 지붕 분리) | 5×4칸(160×128px) 기준 | 초가 — Structures(몸체), Foreground(지붕) |
| Gamcheon_TileHouse (몸체 / 기와지붕 분리) | 7×5칸(224×160px) | 이장 집 |
| Gamcheon_CroneHouse | 7×5칸 | 노파 집(굿을 청한 집 표식: 금줄 등) |
| Gamcheon_StoneWall 직선·모서리·끝 | 32×32 모듈 | 돌담 — Structures |
| Gamcheon_Well + 물결 4프레임 | 3×3칸(96×96) | 우물 — Structures / Animated |
| Gamcheon_Jangdokdae | 2×2칸 | 장독대 — Props |
| Gamcheon_Grave, Gamcheon_GraveEmpty(파헤쳐진) | 1×1칸(투명 소품, 높이 여유 허용) | 무덤 — Props, 빈 무덤은 조사 대상 |
| Gamcheon_Altar (이승 신단) | 1×1칸 + 높이 여유 | Props |
| Gamcheon_ForestEdge (빽빽한 숲 가장자리) | 32×32 모듈 또는 큰 수관 | 지금은 소나무를 촘촘히 배치 |
| Gamcheon_Details (풀 덤불·낙엽·돌멩이) | 32×32 투명 | Details |
