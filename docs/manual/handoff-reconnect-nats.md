HANDOFF CONTEXT
===============

USER REQUESTS (AS-IS)
---------------------
- agentui 프로그램에 카메라를 자동으로 인식해서 등록이 되게 해달라고 했는데 그 기능이 동작 안 하는것 같아. 확인해줘.
- agentid는 pc이름_Agent_순번으로 자동등록 + 핫플러그 자동등록 구현해줘

GOAL
----
Implement hotplug capture and control NATS re-subscription in CameraNatsConnector.

WORK COMPLETED
--------------
- Created public static CameraAutoRegistrar class in Protocols to register missing cameras.
- Auto-registers detected cameras by host AgentId naming convention host_Agent_index.
- Integrated GetPairsOrEmpty and AutoRegisterDetectedCameras into App.xaml.cs startup and hotplug paths.
- Added 7 unit tests in CameraAutoRegistrarTests to verify auto-numbering and deduplication.
- Committed localized Master views files.

CURRENT STATE
-------------
- Auto-registration and local video recovery are functional.
- Build succeeded and all 278 tests are green.
- Replugged camera shows video locally but does not process Master capture commands due to missing NATS subscriptions.

PENDING TASKS
-------------
- Implement SyncSubscriptionsAsync in CameraNatsConnector to lazily subscribe new cameras.
- Track subscribed AgentIds in a HashSet inside CameraNatsConnector to avoid duplicate NATS subscriptions.
- Call SyncSubscriptionsAsync in App.xaml.cs OnCameraHotplug loop after panel rebuild.

KEY FILES
---------
- HeatingCameraSystem.Protocols/Cameras/CameraAutoRegistrar.cs - Auto-registration logic
- HeatingCameraSystem.AgentUI/App.xaml.cs - Startup and hotplug orchestration
- HeatingCameraSystem.Protocols/Cameras/CameraNatsConnector.cs - NATS bridge and subscription logic
- HeatingCameraSystem.Tests/CameraAutoRegistrarTests.cs - Unit tests for registrar logic

IMPORTANT DECISIONS
-------------------
- Shared one pairing pass at startup and hotplug to minimize expensive serial S/N queries.
- Used Environment.MachineName as host prefix for AgentId.
- Preferred USB ContainerId for identity tracking when camera serial number is missing or zero.

EXPLICIT CONSTRAINTS
--------------------
- Nullable is enabled and implicit usings are enabled. No nullable suppression allowed.
- XGB CPU basic setting is UseHexBitIndex=true.

CONTEXT FOR CONTINUATION
------------------------
- CameraNatsConnector has no API to dynamically add a camera after the initial connection task finishes.
- SyncSubscriptionsAsync must safely lock the subscribed set and register new subscriptions on the fly.
