# PLC 순서 의존 접속실패 검토 (2026-10-13, 현장 확인 대기)

업체 보고: Master 먼저→PLC 나중이면 접속됨. PLC 먼저→앱 나중이면 접속 안 됨.

## 코드 확정 결함 (원본 대조 완료)
1. `Protocols/PlcXgtClient.cs:35-51` ConnectAsync — FEnet 검증 없이 `_isConnected=true`. `TcpChannel.Connected` 미확인.
2. `Master/Services/ConnectionMonitorService.cs:39,57` — 최초 점검 30초 뒤, `!IsConnected`일 때만 재시도, 백오프 최대 5분.
3. `Master/Services/AppServices.cs:185-209` — PLC 연결은 NATS 뒤, 실패 시 알람 1회 후 종료. 즉시 재시도 없음.
4. ConnectAsync 재연결 시 기존 _channel/_client 미Dispose → 소켓 누수.

## 순서의존 설명
- 앱 먼저: 초기 TCP 거부→예외→flag 깨끗→PLC 기동 후 재시도 성공.
- PLC 먼저: TCP 열림→낙관 true→첫 FEnet I/O 실패→30초 방치+누수 소켓 누적→(PLC 모듈 세션 꼬이면) 영구 실패처럼 보임. 모듈 측 좀비세션은 가설.

## 수정안 (미적용, 승인 대기)
ConnectAsync 검증 읽기+Connected 확인+실패 시 폐기 / 재연결 전 Dispose / 모니터 첫 tick 즉시 / PLC-NATS 병렬 연결.

## 현장 확인 필요 (다음에 물을 것)
- 시작 직후 알람 "PLC 연결 실패" 정확한 메시지
- 1~2분 대기 시 자가복구 여부 (복구=지연 문제 / 미복구=세션 꼬임)
- hardware.json PLC IP vs 실제 일치 여부
- 최초 부팅 vs 프로그램 재시작 중 어느 상황인지
