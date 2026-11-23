# 데이터 테이블 (MasterTable)

## 원칙

- 원본은 `Table/MasterTable.xlsx`. 밸런싱·수치 조절은 전부 여기서 한다.
- 변환 결과 JSON은 `Client/Assets/Resources/Data/`에 둔다. 클라는 Resources로, 서버는 `Server/Server/config.json`의 `dataPath`로 같은 파일을 읽는다.
- JSON은 **산출물**이다. 직접 수정하지 않고, xlsx 수정 → 변환 → JSON 갱신 순서를 지킨다. JSON도 커밋한다(서버·클라가 xlsx 없이 실행되도록).

## xlsx 구조 (2026-11-21 기준 실제 확인)

시트는 43개. 크게 세 종류다.

| 종류 | 시트 이름 | 처리 |
|---|---|---|
| 데이터 시트 (33개) | 영문 camelCase (`regions`, `spirits`, `spiritSkills` …) | JSON으로 변환 |
| 설계 시트 (10개) | `#`으로 시작 (`#원혼 설계`, `#타입 정의` …) | **변환 제외** (설계 메모) |
| 로컬라이징 | `strings` | 별도 규칙(아래) |

### 데이터 시트 행 규약

| 행 | 내용 | 예 |
|---|---|---|
| 1행 | 한국어 라벨. 타입 힌트는 `\|float` 접미사 | `쿨타임\|float` |
| 2행 | **영문 키(JSON 필드명)**. A열은 `주석` | `coolTime`, `skillIDs<List>` |
| 3행~ | 데이터 | |

- **A열(기능)**: 비어 있으면 일반 행, `//`이면 주석 처리된 행(변환 제외). 현재 `skills` 시트가 전부 `//`다.
- **`#`로 시작하는 키 컬럼**(2행 값이 `#지역`, `#설명` 등): 사람이 보는 메모 컬럼이라 **변환 제외**. 1행 라벨이 `메모`인 컬럼이 이에 해당한다.
- **`<List>` 접미사**: 값이 `|`로 구분된 목록. 예) `skillIDs<List>` = `85011|85012|85013|85014` → `[85011, 85012, 85013, 85014]`. 키에서는 `<List>`를 떼어 필드명으로 쓴다.
- **`|float` 접미사**: 실수. 접미사가 없는 숫자 컬럼은 정수, 나머지는 문자열로 추정한다. (**확정 필요**)
- 고유 ID 컬럼은 대개 2열이며 이름이 `xxxID`다(`regionID`, `spiritID`, `skillID` …). 예외: `characterLevel`은 `group`+`level` 복합 키.
- 다른 시트 참조는 대상 ID를 그대로 쓴다(예: `spirits.skillIDs` → `spiritSkills.skillID`, `dungeons.boss` → 보스 캐릭터 ID).
- ID 대역: 지역 `61xxx`, 통로 `62xxx`, 단서 `51xxx`, NPC `81xxx`, 퀘스트 `83xxx`, 원혼 `85xx`, 원혼 스킬 `85xxx`, 몬스터 분류 `90xx`, 던전 `87xxx`, 드랍 `91xx`/`96xxx` 등. 새 ID는 해당 대역 규칙을 따른다.

### 시트 목록 (용도)

- **월드·스토리**: `configs`(전역 상수, `configID`→`value<List>`), `regions`, `passages`, `worldEffects`, `cases`, `clues`, `npcs`, `dialogues`, `quests`, `beliefChoices`, `chapters`, `chapterSteps`
- **캐릭터·성장**: `characterInfos`(PC·몬스터·보스 공용), `characterLevel`, `items`, `itemEnchants`, `dropGroups`
- **원혼(직업) 시스템**: `spirits`, `spiritSkills`, `statusEffects`
- **몬스터·던전**: `monsterTypes`, `drops`, `spawnGroups`, `bossPhases`, `bossSkills`, `dungeons`, `dungeonSections`
- **삼천도**: `lobbySpots` — Notion 기준 신설(시설 5곳, ID `88xx`)이지만 **저장소의 xlsx(2026-11-21 커밋본)에는 아직 없다.** 최신 xlsx를 반영해야 한다.
- **UI**: `hudElements`
- **레거시 (정리 대상, 2026-11-21 확정)**: `weapons`, `weaponCombos`, `skills`, `skillNodes` — 이전 핵앤슬래쉬 방향(무기 중심)의 산물. 원혼 기준으로 재설계되었으므로 **새 코드에서 쓰지 않으며 변환 대상에서도 제외**한다. xlsx에서의 실제 삭제와 참조 정리(`#장비 설계` 등 설계 시트 포함)는 변환기가 완성되어 로더 교체가 끝난 뒤 한 번에 한다. 그 전에는 건드리지 않는다.

### 로컬라이징 (`strings`)

- 열: `stringID` | KR | EN | CN | TW | JP | (`#`DEU 등은 변환 제외 언어).
- 다른 시트의 `name`, `textKey` 컬럼은 **표시 문자열이 아니라 stringID**다(예: `Region_61101`, `Dlg_811010`). 표시 문자열은 `strings`에서 조회한다. 코드에 한글 문자열을 직접 쓰지 않는다.
- `//`로 시작하는 행은 구분용 주석.

## JSON 형식

- 시트 1개 = JSON 1개. 파일명은 시트명을 PascalCase로 바꾼 것(`spiritSkills` → `SpiritSkills.json`)을 제안한다.
- 최상위는 `{ "<camelCase 복수형>": [ {행}, ... ] }` 래퍼 구조로, 기존 `ILoader<Key, Value>` + Newtonsoft 로더가 `MakeDict()`로 `Dictionary<int, T>`를 만든다.
- 필드명은 2행 영문 키, 값은 **실제 타입**(숫자는 숫자)으로 출력한다.

> 레거시 `StatData.json`·`SkillData.json`과 이를 읽던 `DataManager.StatDict/SkillDict`는 원혼 전투 구현(2026-11-23) 때 삭제했다. 스탯은 `characterLevel`·`spirits`, 스킬은 `spiritSkills`·`statusEffects`·`configs`(전투 상수)에서 읽는다.

## 변환기

- 위치: `Tools/TableConverter/` (별도 .NET 콘솔)을 제안한다. 이유: 클라(Unity)와 서버 양쪽에서 쓰고, CI·명령줄에서도 돌릴 수 있다. Unity 에디터 메뉴는 이 콘솔을 호출하는 얇은 래퍼로 둔다. (**확정 필요**)
- 입력 `Table/MasterTable.xlsx`, 출력 `Client/Assets/Resources/Data/*.json`.
- xlsx 라이브러리는 라이선스를 확인하고 도입한다(ClosedXML 등 MIT 계열 우선, EPPlus는 비상업 라이선스 주의). 이 xlsx는 LibreOffice Calc로 저장된 파일이다(`appName="Calc"`).
- 규칙: 위 행 규약 그대로. `#` 시트, `//` 행, `#` 컬럼은 건너뛴다.
- 검증(오류로 보고): ID 중복, 타입 변환 실패, 존재하지 않는 ID 참조, `strings`에 없는 stringID.

## 코드 쪽 규칙

- 새 시트를 추가하면 (1) `Data.Contents.cs`(클라·서버 각각)에 데이터 클래스와 `ILoader` 구현 추가, (2) `DataManager.LoadData()`에 로딩 추가, (3) 변환 → JSON 커밋.
- 데이터 클래스의 필드명은 JSON 키(2행 영문 키)와 일치시킨다.
- 서버 시작 시 로딩 실패는 **즉시 종료**하고 어느 파일·행이 문제인지 로그를 남긴다(조용히 기본값으로 진행하지 않는다).
- 게임 로직에서 매직 넘버가 보이면 `configs`나 해당 시트로 옮긴다.

## 밸런싱 워크플로

1. xlsx에서 수정
2. 변환기 실행 → JSON 갱신
3. 서버 재시작 + 클라 실행해 확인
4. xlsx와 JSON을 같은 커밋에 포함

## 열린 질문

- `[폐기]` 표시가 붙은 컬럼(`staggerResist`, `staggerMax` 등 경직 관련)은 시트에 남아 있다. 변환기가 제외하도록 규칙(예: 라벨의 `[폐기]` 표기 또는 `tables.json`의 제외 목록)을 정한다.
- `configs`의 ID 대역: 전투 `20xxx`, 저승 던전 `60001~`, 파티·매칭 `60011~`, 삼천도 `70001~`. 폐기 상수는 A열 `//`. 새 상수는 해당 대역을 이어 쓴다.
- 용어 충돌은 해소됐다(적 분류 '원혼' → '망령'). 표시 이름(`strings`)만 바뀌었고 `monsterTypes` 9002·`characterID` 키와 `#원혼 설계` 시트명은 그대로다.
- 접미사 없는 컬럼의 타입 추정 규칙을 그대로 둘지, 타입 행을 별도로 추가할지
