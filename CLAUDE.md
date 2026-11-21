# ProjectY (무당: 조선의 낮과 밤)

조선 배경의 **2D 도트 탑다운 MORPG**. 이승(싱글 스토리·퍼즐) → 삼천도(공용 로비) → 저승(4인 협동 던전) 구조.
레퍼런스는 바람의 나라(조작·전투·시점). 이 문서는 Claude Code(구현 담당)가 매 세션 읽는 룰북이다.

- 역할 분담: **Codex + Pixlab MCP = 리소스(스프라이트 등) 제작**, **Claude Code = 구현**.
- 기획 원본은 Notion `ProjectY`. 이 저장소에 기획 내용을 복사하지 않는다.

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `Client/` | Unity 클라이언트 (코드는 `Client/Assets/Scripts`) |
| `Server/` | .NET 10 게임 서버 (`Server`, `ServerCore`, `PacketGenerator`) |
| `Common/` | 클라·서버 공용. `protoc-3.12.3-win64/bin/Protocol.proto`(패킷 정의), `MapData` |
| `Table/MasterTable.xlsx` | 밸런스·수치 마스터 테이블 (**원본**) |
| `Client/Assets/Resources/Data/*.json` | xlsx에서 변환된 JSON (클라·서버가 같이 읽음, 직접 수정 금지) |
| `docs/` | 상세 문서 (아래 링크) |

## 절대 규칙

1. **수치 하드코딩 금지.** 밸런스·스탯·쿨타임·범위 등은 `MasterTable.xlsx` → JSON으로 읽는다. 새 수치가 필요하면 테이블에 컬럼/시트를 추가한다.
2. **JSON은 산출물이다.** `Resources/Data/*.json`을 직접 고치지 말고 xlsx를 고친 뒤 변환한다. → [docs/data-tables.md](docs/data-tables.md)
3. **서버 권위.** 이동·전투·대상 판정·피해 계산은 서버가 한다. 클라는 입력 전송과 연출(예측 포함)만 담당한다. → [docs/architecture.md](docs/architecture.md)
4. **패킷은 Protocol.proto가 원본.** 수정 후 `GenProto.bat`(PacketGenerator 포함)을 돌려 클라·서버 코드를 재생성한다. 생성 파일을 손으로 고치지 않는다.
5. **기획이 모호하면 구현 전에 질문한다.** 구현 전에 해당 Notion 페이지(`시스템 기획` 하위)를 확인한다. 확정되지 않은 항목은 임의로 정하지 말고 사용자에게 묻는다.
6. **리소스는 직접 만들지 않는다.** 스프라이트는 Codex가 만든다. 필요한 리소스는 [docs/art-pipeline.md](docs/art-pipeline.md)의 규격으로 요청 목록을 정리해 전달하고, 임시 구현은 플레이스홀더로 한다.
7. **DB·웹서버는 지금 만들지 않는다.** 로드맵상 해당 단계에 도달했을 때 도입한다. 그 전에는 메모리 상태 + 인터페이스(저장소 추상화)만 유지한다. → [docs/roadmap.md](docs/roadmap.md)
8. **Unity `.meta` 파일은 항상 함께 커밋한다.** GUID가 깨지면 참조가 전부 끊어진다.

## 핵심 게임 규칙 요약 (상세는 Notion)

- 이동: 방향키 **8방향**, **칸 단위 이동**(대각선도 1칸). 두 키 동시 입력 시 대각선.
- 전투: Space 평타(바라보는 방향) + 숫자키 1~8 스킬 → 방향키로 대상 선택(이동 불가) → Enter/같은 키 확정, Esc 취소.
- 직업(원혼): 저승 입장 시 선택, 5종(탱커·딜러·원거리 딜러·버퍼·힐러), 파티 4인·중복 불가. 레벨·장비는 캐릭터 귀속.
- 용어 주의: 적 분류의 '원혼'은 '망령'으로 변경 제안 중(미확정). 코드·테이블 명명 전에 확인.

## 작업 방식

- 작업 전: 관련 Notion 기획 페이지와 `docs/`의 해당 문서를 읽는다. 큰 작업은 계획을 먼저 공유한다.
- 작업 후: 클라(Unity 컴파일 오류 없음)·서버(`dotnet build`) 둘 다 빌드되는지 확인한다. 패킷/테이블을 바꿨다면 재생성 후 확인한다.
- 커밋은 사용자가 요청할 때만 한다. 커밋 메시지는 한국어, 한 커밋에 한 가지 목적.
- 코딩 규칙: [docs/conventions.md](docs/conventions.md)

## 상세 문서

- [docs/architecture.md](docs/architecture.md) — 클라·서버 구조, 패킷 흐름, 서버 권위
- [docs/data-tables.md](docs/data-tables.md) — MasterTable 규칙, xlsx→JSON 변환, 로딩
- [docs/table-converter.md](docs/table-converter.md) — xlsx→JSON/C# 변환기 설계
- [docs/art-pipeline.md](docs/art-pipeline.md) — Codex/Pixlab 리소스 규격과 전달 방식
- [docs/roadmap.md](docs/roadmap.md) — 단계별 구현 순서
- [docs/conventions.md](docs/conventions.md) — 코딩·커밋 규칙, 체크리스트
