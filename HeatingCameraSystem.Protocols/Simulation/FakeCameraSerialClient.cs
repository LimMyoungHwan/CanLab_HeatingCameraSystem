using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 순수 인메모리 가짜 카메라 시리얼 클라이언트 — 실제 시리얼 I/O가 없다. SimulationMode에서
    /// ClSerialCameraClient 대신 사용한다. 모든 명령은 즉시 성공하며 상태는 내부 플래그로만 유지된다.
    /// 시리얼 번호와 FPA 온도는 포트 이름에서 파생되는 결정적 값이다.
    /// </summary>
    public class FakeCameraSerialClient : ICameraSerialClient
    {
        public string PortName { get; }
        public bool IsOpen { get; private set; }
        public bool ShutterOpen { get; private set; }
        public bool CameraRunning { get; private set; }
        public byte Bias { get; private set; }
        public Dictionary<CameraBiasRegister, byte> BiasRegisters { get; } = new();

        public FakeCameraSerialClient(string portName)
        {
            PortName = portName;
        }

        /// <summary>포트를 열지 않고 즉시 열림 상태로 표시한다.</summary>
        public Task InitializeAsync(CancellationToken ct = default)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        /// <summary>포트 이름에 고정 매핑된 시리얼 번호를 반환한다(COM7→000100001, COM8→000100002, 그 외 000000000).</summary>
        public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        {
            string serial = PortName switch
            {
                "COM7" => "000100001",
                "COM8" => "000100002",
                _ => "000000000",
            };
            return Task.FromResult(serial);
        }

        /// <summary>포트 이름 문자 합에서 파생되는 결정적 FPA 온도를 반환한다. 호출마다 값이 같다.</summary>
        public Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default)
        {
            // 포트별로 결정적이며 항상 유한한 값. 약 30.0..32.9℃.
            int sum = 0;
            foreach (char ch in PortName)
            {
                sum += ch;
            }

            return Task.FromResult(30.0 + (sum % 30) / 10.0);
        }

        public async Task<short> ReadFpaTemperatureRawAsync(CancellationToken ct = default)
        {
            double celsius = await ReadFpaTemperatureAsync(ct).ConfigureAwait(false);
            return (short)Math.Round((celsius - 415.48) / -188.65 * 32768.0 / 4.096);
        }

        /// <summary>실제 전송 없이 <see cref="ShutterOpen"/> 플래그만 기록한다.</summary>
        public Task SetShutterAsync(bool open, CancellationToken ct = default)
        {
            ShutterOpen = open;
            return Task.CompletedTask;
        }

        public Task SetBiasAsync(byte value, CancellationToken ct = default)
        {
            Bias = value;
            BiasRegisters[CameraBiasRegister.GskLsb] = value;
            return Task.CompletedTask;
        }

        public Task SetBiasRegisterAsync(CameraBiasRegister register, byte value, CancellationToken ct = default)
        {
            BiasRegisters[register] = value;
            if (register == CameraBiasRegister.GskLsb) Bias = value;
            return Task.CompletedTask;
        }

        /// <summary>실제 전송 없이 <see cref="CameraRunning"/> 플래그만 기록한다.</summary>
        public Task SetCameraRunningAsync(bool running, CancellationToken ct = default)
        {
            CameraRunning = running;
            return Task.CompletedTask;
        }

        public bool ConfigSaved { get; private set; }

        /// <summary>비휘발성 저장 없이 <see cref="ConfigSaved"/> 플래그만 기록한다.</summary>
        public Task SaveConfigAsync(CancellationToken ct = default)
        {
            ConfigSaved = true;
            return Task.CompletedTask;
        }

        public void Dispose() => IsOpen = false;
    }
}
