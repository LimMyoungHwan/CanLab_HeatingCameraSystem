# HeatingCameraSystem — 개발 계획 (Development Plan)

이 문서는 64대 열화상 카메라 모니터링 및 레시피 기반 자동 촬영 시스템의 잔여 개발 계획을 관리합니다.

## 1. 💾 데이터 영구 저장 및 DB 연동 (Database)
- **목표**: 레시피 CRUD와 촬영 이력, 매핑 설정을 로컬 DB 파일에 영구 보존.
- **세부 작업**:
  - `LiteDB` 또는 `SQLite + EF Core` 패키지 솔루션 통합.
  - 레시피 모델(Recipe, RecipeStep) DB CRUD 기능 구현.
  - 카메라-위치 매핑 정보 저장/로드 구현.
  - 챔버 환경 정보와 영상 메타데이터(시간, 온도, 습도, 파일경로) 촬영 이력 DB 연동.

## 2. 📡 에이전트(Agent) NATS 통신망 연동 및 실구현
- **목표**: 마스터와 분산된 에이전트 PC 간의 실시간 제어 명령 및 상태 공유.
- **세부 작업**:
  - `HeatingCameraSystem.Agent` 콘솔 서비스에 `NatsCommunicationService` 탑재 및 데몬화.
  - 마스터의 `CaptureCommandMessage` (촬영 명령) 구독 및 처리.
  - OpenCV 기반 카메라 캡처 동작 시 연계하여 실제 프레임 저장 후 `CaptureResultMessage` 마스터 회신.
  - 주기적 하트비트 메시지(`AgentStatusMessage`)를 통한 에이전트 상태 보고 및 마스터 대시보드 리스트 실시간 동기화.

## 3. ⚙️ 마스터 UI와 실제 Recipe Engine & PLC 연동
- **목표**: 대시보드 조작 버튼(`START` / `STOP`)과 실제 물리 제어 루프 연동.
- **세부 작업**:
  - 대시보드 `START` 클릭 시 `RecipeEngine.ExecuteRecipeAsync` 가동.
  - `PlcModbusClient`를 통한 실제 Modbus TCP 포트 통신 및 챔버 온도/습도 설정 반영.
  - 챔버 실시간 PV(현재 온도/습도) 값을 Modbus로 수신하여 대시보드 하단 실시간 차트 업데이트.

## 4. 🧹 데이터 보존 및 삭제 정책 (Data Retention)
- **목표**: 디스크 부족 방지를 위해 보존 기한이 경과한 원본 이미지 파일의 주기적 자동 정리.
- **세부 작업**:
  - 에이전트 PC: 설정된 일수(예: 30일) 경과 시 백그라운드 파일 삭제 스케줄러 가동.
  - 마스터 PC: 사용자 옵션(무한 보존 또는 N일 이후 자동 삭제)에 따른 이력 보존 로직 추가.

## 5. 🔌 카메라 가상 시리얼 제어 및 하드웨어 세부 튜닝
- **목표**: 카메라 셔터 제어 프로토콜 연동 및 실제 현장 PLC 메모리 맵 매핑.
- **세부 작업**:
  - `System.IO.Ports`를 이용한 카메라 시리얼 셔터 개폐 및 상태 확인 기능 구현.
  - 가상 PLC 시뮬레이터 주소 명세를 반입 예정인 실제 A&D PLC Modbus Address Map(엑셀 명세)으로 치환 및 현장 튜닝.
