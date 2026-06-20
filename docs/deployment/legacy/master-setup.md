# Master PC 설치 가이드

## 사전 요구사항

- Windows 10/11 (64-bit)
- .NET 8 Runtime ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))
- NATS 서버 접근 가능 (같은 네트워크)
- PLC 네트워크 연결 (Modbus TCP)

## 설치 절차

### 1. .NET 8 Runtime 설치

```powershell
# winget 사용 시
winget install Microsoft.DotNet.DesktopRuntime.8
```

### 2. 빌드 및 배포

```powershell
# 개발 PC에서 빌드
dotnet publish HeatingCameraSystem.Master -c Release -o publish\Master

# Master PC로 publish\Master 폴더 복사
```

### 3. 최초 실행

```powershell
HeatingCameraSystem.Master.exe
```

최초 실행 시 `%LOCALAPPDATA%\HeatingCameraSystem\` 에 자동 생성:
- `hardware.json` — PLC, NATS, Serial 기본 설정
- `data.db` — LiteDB (빈 상태)

### 4. hardware.json 설정

```json
{
  "Plc": {
    "IpAddress": "192.168.1.100",
    "Port": 502
  },
  "Nats": {
    "Url": "nats://192.168.1.10:4222"
  },
  "Serial": {
    "PortName": "COM3",
    "BaudRate": 9600,
    "DataBits": 8,
    "Parity": "None",
    "StopBits": "One"
  }
}
```

> PLC IP, NATS URL을 실제 환경에 맞게 수정 후 재시작.

### 5. 방화벽 설정

| 포트 | 프로토콜 | 용도 |
|------|----------|------|
| 4222 | TCP (아웃바운드) | NATS 연결 |
| 502 | TCP (아웃바운드) | PLC Modbus TCP |

### 6. 확인 사항

- Dashboard에 PLC 온도/습도 표시되는지 확인
- Agent 접속 시 우측 사이드바에 녹색 점 표시되는지 확인
- Serial Settings 탭에서 카메라 설정 전송 가능한지 확인
