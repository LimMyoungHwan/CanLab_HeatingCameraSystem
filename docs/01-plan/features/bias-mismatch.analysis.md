# BIAS MID 결과 불일치 조사

- 최초 조사일: 2026-10-21. **1~5절은 수정 전 소스에 대한 읽기 전용 조사 기록**이며 파일:행도 당시 기준이다. 이후 승인된 실패 판정·측정 안정화 수정은 6절에 기록한다. 목표값/로컬 레시피/백업은 변경하지 않았고 커밋하지 않았다.
- 고객 제공 관측: 챔버 10℃, S/N 536513178, FPA 28.6℃, Y16. UNI004d는 LSB `66`, 중심 약 4893/표준편차 68.8; 당사 화면은 `70`, “오차 1538”, 결과 분포 약 7000~7100. 해당 실행의 명령·프레임별 측정 로그/바이너리는 미확보했으므로 실행 원인의 단정은 피한다.
- 아래 `L` = `%LOCALAPPDATA%/HeatingCameraSystem`. `[INFERENCE]`는 소스/보관 자료로 직접 확정할 수 없는 판단이다.

## 1. 목표값: 기본값과 저장된 값은 다르다

| 경로 | 실제 정책/값 | 근거 |
|---|---|---|
| 공통 모드 기본값 | LOW **8500**, MID **5000**, HIGH **5000** | `HeatingCameraSystem.Core/Models/CameraControlMessages.cs:69-85` |
| Master 편집기 | BIAS LOW/MID/HIGH **operation 변경 시** 기본값 자동 입력 | `HeatingCameraSystem.Master/ViewModels/RecipeEditorViewModel.cs:150-155` |
| 기존 레시피 불러오기 | 저장값이 양수면 보존, 아니면 모드 기본값. 초기화 순서상 operation 콜백 후 저장값으로 다시 덮는다 | 같은 파일 `:845-883` |
| 저장·실행 | 편집값을 저장하고, 엔진이 `step.BiasTargetLevel`을 NATS 명령에 그대로 전달 | 같은 파일 `:791-816`; `HeatingCameraSystem.Master/Services/RecipeEngine.cs:843-850` |
| AgentUI 수신 | 양수는 override, 0 이하이면 null → 모드 기본값 | `HeatingCameraSystem.AgentUI/App.xaml.cs:208-234`; `HeatingCameraSystem.AgentUI/ViewModels/CameraPanelViewModel.cs:469-486` |
| AgentUI 직접 버튼 | CommandParameter가 없어 MID는 **5000**. 레시피의 8500을 참조하지 않음 | `HeatingCameraSystem.AgentUI/MainWindow.xaml:318-320`; 위 ViewModel `:470-476` |

추가로 엔진은 타겟 `TargetChamber`가 있으면 BIAS operation의 대역을 타겟에 맞춰 다시 결정하지만 **수치 목표는 바꾸지 않는다** (`RecipeEngine.cs:945-960`, `:849`). 따라서 LOW에서 가져온 8500을 MID 타겟에 남겨둘 수 있다. 챔버 실측 10℃나 FPA 28.6℃를 목표 레벨로 환산하는 경로는 이 BIAS 호출에 없다. 콘솔 `HeatingCameraSystem.Agent`는 보조/진단 호스트이고 카메라 BIAS 제어를 구독하지 않는다. 여기서 “Agent 기본값”은 실제 주 호스트인 AgentUI의 값이다 (`HeatingCameraSystem.Agent/Program.cs:18-29`, `:82-102`).

### MP10의 8500은 언제 생겼나?

- 현재 `L/recipe/052e8bd7-e2bc-40dc-b37d-1146bf934377.json:3`은 MP10. `:187`은 `biasMid`, `:207`은 **8500**.
- `L/recipe bak/MP10_20261001_142218.json:11-84`에는 챔버/흑체 두 스텝만 있고 BIAS가 없다. 다음 보관본 `MP10_20261001_142418.json:159,179`에는 이미 `biasMid` + **8500**이 있다. `MP10_20261001_145041.json:187-207`도 MID 타겟과 8500을 보존한다. 즉 **BIAS 스텝이 처음 확인되는 보관본부터 8500**이며, 뒤늦은 이번 실행의 자동 기본값 선택 때문에 생긴 값은 아니다.
- 백업 파일명 시간은 해당 내용의 생성 시간이 아니라 **기존 파일을 다음 저장 직전에 백업한 시간**이다 (`HeatingCameraSystem.Master/Services/FileRecipeRepository.cs:71-76`, `:93-105`). 따라서 입력의 정확한 시각/입력자를 복원할 수는 없다.
- `git log --all -p` 확인: `CameraBiasDefaults`는 **dd65e19 (2026-09-07)**에 처음 생겼고, 그때부터 LOW=8500/MID=5000/HIGH=5000이다. 같은 커밋이 편집기 자동입력을 추가했다. 이전 **baffef9 (2026-09-02)**의 AgentUI override 기본값도 MID=5000, 최초 BIAS **611fff0 (2026-08-31)**도 MID 범위 4900~5100이다. **확인된 Git 이력에는 MID 기본값 8500이 없다.**
- 결론: “당사 MID 기본값이 원래 8500이었다”는 설명은 근거가 없다. `[INFERENCE]` 수동 입력, LOW 레시피/스텝 복사·가져오기, 비추적/다른 배포본 중 하나가 후보이나 보관본에는 사용자 조작 이력이 없어 구분 불가. 양수 저장값을 보존하는 현재 경로는 8500이 계속 사용되는 이유를 설명한다.
- `[INFERENCE]` **해당 실행이 MP10 레시피였다면 목표는 8500일 가능성이 가장 높다.** AgentUI 직접 MID 버튼만 눌렀다면 목표는 5000이므로 두 실행 경로를 동일시하면 안 된다. 로컬에는 별도로 `BiasSerch` MID=5000 레시피도 있다 (`L/recipe/16aa9a3f-c53e-4c2e-ae68-82386b9a1fad.json:3,15,35`).

## 2. 당사 탐색: 미수렴도 “완료”로 표시한다

실제 실행 경로는 `CameraPanelViewModel.RunAutoBiasAsync` → **`FindBiasInRangeAsync`** 이다 (`:483-499`). 같은 파일의 `FindBiasAsync(:562-591)`는 별도의 단순 이진탐색 helper이며 실제 BIAS 버튼의 호출 대상이 아니다.

- 고정 레지스터: TINT=`0x0273`, GSK_MSB=`01`, GFID=`AE`; CINT LOW=`93`/MID=`A3`/HIGH=`D3`. **탐색하는 것은 GSK_LSB 한 바이트(0~255)뿐**이며 MSB를 올리거나 내리지 않는다 (`CameraPanelViewModel.cs:455-494`). 시리얼 DETECTOR main=`00`; GFID sub=`04`, GSK_MSB=`05`, GSK_LSB=`06`, TINT_MSB/LSB=`07/08`, CINT=`09` (`HeatingCameraSystem.Protocols/Cameras/CL/ClIds.cs:4-9,21-34`; `ClSerialCameraClient.cs:71-88`).
- 측정값은 **전체 프레임의 `(pixel & 0x3FFF)` 산술평균**을 5회 구한 뒤 그 **시간축 중앙값**이다. 픽셀 중앙값/히스토그램 피크/중앙 ROI가 아니다. `_nuc.Apply` 없이 런타임 원본을 쓴다 (`CameraPanelViewModel.cs:594-617`; `HeatingCameraSystem.Protocols/Cameras/CL/ClThermalMatDecoder.cs:32-44`). “raw”는 당사 소프트웨어 NUC 미적용이라는 뜻이며 카메라 내부 보정 여부는 별도 확인 대상이다.
- 순서: ① `00`/`FF` 측정으로 증가·감소 방향 추정 ② `00,10,...,F0` **16개 coarse** 측정 ③ 가장 오차가 작은 coarse가 속한 `N0..NF` **step 1 fine** 측정 ④ 전 구간 `0..255` **이진탐색** 보완. 측정값은 LSB별 캐시하여 재방문 때 재측정하지 않는다 (`:516-558`).
- 허용범위는 **목표 ±100**. 오차는 `max(target−100−level, level−(target+100), 0)` 즉 **목표 중앙까지가 아니라 가까운 허용 경계까지의 거리**이다 (`:485-486,535-538`). 이진탐색은 오차 0 또는 `low > high`에서 종료한다. 별도 max-iteration 상수는 없고 256칸 이진 분할이므로 반복 수는 유한(최대 9회)이다 (`:547-558`).
- 탐색 종료 후 최선값을 다시 쓰지만 **안정화 후 재측정/레지스터 readback/오차 검사 없이** JSON을 만들고 항상 “BIAS 완료”라고 표시한다 (`:496-499`). NATS 핸들러도 예외가 없으면 `(true,"ok")`를 반환한다 (`App.xaml.cs:233-238`). 따라서 **오차 1538은 성공적 수렴이 아니라 허용범위 미도달**인데도 성공처럼 처리된 것이다.

### 왜 `70`에서 끝났나?

`70`은 허용 LSB 최솟값/최댓값이 아니므로 **직접적인 LSB 범위 끝 도달 증거는 아니다**. 이 코드는 전수탐색이 아니며, coarse에서 가장 가까운 지점의 **오른쪽 nibble만** fine 탐색하고 이진 보완은 끝점으로 추정한 **전역 단조성**을 가정한다 (`:530-557`). `[INFERENCE]` 비단조/포화/랩어라운드 응답, 잘못 추정된 방향, 프레임 반영 지연 등이 있으면 더 좋은 값을 놓칠 수 있다. 다만 정상적인 안정 단조 응답이라면 이진 보완이 목표 주변을 찾아야 하므로 **8500 설정만으로 1538의 잔여 오차까지 설명되지는 않는다.**

측정 안정화도 참고 Python과 다르다. 당사는 write 직후 다음 프레임부터 5장, 매 장 뒤 30ms이며 **첫 측정 전 100ms 안정화가 없다** (`:594-617`). 런타임 snapshot은 2초 내 새 프레임이 없어도 예외가 아니라 마지막 프레임으로 폴백할 수 있고 BIAS 쪽은 프레임 신선도를 검증하지 않는다 (`HeatingCameraSystem.Protocols/Cameras/CameraRuntime.cs:102-145`). `[INFERENCE]` 지연/구 프레임이 LSB별 캐시에 고정되었을 가능성은 있으나 실측 로그 없이 이번 원인으로 확정할 수 없다.

**수치 주의:** 활성 경로에서 target=8500, 낮은 쪽 error=1538이면 당시 측정값은 **8400−1538=6862**이다. 질문의 `8500−1538=6962`는 비활성 helper처럼 중앙값 절대오차일 때의 계산이다. target=5000이라면 가능한 측정값은 3362 또는 6638이다. 최종 재측정이 없고 결과 캡처는 다른 시점/NUC 경로이므로 보고된 7000~7100과의 근접성은 정황이지 목표값의 독립 증명은 아니다.

## 3. 참고 Python·벤더 util 및 bias.json의 출처

- `참고/AISEN_CODE/main.py:1205-1216`의 목표도 LOW=8500±100, MID/HIGH=5000±100이다. `:1531-1574`의 TINT/CINT/MSB/GFID도 당사와 동일하다.
- 이름은 `_measure_median`이지만 **실제 코드는 전체 픽셀 평균**(`np.mean(..., dtype=np.int64)`)이며 픽셀 median 줄은 주석이다 (`:1233-1251`). 5프레임 평균값들의 median을 구하고, LSB write 후 **100ms 대기 + 그 측정을 5번** 하여 다시 median을 취한다 (`:1270-1290`). 유효 프레임이면 LSB당 최대 25프레임. 통계 정의를 “당사는 mean, 참고는 pixel median”이라고 구분하면 잘못이다.
- Python도 `00/FF`로 방향을 판단하지만 coarse는 목표에 **접근하는 쪽**을 선택하고 목표 통과 시 중단한 뒤 그 nibble을 step 1로 탐색한다 (`:1308-1460`). 범위 미도달이면 가장 가까운 LSB를 적용하되 **success=False** (`:1498-1529`); 이때 새 `bias_dict`를 저장하지 않는다 (`:1587-1599`). 당사의 무조건 완료와 차이가 있다.
- `참고/util`은 수동 BIAS read/write 및 저장/불러오기 UI가 있고 (`Viewer.cpp:536-590,832-861`), 히스토그램은 전체 픽셀 `&0x3FFF`, **16384개 bin**이다 (`Histogram.cpp:13-32`). 조사한 이 소스에는 LOW/MID/HIGH 자동목표·자동탐색이나 “Pixel Intensity Center” 정의가 없다. `UNI004d` 및 정확한 해당 문구는 `참고`/`docs` 검색에서 찾지 못했다. **Python/Qt util과 UNI004d가 동일 구현이라는 보장은 없다.** UNI004d의 정확한 center 계산은 미확인이다.

**당사 bias.json 흐름:** 최종 선택 `best`를 `SetBiasAsync(best)`한 직후 `BuildBiasJson(cint,best)` → `LastBiasJson` (`CameraPanelViewModel.cs:496-514`) → AgentUI가 해당 패널의 문자열 제공 (`App.xaml.cs:247-248`) → cold 생산캡처 때 전달 (`HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs:338-344`) → `ProductionCaptureSink.WriteBiasJson`이 **문자열 그대로 덮어쓰기** (`:108-114`). **고정 66이나 최초값을 기록하는 코드는 없다.** 다만 실제 레지스터를 다시 읽은 값이 아니라 마지막 검색 결과의 메모리 캐시다.

Python도 성공한 최종 LSB를 동일 키/hex 형식으로 `bias_dict`에 넣고 cold 조건 계층에 쓴다 (`main.py:1593-1599,1035-1065`). 따라서 제시된 `{CINT:A3,TINT:273,GSK_MSB:1,GSK_LSB:66,GFID:AE}`는 **형식만으로 당사/참고 출처 판별 불가**다. `[INFERENCE]` 고객 참고 실행 결과 66과 일치하므로 참고 파일일 가능성은 있으나 단정할 수 없다. **같은 카메라·같은 패널 생존기간·70 완료 직후의 cold 캡처 파일**이 확실한데 JSON이 66이면 정상 경로와 모순이므로 stale/다른 실행·폴더·카메라/동시 실행·구버전 배포 여부를 조사해야 한다. 기존 파일의 존재나 캐시만으로 현재 하드웨어 상태를 보증하지 않는다.

## 4. 레시피 결과 histogram은 생산 .raw와 같지 않다

- Master는 선택 결과의 **`AgentRawPath`**를 Agent에서 받거나 로컬 캐시에서 읽어 히스토그램을 만든다 (`HeatingCameraSystem.Master/ViewModels/RecipeResultViewModel.cs:73-81,142-175`). 생산 BB 폴더의 `.raw`를 직접 읽는 경로가 아니다.
- Agent는 캡처 snapshot에 **소프트웨어 NUC를 적용한 frame**을 대표 `.y16`로 저장한다. 생산 `.raw`에는 반대로 **NUC 미보정 snapshot**을 보낸다 (`CameraNatsConnector.cs:275-301,324-332`). `.raw`만 픽셀(0,0)에 FPA raw 값을 심는다 (`HeatingCameraSystem.Protocols/Cameras/RawCaptureWriter.cs:34-40`).
- Master는 LE16 픽셀을 **256개 bin/0~16383**에 누적하며 상위비트를 마스킹하지 않고 상한 초과를 마지막 bin에 모은다 (`HeatingCameraSystem.Master/Services/RawHistogram.cs:17-32`). 정상 Y16 입력은 이미 디코더에서 14비트 마스킹된다. 표시 X는 `(bin+0.5)×64`이며 “화소값 (14bit)” 축이다 (`HeatingCameraSystem.Master/Views/RecipeResultView.xaml.cs:49-56`). **8bit 정규화나 +2000 가산은 없고, 64 DN 폭의 binning 차이는 있다.**
- NUC는 flat의 픽셀별 `pixel−전체평균`을 빼는 zero-centered offset 방식이다 (`HeatingCameraSystem.Protocols/Cameras/ThermalNucCorrector.cs:47-53,66-85`). `[INFERENCE]` clipping/dead-pixel 교체가 작으면 전체평균은 대체로 보존되므로 일정한 **+2000 offset을 만드는 설계는 아니다**. 다만 분포 폭·피크는 달라질 수 있어 같은 종류의 raw/NUC, 동일 시각·장면·ROI로 비교해야 한다.
- 참고 Python의 histogram은 `&0x3FFF` 후 16384-bin 전체 분포이고 정규화하지 않는다 (`main.py:470-482`); Qt util도 동일 범위다. **당사 histogram과 단위는 비교 가능하나 입력 프레임/보정/bin 폭은 다르다. UNI004d center와의 통계적 동일성은 미확인**이다. `[INFERENCE]` 고객의 좁은 단봉 분포(std 68.8)라는 조건에서 평균/피크 차이만으로 약 2000 DN을 설명하기보다는 목표/실제 레지스터 차이를 먼저 보는 편이 타당하다.

## 5. 우선순위 결론·조치 선택지

1. **최우선: MP10에 명시 저장된 MID=8500과 고객 기대 MID≈5000의 불일치.** 저장·백업·전달 경로로 확인된다. `[INFERENCE]` MP10 실행이라면 다른 목표를 추적한 것이 중심/LSB 차이의 주원인 후보다. **현재 공통 MID 기본값 자체는 이미 5000이므로 기본값만 고쳐서는 기존 레시피가 바뀌지 않는다.** 고객 확인 후 MP10 및 실제 사용 MID/HIGH 스텝·연결 템플릿의 양수 저장값을 5000으로 명시 수정하는 방안이 우선이다. LOW 8500까지 일괄 변경해서는 안 된다.
2. **확정된 별도 결함: 미수렴(1538)을 성공 완료로 처리.** 오차 0/최종 안정 프레임 재측정에 의한 성공 판정을 분리하고, 범위 밖이면 경고/실패 ACK를 보내도록 하는 방안. 현재는 목표를 올바르게 해도 자동탐색 실패를 감춘다.
3. **[INFERENCE] 탐색/반영 타이밍 문제는 잔여 오차 후보.** LSB별 안정화·신선 프레임 폐기/대기, 최종 readback+재측정, 방향/포화/단조성 확인 후 bracket 양쪽 fine 또는 필요 시 0~255 전수탐색을 고려한다. 실기에서 `(LSB, 실제 readback, 평균, timestamp)`를 확인해 선택할 문제이며 무조건 MSB까지 확장할 근거는 없다. 통계를 pixel median으로 바꾸는 것은 **참고 Python과 맞추는 수정이 아니며** UNI004d 정의가 확인될 때만 검토한다.
4. **[INFERENCE] histogram 표시만의 +2000 오차는 우선순위 낮음.** 현재 코드에는 그런 스케일/오프셋이 없다. 다만 `.y16`/`.raw` NUC 차이와 JSON 출처 불명은 분리해 확인해야 한다.

### 고객/현장 확인 항목

- UNI004d **LOW/MID/HIGH별 목표와 허용범위**, 챔버 온도별로 목표를 바꾸는지. 특히 10℃ MID의 5000 기대를 명시 확인.
- “Pixel Intensity Center”가 전체평균/픽셀 median/최빈값/ROI 중 무엇인지, 내부 NUC/AGC 여부와 실제 Y16 14bit 해석.
- 문제 화면이 **MP10 레시피 명령인지 직접 MID 버튼인지**, 당시 로드된 템플릿/목표, 실행 PC의 Master·AgentUI 빌드 식별값.
- `66` bias.json의 전체 경로·파일 시각·생성 프로그램·카메라/실행 대응관계. 같은 조건에서 `66`/`70` 적용 후 **동일 원본 .y16/.raw**와 레지스터 readback·프레임 시각을 확보하면 목표 문제, 탐색 문제, 표시 문제를 분리할 수 있다.

## 6. 승인된 수정 (목표값 변경 제외)

- **대상/탐색 순서 유지:** LOW=8500, MID/HIGH=5000 기본값과 레시피 override, coarse→fine→binary 순서는 그대로다. MP10의 저장된 8500도 변경하지 않았다.
- **실패 판정:** 활성 `FindBiasInRangeAsync`가 최선 LSB/측정 레벨/허용 경계 오차/성공을 반환한다. 최선 LSB는 실패 시에도 적용하되 최종 재적용 후 안정 프레임을 다시 측정하고, 범위 밖이면 `BIAS 실패: 0x.. (측정 N, 목표 T±100)`로 표시한다. 이 상태는 성공 JSON을 만들지 않는다. 직접 버튼에서도 예외를 상태/로그로 처리한다.
- **ACK→레시피:** AgentUI 카메라 제어 핸들러가 BIAS의 실제 성공 여부와 상태 문구를 반환한다. `CameraNatsConnector`가 실패 ACK를 발행하고, `RecipeEngine.ExecuteCameraStepAsync`가 거절 ACK를 timeout과 구분해 원문 메시지가 포함된 `execution.Fail`/경고를 남긴다. 실패 스텝 영수증으로 전달되는 경로를 회귀 테스트로 연결했다.
- **오래된 bias.json 방지 정책:** 탐색 시작 즉시 `LastBiasJson=null`, 성공 여부=false로 무효화한다. 최종 성공 때만 새 JSON을 설정한다. 유효한 결과가 없는 cold 생산캡처도 **촬영/저장/전송은 정상 진행하고 bias.json만 생략**한다. AgentUI Warning 로그와 카메라 패널 촬영 상태에 생략 이유를 표시한다. 이미 저장된 과거 파일은 삭제하지 않지만 로컬 버퍼의 이전 JSON을 이번 촬영으로 전송하지 않는다. 유효한 cold 캡처는 시작 시 JSON을 한 번 고정해 배치 끝에도 같은 값을 쓴다.
- **측정 안정화:** LSB write 후 100ms 대기하고 안정화 종료 시각보다 새로운 timestamp만 채택한다. 같은 timestamp의 프레임 재사용도 제외한다. 5개 신선한 원본 프레임 전체평균의 시간축 median과 장간 30ms는 유지했다. 각 샘플에서 2초 동안 신선한 프레임이 없으면 실패한다. 이 규칙은 런타임의 오래된 프레임 timeout 폴백도 거절한다. 단, 소스 timestamp는 호스트 수신 시각이므로 센서 노출/드라이버 버퍼의 모든 지연을 증명하지는 못한다.
- **로그:** 기존 AgentUI Serilog에 매 측정 LSB→레벨, 최종 모드/목표/최선 LSB/레벨/경계 오차/성공을 기록한다. 예외도 실패 원인과 목표를 남긴다. 콘솔 Agent에는 별도 BIAS 구현이 없음을 확인했고 테스트만 부르던 비활성 `FindBiasAsync`를 삭제하여 활성 탐색을 직접 테스트한다.
- **회귀 테스트 추가:** 활성 탐색 성공/미수렴 경계 오차, write 후 오래된 프레임이 먼저 오는 가짜 snapshot 공급자에서 올바른 LSB 도달, 성공 후 실패 시 캐시 무효화·실패 문구·최선값 적용, stale-only timeout 실패, cold JSON 누락/잘못된 캐시 시 경고·촬영 성공·JSON 미생성/미전송 및 유효 배치 snapshot, 실제 connector 실패 ACK→레시피 실패 영수증/경고.
- **초기 구현 검증 이력:** 빌드 경고 0 / 오류 0, 전체 662 통과 / 0 실패 / 0 건너뜀. CS8619는 `Task.FromResult<ThermalFrame?>(frame)` 명시로 억제 없이 수정했다. 이후 cold 호환성 정책은 아래 7절로 정정했다. 실카메라/고객 UNI004d 대조 시험은 수행하지 않았다.

## 7. cold 캡처 호환성 정정

- 최초 추가한 cold preflight 거절은 기존 동작을 바꾸는 범위 초과였으므로 제거했다. 수정 전에는 `LastBiasJson=null`이어도 캡처 성공 여부는 촬영 결과로 결정되고 JSON만 Debug 로그와 함께 생략됐다. 현재도 fresh session 및 BIAS 없는 LN10/LP10/MP25/MP40/RP50/RP60 실행이 **BIAS 캐시 부재만으로 실패하지 않는다**.
- 기존 성공 캐시는 같은 패널에서 다음 BIAS 시도 전까지 다른 레시피에서도 재사용된다. 실패/예외 BIAS 시도는 캐시를 비우므로 다음 cold에서 JSON은 경고와 함께 생략되지만 촬영은 정상 진행한다. 실패 BIAS의 새 JSON이나 이전 메모리 캐시를 쓰지는 않는다.
- `CaptureResultMessage`에는 Message/Warning 필드가 없고 일반 생산캡처는 카메라 제어 ACK를 발행하는 경로가 아니다. 새 NATS 채널/필드를 만들지 않고 로컬 콜백으로 **기존 AgentUI 로그 + CaptureStatus**에 경고를 표시한다. Master에는 기존 성공 캡처 결과를 보낸다.
- JSON은 캡처 시작 시 snapshot으로 고정한다. 배치 도중 캐시가 바뀌어도 이미 고정한 값을 쓴다. snapshot이 없으면 이미지 전송은 수행하되 조건 폴더 bias.json 전송만 생략하여 이전 전송 실패로 로컬 버퍼에 남은 파일도 재전송하지 않는다. 기존 Master/버퍼 산출물은 삭제하지 않는다.
- 이전 preflight 실패 테스트는 경고 1회, 성공 결과, 촬영/대표 저장/RAW 전송, JSON 미생성 또는 과거 파일 불변·미전송을 검사하도록 바꿨다. 캐시 null + 과거 JSON 없는 경우도 추가했다. 탐색 실패·캐시 무효화·snapshot·ACK 실패 회귀는 유지한다.
- **정정 후 검증:** `dotnet build --no-restore -m:1` **경고 0 / 오류 0**. `dotnet test HeatingCameraSystem.Tests/HeatingCameraSystem.Tests.csproj --no-build --no-restore -m:1 --logger "console;verbosity=minimal"` 전역 병렬 비활성 전체 실행 **663 통과 / 0 실패 / 0 건너뜀**, 1분 26초. 목표값/로컬 레시피는 변경하지 않았고 커밋하지 않았다.
