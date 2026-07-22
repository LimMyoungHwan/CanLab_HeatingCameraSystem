# Recipe Design — SimulationMode QA 증거 (S1–S6)

- 일자: 2026-07-22
- 대상: 레시피 디자인 확장(위치별 카메라/XY/챔버온습도/흑체온도 + 라이브 열화상 프리뷰 + XY 이동 + COM↔카메라 매칭 + CL 시리얼 제어)
- 모드: `hardware.json` `SimulationMode = true` (전 Fake 서비스, 하드웨어 0)
- 빌드/테스트: `dotnet build` 9개 프로젝트 0 에러 0 경고 · `dotnet test` 86개 통과

## 검증 방법
1. **앱 부팅**: `HeatingCameraSystem.Master.exe` (SimulationMode) 실행 → 창 정상 표시, 크래시 없음. 부팅 성공 = `AppServices.Initialize`(전 Fake 배선) + XAML 3열 레이아웃 로드 + `RecipeEditorViewModel` 생성자(`RefreshPairings`→FakePairing, poll timer, `ReadStatusAsync`) 전부 예외 없이 통과.
2. **엔드투엔드 드라이버**: UI와 동일 경로로 SimulationMode Fake를 통합 실행(임시 xUnit 드라이버, 검증 후 제거).
3. **T14 시각 QA**(별도 에이전트): 3열 렌더, 640×480 애니메이션 프리뷰, COM7/COM8 페어 표시, 바인딩 오류 없음 확인.

> 실행 환경 주: 본 오케스트레이터의 셸은 비대화형 윈도우 스테이션에서 동작해 대화형 데스크톱의 스크린샷/클릭 캡처가 불가했음. 이에 통합 드라이버(관측 가능한 값 출력) + 부팅 성공 + T14 시각 QA로 행위 검증을 대체함.

## 시나리오 결과 (통합 드라이버 실측)
| ID | 시나리오 | 관측 결과 | 판정 |
|----|----------|-----------|------|
| S1 | 레시피 위치 영속(LiteDB 왕복) | name='QA Recipe' X=100 Y=200 챔버T=25.5 챔버H=40 흑체=30 cam=1 | PASS |
| S2 | 라이브 Y16 프리뷰(Fake 카메라) | 800ms에 11프레임(~14fps), 640×480 | PASS |
| S3 | 직접 XY 이동(Fake PLC) | MoveToCoordinateAsync(1234,5678) → ServoX=1234 ServoY=5678 | PASS |
| S4 | COM↔카메라 매칭(실 페어링 서비스 + Fake 열거자) | 2페어: A idx0↔COM7 SN000100001 Paired / B idx1↔COM8 SN000100002 Paired | PASS |
| S5 | CL 시리얼 제어(Fake) | IsOpen=True, SN=000100001, FPA=30.80°C, 셔터+START ok | PASS |
| S6 | 회귀(기존 화면 + 전 테스트) | 86개 단위테스트 통과, RelayCommand<int> 크래시 없음 | PASS |

## 단위 테스트 커버리지(행위 로직, 전부 Fake 대상)
- S1 `RecipeStepPersistenceTests` · S2 `LiveThermalCameraTests` · S3 `FakePlcControllerTests`+`RecipeEngineTests` · S4 `CameraComPairingServiceTests` · S5 `CameraSerialClientTests` · CL 프로토콜 `ClPacketTests`(골든) · USB 토폴로지 `UsbTopologyTests`.

## 남은 검증(실장비 필요)
아래 "실HW 런북" 참조 — 실제 CLTC_T_VGA 2대 + COM7/COM8 + PLC 연결 후 S1–S5 재검증 및 플레이스홀더 주소 확정.
