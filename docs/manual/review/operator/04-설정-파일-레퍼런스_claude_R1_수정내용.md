# 04-설정-파일-레퍼런스 — claude 검토 (Round 1)

## 종합 평가
- 수정 필요도: 중
- 한 줄 요약: 필드 레퍼런스 표는 정확하나, 4.2에서 예고한 "Nats/Serial" 섹션이 실제로는 존재하지 않고 manager-settings/state.json은 필드 설명 없이 방치되어 있음.

## 수정 필요 항목
1. [4.2 참고 표] 문제: "Plc / Nats / Serial / BlackBody / RecipeEngine" 필드가 "아래 4.3~4.6 참고"라고 안내하지만, 실제 챕터에는 Plc(4.3)·BlackBody/RecipeEngine(4.4)만 있고 Nats·Serial 전용 하위 섹션이 없음(4.6은 토픽 매핑 설명이지 Nats 연결설정이 아님). -> 4.5(agent.json)의 NatsUrl과는 별개로, hardware.json 쪽 Nats/Serial 필드(주소·포트·baudrate 등)를 다루는 절을 추가하거나, 없다면 참고 문구에서 Nats/Serial 언급을 삭제.
2. [4.1 파일 위치 표] 문제: manager-settings/state.json이 표에만 나열되고 본문 어디에도 필드 설명 절이 없음(hardware.json·agent.json은 각각 4.2~4.6에서 필드가 설명됨). -> "4.7 manager-settings/state.json" 절 신설하거나, 필드가 없다면 그 사실을 명시.
3. [4.3 주의 박스] 문제: ServoSpeedPercent(D2560), BitEmergencyStop(M2000)이 주의 문구에만 등장하고 4.3 표에는 해당 필드 자체가 없어 독자가 어느 섹션 값인지 찾을 수 없음. -> 표에 두 필드를 추가하거나, 표에 없는 필드임을 명시적으로 안내.
4. [4.3 Admin* 행] 문제: "Admin*(과열상한/경계/딜레이/MFC)"가 실제 JSON 필드명을 밝히지 않고 그룹으로만 표기되어 설정 관리자가 hardware.json에서 어떤 키를 찾아야 할지 알기 어려움. -> AdminOverheatUpperLimit 등 실제 필드명을 개별 행으로 나열 권장.
5. [전체] 문제: "설정 파일 레퍼런스" 챕터인데 파일을 직접 편집할 때의 주의사항(백업, JSON 문법 오류 시 동작, 수정 후 앱 재시작 필요 여부, 인코딩)이 전혀 없음. -> 파일 수동 편집 절차/주의 박스 1개 추가 제안.

## 누락/추가 제안
- BitJogX±/BitJogY± 행에서 X축(P745/746)·Y축(P725/726) 중 어느 비트가 +/- 인지 표기 없음 — 각각 +/- 매핑 명시 필요.
- data.db는 참고 박스로 내용만 설명되고 스키마/컬렉션 목록은 없음 — 상세까지는 불필요해도 "스키마는 자동관리, 직접 편집 금지" 정도 경고는 추가 가치 있음.

## 이미지 자리 검토
- 없음 (이 챕터에는 📷 [그림 N] 자리가 하나도 없음). 표 위주 레퍼런스라 필수는 아니나, hardware.json 실제 파일 스크린샷 1장 정도는 설정 관리자에게 유용할 수 있음(선택 사항).
