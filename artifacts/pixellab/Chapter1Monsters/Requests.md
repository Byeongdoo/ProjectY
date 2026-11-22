# 1장 일반 몬스터 PixelLab 제작 요청

- 컨셉 승인일: 2026-11-22. 사용자가 2D 도트로 수정한 몬스터 3종 컨셉을 승인하고 PixelLab 제작을 요청했다.
- 승인 참조: [조선 설화 괴물 세 종 레트로 픽셀 스프라이트.png](https://chatgpt.com/library?file_id=libfile_04f9b7c664948191b659f5e2eaa50da6&account_id=personal). 각 작업에 같은 이미지를 보내고 왼쪽·가운데·오른쪽 중 해당 몬스터만 참조하도록 지정했다.
- 스타일 참조: `Client/Assets/Resources/Art/Characters/SpiritsV2/Uinyeo/Uinyeo_Front.png`의 윤곽, 디테일, 음영. 몬스터의 팔레트와 외형은 승인 이미지 기준.
- 접수 범위: 64×64 투명 배경, 낮은 탑다운 정면 대기 기준 스프라이트. 작업별 16개 후보, 10 생성량씩 총 30 생성량. 인게임 임포트 기준은 32 PPU.

| 몬스터 | 승인 이미지 위치 | PixelLab 작업 ID | 생성 상태 |
| --- | --- | --- | --- |
| 안개 잡귀 | 왼쪽 | `33eaa076-66e5-406d-b58f-a6ef9af32504` | 생성 완료 · 갤러리 저장 |
| 목매단 망령 | 가운데 | `020a35c2-687c-481f-b731-00a8b6e148f1` | 생성 완료 · 갤러리 저장 |
| 곡하는 귀신 | 오른쪽 | `3ad57e2c-80b9-4742-a9ce-975afa0c6199` | 생성 완료 · 갤러리 저장 |

정면 후보는 각 16개, 총 48개다. [후보 비교표](Candidates.png)의 번호는 PixelLab 다운로드 인덱스에 1을 더한 값이다. 기준 스프라이트 검수 후 8방향 제작과 노션에 정리된 9종 동작을 진행한다.

## Unity 정면 스프라이트

| 몬스터 | 후보 번호 | 게임 데이터 ID | Unity 에셋 |
| --- | --- | --- | --- |
| 안개 잡귀 | 14 | 2000101 | `Assets/Resources/Art/Monsters/2000101/2000101_Front.png` |
| 목매단 망령 | 01 | 2000102 | `Assets/Resources/Art/Monsters/2000102/2000102_Front.png` |
| 곡하는 귀신 | 02 | 2000103 | `Assets/Resources/Art/Monsters/2000103/2000103_Front.png` |

비교표에서 각 외형이 잘 드러나는 정면 후보를 골라 임포트했다. Unity에서 세 파일을 Resources의 Sprite로 불러와 64×64 px, 32 PPU, Point, 투명 배경과 하단 기준 피벗을 확인했다.

## 8방향 애니메이션 납품

세 몬스터의 대기, 이동, 기본 공격, 피격, 기절, 사망, 넉백, 스킬 1·2를 PixelLab V3로 제작했다. [제작 명세](AnimationPlan.json)와 [작업 기록](AnimationQueue.json)에 캐릭터와 애니메이션 그룹 ID를 보관한다.

| 몬스터 | Unity 에셋 폴더 | 시트 | 클립 | 프레임 |
| --- | --- | ---: | ---: | ---: |
| 안개 잡귀 | `Assets/Resources/Art/Monsters/2000101` | 9 | 72 | 480 |
| 목매단 망령 | `Assets/Resources/Art/Monsters/2000102` | 9 | 72 | 480 |
| 곡하는 귀신 | `Assets/Resources/Art/Monsters/2000103` | 9 | 72 | 480 |

각 폴더에 `Sprites`, `Sheets`, `Animations`, `AnimationSource.json`, Animator Controller와 `<ID>_Animated.prefab`을 저장했다. Unity에서 프리팹을 임포트했고, 총 216개 Animator 상태와 1,440개 스프라이트 참조를 확인했다. 기본 상태는 `IDLE_SOUTH`다. 던전 스폰 연결은 별도 구현 대상이다.

일부 원본 프레임에 PixelLab이 만든 효과 픽셀이 있다. 정면 샘플에서 곡하는 귀신의 대기 중 청록색 파편과 목매단 망령의 스킬 1 중 섬광을 확인했다.
