# Design — 카메라 모델 파일 기반 해상도 설정 (camera-model-select)

**Feature**: `camera-model-select`
**Phase**: Design
**Date**: 2026-07-08
**Plan**: `docs/01-plan/features/camera-model-select.plan.md`

---

## Context Anchor

| 항목 | 내용 |
|------|------|
| **WHY** | 카메라 모델마다 센서 해상도가 다름 — 파일 기반으로 신규 모델 대응 (Master UI/코드 변경 없이) |
| **WHO** | 개발자/설치 담당자 (JSON 파일 작성), Agent 런타임 |
| **RISK** | `VideoCapture.Set` 실패, 파일 누락 시 크래시 |
| **SUCCESS** | `agent.json.CameraModel` 지정 → 해당 해상도로 캡처, 미지정/누락 시 기존 동작 무변경 |
| **SCOPE** | `Core/Config/AgentConfig.cs`, `Core/Models/CameraModelSpec.cs`(신규), `Agent/Program.cs`, `Agent/Services/CameraCaptureService.cs` |

---

## 1. 모델 정의

### 1.1 `CameraModelSpec` (신규, Core/Models)

```csharp
namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 카메라 모델별 센서 스펙. CameraModels\{ModelName}.json 파일과 1:1 대응.
    /// 신규 모델 추가 시 이 구조의 JSON 파일만 배치하면 코드 변경 없이 적용됨.
    /// </summary>
    public class CameraModelSpec
    {
        public int Width  { get; set; }
        public int Height { get; set; }
    }
}
```

### 1.2 `AgentConfig` 변경 (Core/Config)

```csharp
// [camera-model-select] Design Ref: §1.2 — 카메라 모델명 지정 필드 추가
// CameraModels\{CameraModel}.json 을 찾는 키로 사용. 미지정(null/빈문자열)이면 모델 스펙 로드 스킵 (기존 동작 유지)
public string? CameraModel { get; set; }
```

---

## 2. 모델 스펙 로드 (Agent/Program.cs)

### 2.1 파일 경로 규칙

```
{AppDomain.CurrentDomain.BaseDirectory}\CameraModels\{CameraModel}.json
```

`config.StoragePath` 해석 방식(45번 줄, `Path.IsPathRooted` 체크)과 동일한 위치 기준(exe 디렉터리) 사용 — 기존 컨벤션 일관성.

### 2.2 로드 함수

```csharp
// [camera-model-select] Design Ref: §2.2 — CameraModel 지정 시 CameraModels\{모델}.json 로드
// 파일 없음/파싱 실패는 예외를 던지지 않고 null 반환 — 캡처 자체는 계속 진행되어야 함
private static CameraModelSpec? LoadCameraModelSpec(string? modelName)
{
    if (string.IsNullOrWhiteSpace(modelName)) return null;

    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraModels", $"{modelName}.json");
    if (!File.Exists(path))
    {
        Console.WriteLine($"[Agent] CameraModel '{modelName}' spec not found at {path} — using camera default resolution.");
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<CameraModelSpec>(File.ReadAllText(path));
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"[Agent] CameraModel '{modelName}' spec parse failed ({ex.Message}) — using camera default resolution.");
        return null;
    }
}
```

### 2.3 `Main()` 통합 지점

`Program.cs` 45번 줄(`storagePath` 계산) 직후, `cameraService` 생성 이전:

```csharp
// [camera-model-select] Design Ref: §2.3 — 모델 스펙 로드 후 CameraCaptureService에 전달
var modelSpec = LoadCameraModelSpec(config.CameraModel);

ICameraCaptureService cameraService = config.SimulationMode
    ? new FakeCameraCaptureService(storagePath, config.AgentId)
    : new CameraCaptureService(storagePath, modelSpec?.Width, modelSpec?.Height);
```

`FakeCameraCaptureService`는 이미 고정 640×480 합성 이미지 — 변경 없음 (Plan §3 범위 외).

---

## 3. `CameraCaptureService` 변경

### 3.1 생성자 시그니처

```csharp
// [camera-model-select] Design Ref: §3.1 — 선택적 해상도 파라미터 추가 (ICameraCaptureService 인터페이스는 무변경)
private readonly int? _width;
private readonly int? _height;

public CameraCaptureService(string storagePath, int? width = null, int? height = null)
{
    _storagePath = storagePath;
    _width = width;
    _height = height;
    if (!Directory.Exists(_storagePath))
    {
        Directory.CreateDirectory(_storagePath);
    }
}
```

`ICameraCaptureService.InitializeCamera(int)` 시그니처는 변경하지 않는다 — `CameraCaptureServiceTests.cs`의 Mock 기반 테스트 및 `FakeCameraCaptureService`와의 인터페이스 호환을 100% 유지하기 위함 (Plan §3 "수정 불가" 준수).

### 3.2 `InitializeCamera` 해상도 적용

```csharp
// [camera-model-select] Design Ref: §3.2 — VideoCapture 오픈 성공 후 해상도 지정 (지정된 경우만)
public bool InitializeCamera(int cameraIndex)
{
    try
    {
        _capture = new VideoCapture(cameraIndex);
        bool opened = _capture.IsOpened();

        if (opened && _width.HasValue && _height.HasValue)
        {
            try
            {
                _capture.FrameWidth  = _width.Value;
                _capture.FrameHeight = _height.Value;
            }
            catch (Exception ex)
            {
                // 해상도 적용 실패해도 카메라 자체는 정상 — 캡처는 계속 진행
                Console.WriteLine($"Warning: failed to set resolution {_width}x{_height}: {ex.Message}");
            }
        }

        return opened;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing camera: {ex.Message}");
        return false;
    }
}
```

`_width`/`_height`가 `null`이면(모델 미지정) 이 블록 자체가 스킵되어 **기존 동작과 100% 동일** (SC-04).

---

## 4. 배포 파일

### 4.1 예시 모델 스펙

`HeatingCameraSystem.Agent/CameraModels/AISEN.json`:
```json
{
  "Width": 640,
  "Height": 480
}
```

`HeatingCameraSystem.Agent/CameraModels/FPV.json`:
```json
{
  "Width": 320,
  "Height": 240
}
```

### 4.2 `.csproj` 빌드 출력 복사

```xml
<!-- [camera-model-select] Design Ref: §4.2 — CameraModels\*.json을 빌드 출력에 복사 -->
<ItemGroup>
  <None Include="CameraModels\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

---

## 5. 테스트 계획

| 레벨 | 테스트 | 방법 |
|------|--------|------|
| L1 | `CameraModelSpec` JSON 직렬화 왕복 | xUnit |
| L1 | `LoadCameraModelSpec`: 존재하는 모델 → 올바른 Width/Height 반환 | xUnit (Program.cs `internal` 노출 또는 별도 헬퍼로 추출 후 테스트 — Design 시 InternalsVisibleTo 검토) |
| L1 | `LoadCameraModelSpec`: 존재하지 않는 모델 → null, 예외 없음 | xUnit |
| L1 | `CameraCaptureService(path, 640, 480)` 생성 후 `InitializeCamera(999)`(invalid) → false, 예외 없음 | xUnit (기존 `InitializeCamera_WithInvalidIndex_ReturnsFalse` 패턴 재사용) |
| L3 | 회귀: 기존 64개 테스트 전부 통과 | `dotnet test --no-build` |

> `LoadCameraModelSpec`이 `Program` 클래스의 `private static` 메서드이므로 직접 단위 테스트하려면 `internal`로 변경 + `[InternalsVisibleTo("HeatingCameraSystem.Tests")]`가 필요하다. 최소 변경 원칙에 따라 `internal static`으로 변경하고 `AssemblyInfo`(또는 csproj `<InternalsVisibleTo>`)에 노출한다.

---

## 6. 구현 가이드 (Module Map)

| 모듈 | 파일 | 변경 종류 |
|------|------|---------|
| M1 | `Core/Models/CameraModelSpec.cs` | 신규 |
| M2 | `Core/Config/AgentConfig.cs` | `CameraModel` 필드 추가 |
| M3 | `Agent/Services/CameraCaptureService.cs` | 생성자 + `InitializeCamera` 수정 |
| M4 | `Agent/Program.cs` | `LoadCameraModelSpec` 함수 추가(internal), 생성자 호출부 수정 |
| M5 | `Agent/CameraModels/*.json` + `.csproj` | 신규 배포 파일 |
| M6 | `Tests/` | `CameraModelSpecTests.cs` 신규 |

### 주석 컨벤션 (기존 관례 유지)

```csharp
// [camera-model-select] Design Ref: §N — {변경 이유 한 줄}
```
