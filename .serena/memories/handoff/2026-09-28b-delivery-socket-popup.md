# 인계 — 2026-09-28 오후 (전달 빌드 / 반복 오류 창)

HEAD `5ad6193`, origin 동기화. build 0/0 · tests 490 passed.

## 오늘 커밋 (오전 인계 `2026-09-28-blackbody-plc-detach.md` 이후)
- `8ad2319` fix(recipe): 저장 루트 없이 촬영 스텝 있으면 실행 전 경고 (오전 인계 P1).
  알람 코드는 기존 `ProductionNamingFailed` 재사용 — 전용 코드 분리는 보류.
- `5f65afe` docs(ref): `참고/` 전체 추가 (util 소스, BB70_000.raw, Y16 PDF).
  util은 기존 카메라 뷰어 참고 소스의 사본. 중첩 `.git`은 로컬에만 두고 파일만 커밋.
- `5ad6193` docs(ref): `참고/util/opencv-win/x64` DLL·CMake 강제 추가 (루트 `.gitignore`의 `x64/`에 걸림).
  이 폴더에 새 파일 추가 시 `git add -f` 필요.

## 사용자 보고: 켜자마자 "화면 처리 중 오류" 창 반복
- 문구: `One or more errors occurred. (연결된 구성원으로부터 응답이 없어…)` = AggregateException + SocketException 10060.
- 원인: 흑체 SR UDP 수신 타임아웃(`UdpSrLink.cs:29`)을 **UI 스레드 `.Wait()`**로 기다린 코드.
  운영자가 임시로 넣었던 흑체 직접 재판독이며 git에 커밋된 적 없음 → 사용자 빌드는 운영자 로컬 코드로 만든 것.
  `70df78b`에서 이미 제거. 현재는 `PlcStatusService.PollBlackBodyAsync`(:139-151)가 잡아 빨간 경고로만 표시.
- 조치: `publish.ps1`(framework-dependent)로 재빌드 → `HeatingCameraSystem_20260928_5ad6193.zip` 전달 완료
  (소스 포함). 로컬 스모크: Master 15초 실행, 오류 창 0.

## 내일 확인
1. 사용자 PC에서 오류 창 사라졌는지.
2. 흑체 카드 **빨간 경고** 뜨는지 — 뜨면 문구 받기. 흑체 전원·네트워크·`hardware.json` 흑체 IP/포트 확인.
   (근본 문제는 여전히 흑체 무응답일 가능성 높음. 오전 인계 1절의 "poll 안 SR 판독 실패"와 같은 건일 수 있음.)
3. 오전 인계 7절 실기 확인 항목 (흑체 1초 갱신, raw 리플레이, 팬 D350, CSV 엑셀 왕복, P252 리셋).
4. 레시피 내용 P2~P5 운영자 확인 (오전 인계 6절).

## 제안 보류
- `App.OnDispatcherUnhandledException`에서 예외 전체를 `%LOCALAPPDATA%\HeatingCameraSystem\error.log`에 기록.
  이번처럼 문구만으로 원인 추적하는 일 방지. 사용자 승인 전 미적용.
- 같은 예외 반복 시 창 스팸 억제 — 요청 없음.
