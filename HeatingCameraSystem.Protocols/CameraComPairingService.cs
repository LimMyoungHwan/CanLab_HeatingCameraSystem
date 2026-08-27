using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// 열화상 카메라(UVC)와 같은 물리 장치에 속한 USB-serial COM 포트를 자동으로 짝짓는다.
    /// USB 부모 키(<see cref="UsbTopology.DeriveContainerId"/>)가 일치하는 포트를 찾되,
    /// 수동 오버라이드(<c>HardwareSettings.CameraPairings</c>)가 있으면 그것을 우선한다.
    /// </summary>
    public sealed class CameraComPairingService : ICameraComPairingService
    {
        private readonly ICameraEnumerator _cameraEnumerator;
        private readonly IUsbSerialEnumerator _serialEnumerator;
        private readonly Func<string, ICameraSerialClient> _serialClientFactory;
        private readonly HardwareSettings _settings;

        public CameraComPairingService(
            ICameraEnumerator cameraEnumerator,
            IUsbSerialEnumerator serialEnumerator,
            Func<string, ICameraSerialClient> serialClientFactory,
            HardwareSettings settings)
        {
            _cameraEnumerator = cameraEnumerator;
            _serialEnumerator = serialEnumerator;
            _serialClientFactory = serialClientFactory;
            _settings = settings;
        }

        /// <summary>
        /// "CLTC_T_VGA"로 시작하는 열화상 카메라마다 짝 COM 포트를 찾아 반환한다.
        /// 후보 포트가 없으면 Unpaired, 둘 이상이면 Ambiguous로 표시하고 포트를 정하지 않는다.
        /// </summary>
        public async Task<IReadOnlyList<CameraComPair>> GetPairsAsync(CancellationToken ct = default)
        {
            var thermalCameras = _cameraEnumerator.Enumerate()
                .Where(c => c.FriendlyName.StartsWith("CLTC_T_VGA", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var ports = _serialEnumerator.Enumerate();
            var pairs = new List<CameraComPair>();

            foreach (var camera in thermalCameras)
            {
                var manualOverride = _settings.CameraPairings.FirstOrDefault(p => p.CameraUsbParentId == camera.UsbParentId);
                if (manualOverride is not null)
                {
                    var port = ports.FirstOrDefault(p => p.PortName == manualOverride.PortName);
                    var (serialNumber, status) = await ReadSerialNumberAsync(manualOverride.PortName, ct);
                    pairs.Add(new CameraComPair(camera, port, serialNumber, status, true));
                    continue;
                }

                var matches = ports
                    .Where(p => !string.IsNullOrEmpty(p.UsbParentId) && p.UsbParentId == camera.UsbParentId)
                    .ToList();

                if (matches.Count == 0)
                {
                    pairs.Add(new CameraComPair(camera, null, null, PairingStatus.Unpaired, false));
                    continue;
                }

                if (matches.Count > 1)
                {
                    pairs.Add(new CameraComPair(camera, null, null, PairingStatus.Ambiguous, false));
                    continue;
                }

                var matchedPort = matches[0];
                var (matchedSerialNumber, matchedStatus) = await ReadSerialNumberAsync(matchedPort.PortName, ct);
                pairs.Add(new CameraComPair(camera, matchedPort, matchedSerialNumber, matchedStatus, false));
            }

            return pairs;
        }

        /// <summary>카메라-포트 수동 매핑을 추가하거나 갱신한다. 설정 파일 저장은 호출자 책임이다.</summary>
        public void SetManualOverride(string cameraUsbParentId, string portName)
        {
            var entry = _settings.CameraPairings.FirstOrDefault(p => p.CameraUsbParentId == cameraUsbParentId);
            if (entry is null)
            {
                _settings.CameraPairings.Add(new CameraPairingEntry
                {
                    CameraUsbParentId = cameraUsbParentId,
                    PortName = portName,
                });
                return;
            }

            entry.PortName = portName;
        }

        /// <summary>
        /// 포트를 열어 카메라 S/N을 읽는다. 실패하면 예외를 삼키고 (null, DetectedButUnverified)로
        /// 강등한다 — 취소(OperationCanceledException)만 그대로 전파한다.
        /// </summary>
        private async Task<(string? SerialNumber, PairingStatus Status)> ReadSerialNumberAsync(string portName, CancellationToken ct)
        {
            using var client = _serialClientFactory(portName);
            try
            {
                await client.InitializeAsync(ct);
                string serialNumber = await client.ReadSerialNumberAsync(ct);

                // CL 검출기는 STOPPED 상태로 부팅되어 START 전까지 전부 0인 S/N(그리고 검은 영상)을 보고한다.
                // 페어링은 카메라 런타임 시작 전에 실행되므로 여기서 START를 보내고 다시 읽어
                // 포트와 무관한 S/N 키를 복구한다. 실제 r200/r150 장비에서 검증:
                // START 후 0 -> 545308059 / 545308020 (단순 재읽기만으로는 절대 복구되지 않는다).
                if (IsZeroSerial(serialNumber))
                {
                    await client.SetCameraRunningAsync(true, ct);

                    // ponytail: START 후 고정 10회 x 250ms 폴링(~2.5초) — 측정한 장비에서는 충분하다.
                    // 더 느린 검출기가 더 긴 워밍업을 요구하면 HardwareSettings로 끌어올린다.
                    for (int i = 0; i < 10 && IsZeroSerial(serialNumber); i++)
                    {
                        await Task.Delay(250, ct).ConfigureAwait(false);
                        serialNumber = await client.ReadSerialNumberAsync(ct);
                    }
                }

                return (serialNumber, PairingStatus.Paired);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (null, PairingStatus.DetectedButUnverified);
            }
        }

        private static bool IsZeroSerial(string? serial) =>
            string.IsNullOrEmpty(serial) || serial.All(c => c == '0');
    }
}
