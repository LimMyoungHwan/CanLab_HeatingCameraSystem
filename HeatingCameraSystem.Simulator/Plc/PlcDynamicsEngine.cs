using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Memory;

namespace HeatingCameraSystem.Simulator.Plc;

public sealed class PlcDynamicsEngine : IDisposable
{
    private readonly FEnetDeviceMemory _memory;
    private readonly PlcSettings _plc;
    private readonly DynamicsSettings _dynamics;
    private readonly Timer _timer;
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
    }

    public void Start() => _timer.Change(_dynamics.TickMs, _dynamics.TickMs);

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _timer.Dispose();
        // ponytail: not disposing _cts — an in-flight Tick may still reference its Token; Cancel is enough
    }

    private void Tick()
    {
        if (_disposed) return;
        lock (_gate)
        {
            StepScaled(_plc.TempPv, _plc.TempSv, _dynamics.TemperatureRatePerSecond);
            StepScaled(_plc.HumPv, _plc.HumSv, _dynamics.HumidityRatePerSecond);
            StepScaled(_plc.Bb1Pv, _plc.Bb1Sv, _dynamics.BlackbodyRatePerSecond);
            StepScaled(_plc.Bb2Pv, _plc.Bb2Sv, _dynamics.BlackbodyRatePerSecond);
            MirrorEquipmentStatus();
            DetectPointMoves();
        }
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

    private void StepScaled(string pvToken, string svToken, double ratePerSecond)
    {
        short pv = _memory.ReadWordToken(pvToken);
        short sv = _memory.ReadWordToken(svToken);
        int maxStep = Math.Max(1, (int)Math.Round(ratePerSecond * 10 * _dynamics.TickMs / 1000.0));
        int delta = sv - pv;
        if (delta == 0) return;
        int step = Math.Clamp(delta, -maxStep, maxStep);
        _memory.WriteWordToken(pvToken, (short)(pv + step));
    }

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
            return; // engine disposed mid-move: stop before mutating shared memory
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
