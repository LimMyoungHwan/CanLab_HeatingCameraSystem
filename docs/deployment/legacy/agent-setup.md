# Agent PC 설치 가이드

## 사전 요구사항

- Windows 10/11 (64-bit)
- .NET 8 Runtime ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))
- USB-C 열화상 카메라 연결
- NATS 서버 접근 가능 (같은 네트워크)

## 설치 절차

### 1. .NET 8 Runtime 설치

```powershell
winget install Microsoft.DotNet.Runtime.8
```

### 2. 빌드 및 배포

```powershell
# 개발 PC에서 빌드
dotnet publish HeatingCameraSystem.Agent -c Release -o publish\Agent

# Agent PC로 publish\Agent 폴더 복사
```

### 3. 카메라 연결

USB-C 카메라 연결 시 자동 생성:
- USB 카메라 장치 (OpenCV 캡처용)
- 가상 시리얼 포트 (셔터 제어용)

장치 관리자에서 할당된 COM 포트 번호 확인.

### 4. 최초 실행

```powershell
HeatingCameraSystem.Agent.exe
```

최초 실행 시 `agent.json` 자동 생성 (exe 폴더):

```json
{
  "AgentId": "HOSTNAME",
  "CameraIndex": 0,
  "NatsUrl": "nats://127.0.0.1:4222",
  "StoragePath": "ImageStorage",
  "HeartbeatIntervalSeconds": 5
}
```

### 5. agent.json 설정

| 필드 | 설명 | 예시 |
|------|------|------|
| `AgentId` | 고유 식별자 (Master에 표시) | `Agent_Bay1` |
| `CameraIndex` | 카메라 번호 (0부터) | `0` |
| `NatsUrl` | NATS 서버 주소 | `nats://192.168.1.10:4222` |
| `StoragePath` | 캡처 이미지 저장 경로 | `D:\CaptureImages` |

> 수정 후 재시작 필요.

### 6. 커맨드라인 오버라이드

agent.json 대신 인수로 직접 지정 가능:

```powershell
HeatingCameraSystem.Agent.exe Agent_Bay1 nats://192.168.1.10:4222
```

### 7. 시리얼 포트 설정

Master UI의 **Serial Settings** 탭에서 원격 설정 가능:
- PortName, BaudRate, Parity, StopBits 변경 → NATS로 자동 전달
- Agent가 자동 재연결 후 ACK 응답

### 8. Windows 서비스로 등록 (선택)

```powershell
sc.exe create HeatingCameraAgent binPath="C:\Agent\HeatingCameraSystem.Agent.exe" start=auto
sc.exe start HeatingCameraAgent
```

### 9. 방화벽 설정

| 포트 | 프로토콜 | 용도 |
|------|----------|------|
| 4222 | TCP (아웃바운드) | NATS 연결 |

### 10. 확인 사항

- Master Dashboard 우측 사이드바에 Agent 녹색 점 표시
- 캡처 명령 수신 시 `ImageStorage/` 에 이미지 저장
