# CANLAB Thermal Viewer

CANLAB Controller를 기반으로 정리한 열화상 카메라 뷰어입니다.

## 카메라 화면 구성

- `Connections`: 첫 번째 탭에서 Device Connection과 Video Connection을 설정합니다.
- 왼쪽 영상 영역: 카메라 영상을 320 x 240 고정 크기로 표시하며, 클릭하면 800 x 600 별도 화면을 열거나 닫습니다.
- 창 크기가 바뀌면 카메라 영역은 유지되고 오른쪽 설정 탭만 확장됩니다.
- 설정 영역: Camera Setup, Operation, Image Quality, Detector 탭을 제공합니다.

창은 `Mainwindow.ui`에 지정된 기본 크기로 열리며, 현재 모니터보다 큰 경우에만 축소됩니다. 작은 화면에서는 설정 영역을 스크롤할 수 있습니다.

## 빌드 환경

- Qt 5.14.2
- MinGW 7.3 32-bit
- OpenCV 4.13.0 (프로젝트에 포함된 DLL은 32-bit)

프로젝트에 포함된 OpenCV DLL이 32비트이므로 Qt와 MinGW도 반드시 32비트 키트를 사용해야 합니다.

```text
qmake canlab_controller.pro
mingw32-make -j4
```

실행 파일 이름은 `Canlab_Thermal_Viewer.exe`입니다.

## 장비 확인

장비가 없는 환경에서도 프로그램 실행과 UI 렌더링은 확인할 수 있습니다. 실제 영상 입력, 시리얼 연결 및 카메라 제어 명령은 대상 장비를 연결한 상태에서 별도로 확인해야 합니다.

Licensed under GNU GPLv3. See `LICENSE.GPL3`.
