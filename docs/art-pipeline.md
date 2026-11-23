# 아트 파이프라인 (Codex + Pixlab ↔ Claude Code)

리소스는 Codex(ChatGPT)가 Pixlab.ai MCP로 만들고, 구현은 Claude Code가 한다. 이 문서는 **양쪽이 공유하는 규격**이다. Codex에게도 이 문서를 그대로 전달한다.

> 2026-11-22 감천 테스트 세트와 기존 주인공을 Unity 6000.3.5f2에서 비교해 아래 리소스 제작 기준 v1을 정했다. 캐릭터·타일·소품의 **픽셀 밀도는 공통 32 PPU**다. 이 문서는 미술/임포트 규격이며 맵 배치·전투·서버 판정 수치를 확정하는 문서가 아니다.

## 컨셉 이미지 선제작·컨펌 (2026-11-22 확정)

**GPT 컨셉 이미지 제작 → 사용자 컨펌 → PixelLab 제작 요청** 순서를 지킨다.

- 캐릭터·몬스터·무기·소품·맵/타일·이펙트·UI 등 신규 리소스와 외형·연출 재작업에 적용한다.
- GPT(ChatGPT/Codex)가 관련 기획과 기존 스타일을 확인한 뒤 **이미지 생성 도구로 컨셉 이미지를 먼저 그려 사용자에게 보여준다.** 텍스트 프롬프트만 제시하거나 PixelLab으로 먼저 시안을 뽑는 것으로 대체하지 않는다.
- 사용자가 수정을 요청하면 GPT 컨셉 이미지를 수정하여 다시 보여준다. **해당 이미지에 대한 명시적 컨펌 전에는 PixelLab 생성·재생성 요청을 보내지 않는다.** GPT의 자체 선택이나 응답이 없는 상태를 승인으로 해석하지 않는다.
- 컨펌 후 승인된 이미지를 PixelLab의 시각 참조로 전달하고, 본 문서의 픽셀 규격·시점·팔레트·크기·방향/프레임 명세를 함께 적용한다. 컨셉 이미지는 디자인 참조이며 실제 납품용 도트 규격은 별도로 준수한다.
- 제작 요청 기록에 승인된 컨셉 이미지의 참조와 사용자 컨펌 내용·일자·승인 범위를 남긴다. 같은 승인 범위의 방향 확장·애니메이션은 해당 컨셉을 재사용하되, 외형이나 연출 방향을 바꾸면 수정 컨셉을 먼저 컨펌받는다.

## 공통 규격 v1

| 항목 | 값 |
|---|---|
| 스타일 | 조선 배경 2D 도트, 고정 low top-down / 약한 3/4 시점 |
| 타일 크기 | 32×32 px = 테스트 Grid의 1×1 Unity unit. 전체 맵을 한 장으로 생성하지 않는다. |
| 캐릭터 프레임 | 기존 주인공 64×64 px. 캔버스는 2×2칸, 실제 불투명 높이는 59px ≒ 1.84칸. 캔버스 크기와 점유 칸은 별개다. |
| PPU / Transform | 신규 캐릭터·타일·소품은 모두 32 PPU, Transform scale=(1,1,1). 고해상도 그림을 축소해 도트 밀도를 맞추지 않는다. |
| 파일 | PNG RGBA, 캐릭터·소품·VFX는 투명 배경. 지면 타일은 불투명 반복 텍스처. |
| 팔레트 | 공간별 분위기 준수 (이승 따뜻함 / 삼천도 회색빛·물안개 / 저승 붉거나 푸른 달빛) |
| 임포트 | Sprite / Point / Compression None / Mipmap Off / sRGB On / Full Rect / NPOT None / Wrap Clamp |
| 캐릭터 피벗 | 주인공의 고정 발밑 기준: 원본 왼쪽 아래 기준 (32,3) px, 정규화 (0.5,0.046875). 104개 이미지에 동일 적용. 프레임마다 자동 trim/중심 재계산 금지. |
| 타일 피벗 | 중앙 (0.5,0.5). Tilemap Grid cell size=(1,1,0). |
| 조명·텍스처 | 좌상단 광원, 선명한 픽셀 군집, 인물보다 낮은 지면 대비. 캐스트 그림자는 본체에 굽지 않고 별도 레이어로 제작. |
| 표시 | 정수 배율 확대와 픽셀 단위 카메라 정렬. 참조 씬은 32 render px/unit, MSAA Off, 결과 PNG는 nearest-neighbour 2배 확대. 게임용 Pixel Perfect 카메라 통합은 구현 단계에서 확인. |

### 감천 비교 결과

- 32px 타일 옆에서 주인공만 64 PPU로 표시하면 원본 1픽셀이 지면의 0.5픽셀 크기가 되어 작아지고 세부가 손실된다. 32 PPU로 통일하면 원본 픽셀 크기가 일치한다.
- 중앙 피벗에서는 Transform의 위치와 발 위치가 다르다. 발밑 피벗을 사용하면 접지 위치·가림 정렬의 기준을 같은 지점에 둘 수 있다. 주인공 원본 PNG와 애니메이션 GUID는 유지하고 임포트 설정만 변경했다.
- 주인공 IDLE은 방향별 4프레임, RUN은 8프레임, 현재 10fps 루프다. 다른 동작의 프레임 수와 재생 시간은 동작별 요청서에 명시한다.

| 자산 | 원본 캔버스 | 실제 불투명 영역 | 32 PPU에서 실제 크기 | 발밑 피벗(px, 왼쪽 아래 기준) |
|---|---|---|---|---|
| 주인공 정면 | 64×64 | 29×59 | 0.91×1.84칸 | (32,3) |
| 테스트 소나무 | 96×128 | 62×75 | 1.94×2.34칸 | (46,26) |
| 테스트 바위 | 64×64 | 35×27 | 1.09×0.84칸 | (34,11) |

소품 크기는 **투명 여백을 제외한 불투명 영역**으로 확인한다. 현재 소나무는 작은 마을 소나무의 기준 시안이며, 큰 나무를 추가할 때는 픽셀을 확대하지 않고 목표 불투명 높이를 명시해 새로 제작한다. 캐릭터 몸통, 무구, 큰 소품의 캔버스 규격은 32px 모듈의 배수로 확장할 수 있다.

### 가림과 충돌

- Ground는 낮은 sorting order, 캐릭터와 소품은 같은 sorting layer/order에서 카메라 Custom Axis=(0,1,0), Sprite Sort Point=Pivot을 사용한다. 화면 아래쪽 발밑이 앞에 표시된다.
- 나무의 Trunk / Canopy는 렌더 파트를 분리하되, **뿌리 위치의 하나의 SortingGroup**에 묶는다. 수관만 무조건 최상위 Foreground로 올리면 나무 앞의 플레이어까지 가릴 수 있다.
- 잎 영역 전체를 충돌로 막지 않는다. 나무의 논리 충돌은 줄기/뿌리 근처의 칸으로 따로 정의한다. 참조 프리팹의 BoxCollider2D는 가림·접지 검사용이며 서버의 막힌 칸/이동 판정 명세가 아니다.
- Unity 오프스크린 렌더에서 나무 뒤 겹침 836픽셀 중 836픽셀이 나무에 가려지고, 앞 배치에서는 캐릭터가 나무 위에 표시되는 것을 검사했다.

### 지면·경계 제작

- 감천 흙길↔잔디는 32px Wang corner tile 16종(지면 2종 + 전이·모서리 14종)이다. 파일 이름/생성 순서로 위치를 추정하지 않고 PixelLab metadata의 `bounding_box`와 NW/NE/SW/SE 코너를 사용한다.
- 각 타일의 공유 꼭짓점 지형을 동일하게 정해 조립한다. `wang_N`과 `original_position`은 아틀라스 좌표가 아니다.
- 후속 지형 연결 시 기존 base tile ID를 재사용한다. 감천 흙: `ba00427d-a4e7-4b8c-acb5-2d5637f5fa6a`, 잔디: `0f5c159f-51b4-4b36-8c33-81ea00762333`.
- PixelLab source metadata, 팔레트, 원본 atlas를 함께 보관한다. 나무·바위 등 독립 소품은 같은 주인공 스타일 참조와 low top-down 카메라를 사용한다.

### 확인할 파일

- 실제 리소스: `Client/Assets/Resources/Art/Tiles/Iseung/Gamcheon/` (`Ground`, `TileAssets`, `Props`).
- 비교 씬: `Client/Assets/Scenes/ArtTests/GamcheonArtStudy.unity`, `GamcheonScaleComparison.unity`, `GamcheonOcclusionComparison.unity`.
- 재생성/검사 메뉴: `Tools > ProjectY > Build Gamcheon Art Study`. 현재 수정한 씬은 저장 여부를 확인한 후 전환한다.
- 측정·검증: [ValidationReport.json](../artifacts/pixellab/GamcheonStudy/ValidationReport.json), [OcclusionCheck.json](../artifacts/pixellab/GamcheonStudy/OcclusionCheck.json).
- 미리보기: [테스트 배치](../artifacts/pixellab/GamcheonStudy/Gamcheon_Overview.png), [PPU·피벗 비교](../artifacts/pixellab/GamcheonStudy/Gamcheon_ScaleComparison.png), [가림 비교](../artifacts/pixellab/GamcheonStudy/Gamcheon_OcclusionComparison.png).

## 캐릭터 8방향 스프라이트

### 기존 원혼 시안 V1 제거 (2026-11-22)

- 사용자 요청으로 기존 `Spirits` 리소스·프리팹과 `.meta`, V1 비교 씬 3개, V1/V2 비교 씬, V1 제작 도구를 Unity 프로젝트에서 제거했다. 현재 사용하는 시안은 아래 `SpiritsV2`다.
- 기존 파일은 Unity 밖의 `artifacts/pixellab/SpiritConceptStudy/RemovedFromUnity/`에 보관하며, 과거 미리보기·생성 기록도 `artifacts`에 남긴다. 이 보관본은 Unity 리소스나 현행 디자인 기준으로 사용하지 않는다.

### 첨부 이미지 기반 원혼 정면 재시안 V2 (2026-11-22)

- 사용자가 기존 외형을 재작업하도록 요청하여 첨부한 5종 컨셉 카드의 복식·머리·색·무구를 V2의 우선 시각 기준으로 사용했다. V1의 성별·복식·침통 해석을 V2에 강제하지 않는다. V1은 Unity에서 제거했다.
- V2 위치는 `Client/Assets/Resources/Art/Characters/SpiritsV2/<영문키>/`. 64×64 투명 PNG, 32 PPU, Point, 원본 픽셀 유지, 발밑 피벗·Y 정렬을 사용한다. 큰 머리와 풍성한 장식은 이번 첨부 이미지에 맞춘 시안이며 최종 디자인 확정은 별도다.
- 무명장수는 남색·금장 갑옷과 사자 장식 방패, 망나니는 거친 머리와 붉은 넓은 처형도, 착호갑사는 긴 묶은 머리의 여성·녹색 복식·각궁과 화살통, 꼭두쇠는 넓은 남색 갓·금장 공연복·꽹과리와 채, 의녀는 꽃장식·연보라 한복·작은 약함과 은침을 기준으로 삼는다.
- PixelLab Pro에 직업별 인물 원화·작은 도트 예시·무구 이미지를 나누어 전달하고 16개 후보 중 1개씩 선택했다. `artifacts/pixellab/SpiritConceptStudyV2/`에 원본 참조, 후보, 생성 기록과 선택 번호를 보관한다. 각 리소스의 `SourceInfo.json`에서도 정확한 job ID와 후보 번호를 확인할 수 있다.
- 비교 씬은 `SpiritFrontLineupV2.unity`, `SpiritSilhouetteComparisonV2.unity`. 메뉴는 `Tools > ProjectY > Build Spirit Reference V2 Study`다. V1/V2 비교 씬과 생성 기능은 이전 리소스 제거에 맞춰 정리했다. 이번 범위는 정면 재시안 5종이며 방향 확장·애니메이션은 포함하지 않는다.

### 방향별 애니메이션 납품

#### 원혼 5종 전체 제작 진행 (2026-11-22)

- 사용자 요청으로 `SpiritsV2` 정면 외형을 PixelLab 캐릭터 5개로 등록하고 각각 8방향으로 확장했다. 동작은 Notion 각 원혼의 제작 명세에 맞춰 `IDLE`, `RUN`, `ATTACK`, `HIT`, `STUN`, `DIE`, `KNOCKBACK`, `SKILL_1`~`SKILL_4`의 11종이다. 총 55개 동작 그룹·440개 방향별 클립·3,136개 생성 프레임을 요청 목록에 등록했다. 전체 완료 여부는 아래 진행 기록에서 확인한다.
- 생성 요청: `artifacts/pixellab/SpiritAnimationProduction/ProductionPlan.json`. 현재 접수·완료·Unity 저장 수는 `Progress.md`와 `QueueState.json`이 원본이다. 로컬 요청 대기와 PixelLab 접수·완료를 구별한다. Tier 2 동시 10작업 제한에 맞춰 순차 요청하며, API 응답이 불확실할 때는 임의로 재요청하지 않고 중복 과금을 막도록 중지한다.
- `Tools/Art/pixellab_spirit_batch.py`가 기존 Codex 연결의 인증을 읽어 같은 PixelLab MCP 도구를 호출한다. 키는 리소스·진행 기록에 저장하지 않는다. 작업은 숨겨진 별도 프로세스에서 진행되므로 Codex/Unity 창을 닫아도 이어지지만 컴퓨터·네트워크는 켜져 있어야 한다. 중단 후에는 작업 기록을 대조해 재개한다.
- 완성 파일: 각 캐릭터의 `Sprites/Rotations/`, `Sprites/<동작>/<방향>/frame_NNN.png`, `Sheets/<영문키>_<동작>.png`와 레이아웃 JSON, `Animations/<동작>_<방향>.anim`, `AnimationSource.json`. 기존 정면 PNG·프리팹·GUID는 유지한다. 게임의 Animator/공격 판정/빙의 전환 방식은 이 작업에서 변경하지 않는다.
- 프레임 수: idle·hit·stun·knockback 4, run·attack 및 기본 스킬 8, die 12. 망나니 skill2·착호갑사 skill2·의녀 skill4는 준비/시전을 읽기 쉽게 12프레임이다. idle·run·stun은 루프이며 무명장수 skill2는 방어 유지 루프다. 나머지는 1회 재생이다. 클립의 FPS는 제작 요청서에 기록한 연출용 기본값으로 게임의 시전 시간·공격 속도를 확정하지 않는다.
- PixelLab V3 애니메이션은 64px 회전 원본에 동작 여백을 더해 방향별로 84~92px 이상 캔버스를 사용할 수 있다. 원본 PNG는 확대·축소·프레임별 trim하지 않는다. 피벗은 `원본 발밑 피벗 + (애니메이션 캔버스 - 64) / 2`로 유지해 여백을 보정한다. 시트 셀은 가장 큰 원본 프레임에 가운데 여백만 추가한다. PPU는 계속 32다.
- 자동 저장은 생성 완료를 뜻하며 시각 검수 승인을 뜻하지 않는다. 무구 누락·뒤바뀜, 발 미끄러짐, 루프 이음새, 스킬 준비/발동/회수는 최종 검수한다. 스킬 VFX·투사체·덫은 본체와 분리한다. `DIE`는 명세상의 쓰러짐 리소스 이름이며 실제 혼절/부활 규칙을 변경하지 않는다.
- Unity 확인: `Tools > ProjectY > Verify Available Spirit Animations` 또는 `SpiritAnimationProductionValidator.VerifyAvailable`. 현재 완성된 클립만 검사하고 `UnityValidationReport.json`에 부분/전체 완료를 표시한다. 검사는 현재 열어 둔 씬을 전환하지 않는다.

#### 원혼 애니메이션 검수 및 근접 Trail (2026-11-22)

- 기본 생성 440클립·3,136프레임은 모두 저장되었다. 시각 검수는 **보완 필요**이며 `artifacts/pixellab/SpiritAnimationQA/QAReport.md`가 관찰·우선순위의 기준이다. 생성 완료나 Unity 구조 검사 통과를 최종 미술 승인으로 해석하지 않는다.
- 사용자가 근접 무기 궤적을 프레임에 추가하도록 요청하여 무명장수 ATTACK, 망나니 ATTACK/SKILL_1~3, 꼭두쇠 ATTACK/SKILL_1~4의 총 80방향 클립·672프레임에 PixelLab VFX 궤적을 합성했다. 이 요청은 본체/VFX 분리 원칙에 대한 해당 동작의 합성본 제공 요청이며, 독립 효과 레이어도 함께 보관한다.
- `Sprites/`는 원본이다. `SpritesWithTrail/`는 합성 프레임, `TrailLayers/`는 투명 효과 레이어, `Sheets/*_WithTrail.*`·`*_TrailOnly.*`는 시트와 레이아웃이다. 같은 GUID의 기본 클립이 합성 프레임을 참조한다. 원래 클립 백업은 Unity 밖의 `artifacts/pixellab/SpiritAnimationQA/Trails/NativeClipsWithoutTrail/`에 있다.
- 칼날 궤적은 본체 뒤, 짧은 채 잔상은 얼굴 아래 손·타격부 앞에 표시한다. 원본 PNG 해시, 효과 바깥의 의도하지 않은 픽셀 변경 0, 효과의 실제 가시성, 발 피벗을 검사했다. 각 `TrailSource.json`과 `Trails/TrailDelivery.json`에 출처·적용 범위를 기록했다.
- Unity 검사에서 전체 440클립의 연결과 Trail 80클립·672프레임을 확인했다. `Tools > ProjectY > Verify Spirit Trail Playback`은 Animator를 붙인 격리된 프리팹 복사본에서 실제 프레임을 샘플링하며 열린 씬/저장된 프리팹을 바꾸지 않는다. 결과는 `UnityTrailPlayback.json`이다. 정면 시안 프리팹 자체는 정적 프리팹이며 게임 Animator 전이는 별도 구현한다.
- 검수 재생 화면은 `artifacts/pixellab/SpiritAnimationQA/viewer.html`이며 원본/Trail 전환과 프레임 이동을 지원한다. 이음새·무구 형태·착호갑사 쓰러짐 등의 원본 결함은 Trail 추가와 별개의 재작업 대상이다.

- 방향: 하(S), 하우(SE), 우(E), 상우(NE), 상(N), 상좌(NW), 좌(W), 하좌(SW).
- 좌우 대칭 방향은 **반전으로 대체하지 않는다**(비대칭 소품·무기 때문). 단, 비용이 크면 5방향 + flip으로 합의 후 변경 가능.
- 시트 레이아웃: **행 = 방향, 열 = 프레임**, 방향 순서는 위 목록 순서(S부터 시계 방향이 아니라 위 순서 고정).
- 상태별로 시트를 분리: `idle`, `walk`, `attack`, `skill_<n>`, `hit`, `die`.

## 파일명·폴더

```
Client/Assets/Resources/Art/
  Characters/<캐릭터명>/<캐릭터명>_<상태>.png      예) Mudang/Mudang_walk.png
  Monsters/<몬스터ID>/<몬스터ID>_<상태>.png
  Tiles/<공간>/...                                  공간 = Iseung | Samcheondo | Jeoseung
  Effects/<이름>_<프레임수>f.png
  UI/...
```

- 파일명은 영문, PascalCase + `_` 구분. 한글 파일명 금지.
- 몬스터·스킬 이펙트의 ID는 `MasterTable`의 `Id`/`Name`과 맞춘다(테이블이 기준).

## 요청·전달 흐름

1. Claude Code가 기능 구현 중 필요한 리소스를 **요청 목록**(이름, 크기, 방향/프레임 수, 용도, 테이블 ID)으로 정리해 사용자에게 준다.
2. 사용자가 Codex에 전달하면 GPT가 **컨셉 이미지를 먼저 제작**하여 보여준다.
3. 사용자의 수정 의견을 반영하고 **컨셉 이미지에 대한 명시적 컨펌**을 받는다. 컨펌 전에는 PixelLab에 제작을 요청하지 않는다.
4. Codex가 승인된 컨셉 이미지와 제작 규격을 참조로 PixelLab에 요청하고, 결과를 위 폴더 규격대로 저장한다.
5. Claude Code가 임포트 설정·슬라이싱·애니메이션 컨트롤러 연결을 맡는다. (스프라이트 슬라이싱 규칙은 시트 레이아웃을 따른다.)
6. 리소스가 아직 없으면 단색 사각형 등 플레이스홀더로 구현을 진행하고, 교체 지점을 TODO로 남기지 않고 요청 목록에 기록한다.


#### 무명장수 큰 검 및 북쪽 공격 수정 (2026-11-22)

- 무명장수 88클립·608프레임과 정면/8방향 기준 이미지를 큰 은색 도신으로 수정했다. 북쪽/북동 공격은 전방 자세로, 북서는 PixelLab 레이어 동작으로 수정했다. 공격 8방향 Trail도 재연결했다. 기록: rtifacts/pixellab/MumyeongJangsuSwordRevision/RevisionReport.md.
- 무명장수 기준 PNG는 **96×96 투명 캔버스**, 피벗 **(48,18)**, 32 PPU다. 기존 64×64 몸체의 픽셀 밀도와 발 기준점을 유지한다. 동작별 피벗은 x=width/2, y=18+(height-96)/2이며 원래 (32,2) 기준과 동일하다. 일부 프레임은 검 끝 여백을 위해 128×128이며 확대/축소하지 않는다. 시트의 최대 셀과 개별 PNG 크기를 구분한다.
- 기준 이미지 발 위치는 PixelLab 정수 이동으로 보정했다. 65프레임의 여백·검 끝 보정에서 본체 내부 픽셀 변경 0, 최종 608프레임 가장자리 접촉 0을 확인했다. 기본 클립 GUID·FPS·루프와 기존 씬은 유지했다.

### 1챕터 보스·2챕터 망령 임포트 (2026-11-23)

사용자 요청으로 PixelLab에 제작된 아래 6종을 `Resources/Art/Monsters/<characterID>/`에 가져왔다. 신규 생성 요청은 하지 않았다. 테이블의 ID·표시 이름·prefabName을 사용하며, 기존 몬스터 프리팹과 같은 구성으로 `Resources/Prefabs/Creature/Monster/`에 연결했다.

| 챕터 | 망령 | characterID | 프리팹 | 방향별 클립 | 동작 프레임 |
|---|---|---|---|---:|---:|
| 1 | 뒤틀린 목귀 | 3000101 | Boss_101 | 104 | 832 |
| 2 | 갯벌 잡귀 | 2000201 | Mob_201 | 72 | 448 |
| 2 | 물먹은 망령 | 2000202 | Mob_202 | 72 | 448 |
| 2 | 뱃노래 귀신 | 2000203 | Mob_203 | 72 | 448 |
| 2 | 그물 악귀(정예) | 2000204 | Mob_204 | 72 | 448 |
| 2 | 수귀 사공 | 3000201 | Boss_201 | 112 | 768 |

- 총 63동작 × 8방향 = **504클립·3,392프레임**. `Sprites/Rotations`, 동작별 원본 PNG, `Sheets` 시트·레이아웃, `Animations`, Animator Controller와 출처 JSON을 제공한다. 이동·기본 공격·사망의 상태 이름은 기존 클라이언트의 `IDLE/RUN/ATTACK/DIE_<방향>`과 맞췄다. 목귀의 원본 `Walk/Death`는 `RUN/DIE`로 매핑하고, 보스 패턴·페이즈·탈진 동작은 별도 이름으로 보존했다.
- `REJECTED_*`는 Unity에 넣지 않았다. 방향별 `*_FIX_*`, 최신 `Refined_*_R3` 및 뱃노래 귀신의 `POSE_HOLD_NW_R4`를 해당 기본 동작에 반영했다. 35개 방향 수정본의 원래 이름·그룹 ID는 각 `AnimationSource.json`의 `directionalSources`에 기록했다.
- 원본 PNG 바이트는 변경하지 않았다. 32 PPU, Point, 무압축, Mipmap Off, sRGB, Full Rect, NPOT None, Clamp를 사용한다. 각 캐릭터의 고정 발 기준점은 `SourceInfo.json`에 기록하며, 커진 동작 캔버스의 중앙 여백만 피벗에 보정한다. FPS·루프는 리소스 재생 기본값이며 전투 시전 시간·공격 속도를 정하지 않는다.
- 목귀 원본 회전 PNG 일부는 192px 캔버스 경계에 닿아 있다. 원본 보존을 위해 임의 보간·확대·외형 수정은 하지 않았다. 생성된 동작 프레임은 경계 접촉 0이다. 기술 검증은 미술 승인과 별개다.
- 다운로드 ZIP·메타데이터와 프레임 SHA-256 대조 기록: `artifacts/pixellab/MonsterImport/`. Unity 전체 프레임 샘플링과 프리팹·임포트 설정 검사 결과는 `UnityValidationReport.json`에 기록한다. 열린 씬은 변경하지 않는다.
- 재임포트 도구: `Tools/Art/import_completed_monsters.py`(Pillow 필요). 기존 다운로드만 사용하며 생성·과금 요청은 하지 않는다. `--repair`는 PNG를 유지하면서 임포트 메타데이터와 최종 GUID 기준 클립 참조를 재연결한다. 실행 후 Unity Refresh 및 검증이 필요하다. 기존 `Tools > ProjectY > Build Monster Prefabs` 메뉴로도 프리팹을 다시 만들 수 있다.

### 삼천도 타일 시트·팔레트 임포트 (2026-11-23)

- 사용자 요청으로 기존 PixelLab 삼천도 리소스를 Gamcheon의 Ground/Struct/Deco 구성에 맞춰 `Resources/Art/Tiles/Samcheondo/`에 가져왔다. `Tile_Samcheondo_Ground.png`(768×128, 80타일), `Tile_Samcheondo_Struct.png`(512×352, 79타일), `Tile_Samcheondo_Deco.png`(512×64, 10타일)와 각 `Tiles/`의 Tile 애셋을 제공한다.
- Ground는 강물↔강가 흙, 흙↔돌바닥, 강물↔잔교, 흙↔잔디, 흙↔잔교의 5개 Wang 세트다. 원본 metadata의 `bounding_box`로 잘라 각 블록에 코너 마스크 0~15 순서로 놓았다. 마스크는 `NW<<3 | NE<<2 | SW<<1 | SE`이며 블록 사이 한 칸을 비웠다.
- Struct는 주막·부활 신단·나룻배·명부 게시판·등불·버드나무·강가 바위, Deco는 갈대·낮은 물안개다. 주막과 신단은 나중에 제작된 low top-down 버전을 사용했다. 원본 PNG의 픽셀·캔버스를 확대·축소·보간하지 않고 정수 위치에 복사했다. 원본 오브젝트 경계를 알기 때문에 자동 덩어리 검출 대신 32px 격자로 묶어 자르고 완전히 투명한 칸만 제외한다.
- 모두 32 PPU, Point, 무압축, Mipmap Off, sRGB, Full Rect, NPOT None, Clamp다. 큰 오브젝트는 팔레트에서 칸 묶음을 선택해 한 번에 칠한다. 충돌은 기존 Collision 레이어 방식으로 따로 지정하며 Tile 자체 collider는 None이다.
- 팔레트 3개는 `Assets/Editor/Art/Palettes/Samcheondo/Palette_Samcheondo_<Ground|Struct|Deco>.prefab`이다. 시트 재조립은 `Tools/Art/import_samcheondo_tiles.py`(Pillow 필요), Unity 등록·검사는 `Tools > ProjectY > Levels > Samcheondo > Build Tile Palettes` / `Verify Tile Palettes`다. 재실행 시 시트·타일·팔레트 GUID와 스프라이트 fileID를 유지한다.
- 출처·시트 좌표는 `SourceInfo.json`, 원본 다운로드·PixelLab 응답·픽셀 검사·Unity 검사·재생성 참조 유지 검사 결과는 `artifacts/pixellab/SamcheondoTiles/`에 보관한다. 이번 작업은 타일 애셋·팔레트 등록이며 기존 씬의 맵 배치와 충돌 데이터는 변경하지 않는다.

### PixelLab 이펙트 리소스 임포트 (2026-11-23)

사용자 요청으로 이미 제작된 목귀 전투 VFX 7종과 산신당 환경 효과 3종을 가져왔다. 총 **10종·76개 원본 애니메이션 프레임**, 10개 클립·컨트롤러·프리팹이다. 원본 PNG의 바이트와 투명도를 유지하며 신규 생성 요청은 하지 않았다.

| 그룹 | 이펙트 키 | 원본 프레임 | 재생 |
|---|---|---:|---|
| Mokgui | WarningGrid, Phase2Aura, ExhaustedAura, ThreadBinding | 각 8 | 반복 |
| Mokgui | MountainWailRing, RootEruption, TankSlamImpact | 각 8 | 1회 |
| Sansindang | LowMist | 8 | 반복 |
| Sansindang | SealPulse, SoulFlame | 각 6 | 반복 |

- 아트는 `Resources/Art/Effects/<Mokgui|Sansindang>/<키>/`의 `Sprites`, `Sheets`, `Animations`, `<그룹>_<키>.controller`, `SourceInfo.json`이다. 프리팹은 `Resources/Prefabs/Effect/<그룹>/<그룹>_<키>.prefab`이며 `SpriteRenderer + Animator`, 기본 상태 `START`로 바로 재생한다. `PixelLabEffects.json`의 `resourcePath`를 `Resources.Load<GameObject>`에 넘겨 로드할 수 있다.
- 32 PPU, Point, 무압축, Mipmap Off, sRGB, Full Rect, NPOT None, Clamp, 중앙 피벗, Transform scale 1이다. 캔버스 기준점을 유지하며 프레임별 trim·확대·축소는 하지 않는다. 경고 격자·절규 파동·안개·봉인 문양은 `tileLayers.Details`, 나머지는 `tileLayers.Animated`의 sorting order를 사용한다.
- PixelLab 내보내기에 프레임 시간이 없어 반복은 8fps, 단발은 10fps의 리소스 재생 기본값을 기록했다. 전투 시전 시간·스킬 수치는 정하지 않는다. Unity는 마지막 스프라이트 키에도 한 프레임 길이를 더한다. 반복 클립은 원본 키만 사용하고, 단발 3종은 마지막 원본 프레임을 온전히 표시한 뒤 Sprite를 null로 바꾸는 종료 키를 추가했다. 원본 이미지 자체는 수정하지 않았다.
- 프리팹은 시각 리소스다. 스킬 이벤트에서 생성·부착하고, 반복 중지와 인스턴스 반환/삭제를 관리하는 부분은 호출 측에서 연결한다. 이펙트 판정용 collider, 피해 판정, 사망 이펙트 별칭은 추가하지 않았다.
- 재임포트는 `Tools/Art/import_pixellab_effects.py`(Pillow 필요) 후 `Tools > ProjectY > Effects > Build PixelLab Effects`를 실행한다. `Verify PixelLab Effects`는 모든 프레임과 Animator 시작·반복·종료, Resources 로드, 정렬·피벗·임포트 설정을 검사한다. 재실행 시 PNG·클립·컨트롤러·프리팹 GUID와 Sprite fileID를 유지한다.
- 다운로드 ZIP·PixelLab 응답·원본 대조·Unity 재생·GUID 유지 검사 결과는 `artifacts/pixellab/EffectsImport/`에 보관한다. 현재 검증은 76개 원본 프레임, 반복 7종, 종료 시 숨김 3종을 통과했으며 기존 열린 씬을 변경하지 않았다.
