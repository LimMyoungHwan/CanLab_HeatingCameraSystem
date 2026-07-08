# 카메라 연결 검증 — 참고 자료 분석 (2026-06-29)

## 배경
`참고/` 폴더(untracked, git 미추적)에 벤더 제공 Python 레퍼런스 코드 2종 발견.
목적: 열화상 카메라 실물 연결 확인 시 참고용.

## 참고 폴더 구성

```
참고/
├── AISEN_CODE/          ← 원본 zip 포함
│   ├── main.py                    (1829 lines, Tkinter GUI 전체 테스트 툴)
│   ├── serial_code.py             (SerialComm — 포트 open/close/send/receive)
│   ├── serial_rw.py               (SerialReadWrite — serial num, FPA temp, bias, shutter)
│   ├── two_point_viewer.py        (31448 bytes)
│   ├── requirements.txt           (pyserial, opencv-contrib-python, tkinter 등)
│   └── core/serial/data_packet.yaml  (프로토콜 스펙 v2.0)
└── FPV_code/            ← 위와 동일 구조, 원본 zip 포함
    ├── main.py                    (498줄 diff — AISEN과 다른 변형)
    ├── serial_code.py             (AISEN과 100% 동일 — FC 바이너리 비교로 확인)
    ├── two_point_viewer.py        (31684 bytes, 약간 다름)
    └── core/serial/data_packet.yaml  (AISEN과 100% 동일)
```

- `data_packet.yaml`은 두 폴더 동일 → 동일 프로토콜, `main.py`만 카메라 모델별 GUI 변형으로 추정.
- `main.py`가 `from biassetting import setting` 임포트하지만 **biassetting.py가 두 폴더 모두 없음** → 그대로 실행 시 ImportError. 필요 시 벤더에 요청.

## 시리얼 프로토콜 스펙 (data_packet.yaml v2.0)

```
HEADER = [0x43, 0x4C]  ('C','L' = CANLAB)
패킷 = [HEADER(2), MAIN_ID(1), SUB_ID(1), RW(1), reserved(1), DATA(1)]  총 7바이트

MAIN_ID: DETECTOR=0x00, NUC=0x10, USER_CONFIG=0x20, OPERATE_CTRL=0x30, DEBUG=0xF0
RW: WRITE=0x00, READ=0x01

셔터 제어 (OPERATE_CTRL.SHUTTER, sub_id=0x01):
  OPEN  → [0x43,0x4C,0x30,0x01,0x00,0x00,0x01]
  CLOSE → [0x43,0x4C,0x30,0x01,0x00,0x00,0x00]

Baud rate: 115200 (serial_code.py 하드코딩)
```

## ⚠️ 중요 — 현재 프로젝트 코드와 불일치 발견

`HeatingCameraSystem.Protocols/SerialShutterController.cs`:
```csharp
_openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
_closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
```

vs 참고 프로토콜(위 data_packet.yaml 기준):
```
OPEN  = 43 4C 30 01 00 00 01
CLOSE = 43 4C 30 01 00 00 00
```

→ **완전히 다른 바이트 시퀀스.** 현재 `_openBuffer`/`_closeBuffer`는 AGENTS.md에 "실제 카메라 셔터 프로토콜"로 기록되어 있지만, 참고 자료(벤더 스펙)와 헤더부터 다름.

또한 `HeatingCameraSystem.Core/Models/CameraSerialSettings.cs` 기본 BaudRate=9600 vs 참고 코드 115200.

## 다음 확인 필요 (카메라 실물 연결 시)

1. 어느 프로토콜이 맞는지 실카메라로 검증 — `_openBuffer`(04 00 01...)가 진짜 동작하는 프로토콜인지, 혹은 data_packet.yaml(43 4C 30 01...)로 교체해야 하는지.
2. BaudRate 9600 vs 115200 실측 확인.
3. 맞다면 `SerialShutterController.cs` + `CameraSerialSettings.cs` 기본값 수정 필요 (AGENTS.md "알려진 플레이스홀더" 항목과 연관).
4. `serial_rw.py`의 `get_cam_serial()`, `get_fpa_temp()`, `set_temperature_bias_auto()` 등은 셔터 외 추가 기능(시리얼 번호 조회, FPA 온도, bias 설정) — 현재 C# 프로젝트에는 미구현. 필요 범위 확인.

## 관련 파일
- `mem:session-handoff` — SC-12 범위 2 PDCA 상태 (별개 진행 중 작업)
- `AGENTS.md` "시리얼 셔터 프로토콜" 섹션, "알려진 플레이스홀더" 섹션
