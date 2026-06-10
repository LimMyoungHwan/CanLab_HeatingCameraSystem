# UI 명세 & Stitch 시안

정본 문서: `docs/ui_requirements.md`. Stitch 프로젝트 ID: **18194888590092025705** (제목 "HeatingCameraSystem UI").
디자인 시스템: **Industrial Precision Control** (다크 슬레이트 #1a1f2e + 틸 #00ced1 액센트, Hanken Grotesk UI / JetBrains Mono 수치).

## 4개 화면 (Stitch screen IDs)
1. **메인 대시보드** `f67a29c86af24ac5ad3a60500df1b5d1` → `docs/mockups/01_dashboard.html`
   - 좌상단: 영상 그리드 (View Mode 1~5), 1초 갱신
   - 좌하단: 챔버 온도/습도 트렌드 차트 (단일 소스)
   - 우측: Agent PC별 그룹 카메라 리스트(드래그 소스) + START/STOP
   - 상단: 레시피 드롭다운, 64/64 연결, EMERGENCY STOP
2. **레시피 편집** `83fbe9b0797c4002a26c5310aada8abe` → `02_recipe_editor.html`
   - 좌: 레시피 목록 + New/Copy/Del
   - 우(본문): 레시피 전체 챔버 타겟 온/습도, Capture Mode(Sequential1/Simultaneous2 토글), 스텝 리스트(드래그 정렬, 스텝마다 Position→CAM + 블랙바디 타겟온도)
3. **카메라-위치 매핑** `d86ce0a5614c4ac5b6525928d2f270f7` → `03_camera_position_mapping.html`
   - 64 고정 슬롯 8x8 그리드 + 드래그앤드롭, 우측 Agent PC별 카메라, "48/64 assigned"
4. **히스토리** `8875640daead4bc39000f37ad56d12c9` → `04_history.html`
   - From/To 날짜, 카메라 선택(온도범위 없음), 결과 테이블(타임스탬프/카메라/온도/습도/썸네일), 썸네일 클릭→팝업

## View Mode (대시보드 영상)
- Mode1: 최대 8개, **연결 카메라 수 기반 적응형 순환** (페이지수=ceil(연결수/8), 1초 간격 페이지 전환). 4개만 연결시 순환 없이 1초 갱신.
- Mode2~5: 8/4/2/1개, 드래그앤드롭 등록분만 표시.

## 레이아웃 통일
- 레시피·히스토리의 좌측 네비 메뉴를 **우측으로 이동**해 대시보드와 통일 완료 (edit_screens 적용, mr-64/border-l).

## 데이터 저장/보존
- Agent PC + Master PC 양쪽 저장, 경로 사용자 지정.
- Agent: 지정일 이후 자동 삭제. Master: 무한 보존 or 지정일 삭제(옵션).
- 카메라-위치 매핑 정보도 양쪽 저장.

## 참고
- 레시피 시안 중복 1개 존재: `f5e3c3adabcd43e88e755a9b09f12381` (메뉴 미이동 구버전, 무시/삭제 대상).
- Stitch 다운로드 URL은 1회성 서명 — 만료시 get_screen으로 재발급 필요.
