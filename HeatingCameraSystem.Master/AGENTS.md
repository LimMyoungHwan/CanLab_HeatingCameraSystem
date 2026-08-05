# HeatingCameraSystem.Master

## 범위

WPF 운영자 화면과 Master 런타임 조합 루트다. 공통 아키텍처와 하드웨어 계약은
루트 `AGENTS.md`와 `HeatingCameraSystem.Protocols/AGENTS.md`를 따른다.

## 시작과 종료

- 진입점은 `App.xaml.cs`다.
- `OnStartup`에서 `AppServices.Initialize()`를 먼저 호출하고 연결 시도를 시작한다.
- `AppServices`가 만든 PLC, NATS, 셔터, 흑체, 레시피, 상태 서비스를 화면이 사용한다.
- `OnExit`의 동기 대기는 알려진 기술 부채다. 명시적 작업 없이는 종료 시퀀스를 리팩터링하지 않는다.

## 서비스 등록

DI 컨테이너를 추가하지 않는다. 서비스를 추가할 때는 다음 두 곳을 함께 수정한다.

1. `Services/AppServices.cs`에 nullable 서비스 프로퍼티를 추가한다.
2. `AppServices.Initialize()`에 SimulationMode와 실제 하드웨어 구현을 각각 등록한다.

정적 상태를 사용하는 테스트가 있으므로 초기화·해제 순서를 바꾸면
`HeatingCameraSystem.Tests` 전체를 실행해 검증한다.

## ViewModel과 View

- 화면은 `Views/<Name>View.xaml` 및 선택적 `.xaml.cs` 쌍으로 둔다.
- 상태와 명령은 `ViewModels/<Name>ViewModel.cs`에 둔다.
- CommunityToolkit.Mvvm 명령·observable 패턴을 기존 ViewModel과 맞춘다.
- 백그라운드 PLC/NATS 콜백에서 ObservableCollection 또는 바인딩 속성을 직접 갱신하지 말고
  `Dispatcher`/`RunOnUi` 경로를 사용한다.

## 리소스와 알림

- 번역은 `Resources/Lang/ko.txt`, `Resources/Lang/en.txt`의 외부 파일을 사용한다.
- XAML 문자열은 `{loc:Loc Key}` 패턴을 우선 사용한다.
- PLC·NATS·카메라 오류는 화면별 임의 메시지보다 `AlarmSink`를 통해 기록한다.
- 테스트 출력에서 번역 키가 그대로 보이면 테스트 프로젝트의 리소스 복사 설정부터 확인한다.

## 주요 위치

- `App.xaml.cs`: WPF 수명주기.
- `Services/AppServices.cs`: 정적 서비스 조립 루트.
- `Services/RecipeEngine.cs`: 레시피 실행 오케스트레이션.
- `ViewModels/DashboardViewModel.cs`: PLC 상태·Agent 상태·라이브 프레임 화면.
- `Localization/LocalizationManager.cs`: 런타임 번역 로딩.
