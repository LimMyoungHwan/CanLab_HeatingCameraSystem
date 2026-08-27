using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Memory;

namespace HeatingCameraSystem.Simulator.Plc;

/// <summary>
/// 디바이스 메모리 위에서 도는 결정론적 물리 대역. 매 tick(기본 100ms)마다
/// 온도/습도/흑체 PV를 SV 쪽으로 고정 속도만큼 선형 램프하고, 장비 명령 비트를 상태 비트로
/// 미러링하며, 조그 비트가 눌린 동안 서보 위치 워드를 램프한다. 포인트 이동 트리거는
/// ServoBusyMs 대기 후 좌표 워드를 즉시 복사하는 방식이다.
/// 실 챔버와의 차이: 열 관성·오버슈트·센서 노이즈·축별 이동 시간 차가 없고 램프는 항상 직선이다.
/// </summary>
public sealed class PlcDynamicsEngine : IDisposable
{
    private readonly FEnetDeviceMemory _memory;
    private readonly PlcSettings _plc;
    private readonly DynamicsSettings _dynamics;
    private readonly Timer _timer;
    // 원터치 P 비트는 PulseHoldMs(기본 100ms)만 ON이므로 dynamics tick(=100ms)으로는 놓칠 수 있다.
    // 실 PLC 스캔처럼 트리거만 별도로 빠르게 샘플링한다.
    private const int TriggerScanMs = 20;
    private readonly Timer _triggerTimer;
    private readonly object _gate = new();
    private readonly bool[] _moveLatch;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public PlcDynamicsEngine(FEnetDeviceMemory memory, PlcSettings plc, DynamicsSettings dynamics)
    {
        _memory = memory;
        _plc = plc;
        _dynamics = dynamics;
        _moveLatch = new bool[Math.Max(1, plc.ServoPointCount)];
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _triggerTimer = new Timer(_ => ScanTriggers(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>동역학 tick과 트리거 스캔 타이머를 함께 기동한다.</summary>
    public void Start()
    {
        _timer.Change(_dynamics.TickMs, _dynamics.TickMs);
        _triggerTimer.Change(TriggerScanMs, TriggerScanMs);
    }

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _triggerTimer.Dispose();
        _timer.Dispose();
        // ponytail: _cts는 Dispose하지 않는다 — 진행 중인 Tick이 아직 Token을 참조할 수 있다. Cancel로 충분하다
    }

    private void Tick()
    {
        if (_disposed) return;
        lock (_gate)
        {
            StepScaled(_plc.TempPv, _plc.TempSv, _dynamics.TemperatureRatePerSecond);
            StepScaled(_plc.HumPv, _plc.HumSv, _dynamics.HumidityRatePerSecond);
            // 흑체 워드는 ×100 스케일(PlcXgtClient와 일치).
            StepScaled(_plc.Bb1Pv, _plc.Bb1Sv, _dynamics.BlackbodyRatePerSecond, scale: 100);
            StepScaled(_plc.Bb2Pv, _plc.Bb2Sv, _dynamics.BlackbodyRatePerSecond, scale: 100);
            JogStep(_memory, _plc, _dynamics.JogRatePerSecond, _dynamics.TickMs);
            MirrorEquipmentStatus();
        }
    }

    private void ScanTriggers()
    {
        if (_disposed) return;
        lock (_gate) DetectPointMoves();
    }

    private void MirrorEquipmentStatus()
    {
        Mirror(_plc.EqCooler1st, _plc.StatusCooler1st);
        Mirror(_plc.EqCooler2nd, _plc.StatusCooler2nd);
        Mirror(_plc.EqCoolerRoom, _plc.StatusCoolerRoom);
        Mirror(_plc.EqBlower1, _plc.StatusBlower1);
        Mirror(_plc.EqBlower2, _plc.StatusBlower2);
        Mirror(_plc.EqPairGlass, _plc.StatusPairGlass);
    }

    private void Mirror(string source, string target) => _memory.WriteBitToken(target, _memory.ReadBitToken(source));

    // PV를 SV 쪽으로 한 tick 이동. 최대 스텝 = rate(단위/s) × scale × TickMs / 1000, 최소 1 raw.
    private void StepScaled(string pvToken, string svToken, double ratePerSecond, int scale = 10)
    {
        short pv = _memory.ReadWordToken(pvToken);
        short sv = _memory.ReadWordToken(svToken);
        int maxStep = Math.Max(1, (int)Math.Round(ratePerSecond * scale * _dynamics.TickMs / 1000.0));
        int delta = sv - pv;
        if (delta == 0) return;
        int step = Math.Clamp(delta, -maxStep, maxStep);
        _memory.WriteWordToken(pvToken, (short)(pv + step));
    }

    // 조그 비트가 눌려있는(held) 동안 매 틱 서보 위치 워드를 램프한다. 위치 워드는 0.1mm(×10 스케일)이므로
    // step = rate(mm/s) × 10 × TickMs / 1000. internal: PlcDynamicsEngineTests가 한 스텝을 직접 검증.
    internal static void JogStep(FEnetDeviceMemory memory, PlcSettings plc, double jogRatePerSecond, int tickMs)
    {
        int step = Math.Max(1, (int)Math.Round(jogRatePerSecond * 10 * tickMs / 1000.0));
        JogAxis(memory, plc.ServoXPos, memory.ReadBitToken(plc.BitJogXPlus), memory.ReadBitToken(plc.BitJogXMinus), step);
        JogAxis(memory, plc.ServoYPos, memory.ReadBitToken(plc.BitJogYPlus), memory.ReadBitToken(plc.BitJogYMinus), step);
    }

    private static void JogAxis(FEnetDeviceMemory memory, string posToken, bool plus, bool minus, int step)
    {
        if (plus == minus) return; // 둘 다 ON(모순) 또는 둘 다 OFF → 이동 없음
        short pos = memory.ReadWordToken(posToken);
        int next = plus ? pos + step : pos - step;
        // ponytail: 실제 스테이지는 원점(0) 아래로 못 감 — 시뮬은 0에서 클램프
        if (next < 0) next = 0;
        memory.WriteWordToken(posToken, (short)next);
    }

    // 포인트 이동 트리거 비트의 상승 에지를 래치로 감지해 이동을 시작한다.
    private void DetectPointMoves()
    {
        for (int i = 0; i < _moveLatch.Length; i++)
        {
            int position = i + 1;
            string bit = IncDevice(_plc.ServoPointMoveBase, i);
            bool trigger = _memory.ReadBitToken(bit);
            if (trigger && !_moveLatch[i])
                _ = CompleteMoveAsync(position, bit);
            _moveLatch[i] = trigger;
        }
    }

    // busy를 올리고 ServoBusyMs 대기 후 포인트 좌표를 현재 위치로 복사, busy/트리거를 내린다.
    private async Task CompleteMoveAsync(int position, string moveBit)
    {
        _memory.WriteBitToken(_plc.ServoXBusyBit, true);
        _memory.WriteBitToken(_plc.ServoYBusyBit, true);
        try
        {
            await Task.Delay(_dynamics.ServoBusyMs, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // 이동 도중 엔진이 dispose됨: 공유 메모리를 건드리기 전에 중단한다
        }
        lock (_gate)
        {
            if (_disposed) return;
            var (xToken, yToken) = PointCoordDevices(position);
            _memory.WriteWordToken(_plc.ServoXPos, _memory.ReadWordToken(xToken));
            _memory.WriteWordToken(_plc.ServoYPos, _memory.ReadWordToken(yToken));
            _memory.WriteWordToken(_plc.ServoCurrentPoint, (short)position);
            _memory.WriteBitToken(_plc.ServoXBusyBit, false);
            _memory.WriteBitToken(_plc.ServoYBusyBit, false);
            _memory.WriteBitToken(moveBit, false);
            _moveLatch[position - 1] = false;
        }
    }

    // 포인트 n의 좌표 워드: X = ServoPointXBase + (n-1)×ServoPointStride, Y = X + 2.
    private (string X, string Y) PointCoordDevices(int positionIndex)
    {
        var (prefix, baseNum) = SplitDecimal(_plc.ServoPointXBase);
        int x = baseNum + (positionIndex - 1) * _plc.ServoPointStride;
        return ($"{prefix}{x}", $"{prefix}{x + 2}");
    }

    private static string IncDevice(string token, int offset)
    {
        var (prefix, number) = SplitDecimal(token);
        return prefix + (number + offset);
    }

    private static (string Prefix, int Number) SplitDecimal(string token)
    {
        int i = 0;
        while (i < token.Length && char.IsLetter(token[i])) i++;
        return (token[..i], int.Parse(token[i..]));
    }
}
