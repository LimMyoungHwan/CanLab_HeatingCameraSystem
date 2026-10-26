using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// COM 포트 하나당 클라이언트 하나. SerialPort는 동시 접근이 안 되므로 SemaphoreSlim 하나로
    /// 모든 명령(요청 쓰기 + 7바이트 읽기)을 직렬화한다.
    /// </summary>
    public class ClSerialCameraClient : ICameraSerialClient
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _disposeGate = new();
        private readonly IClSerialTransport _transport;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly int _timeoutMilliseconds;
        private int _disposed;
        private bool _isOpen;
        private bool _needsRecovery;

        public string PortName { get; }
        public bool IsOpen => Volatile.Read(ref _isOpen) && Volatile.Read(ref _disposed) == 0;

        public ClSerialCameraClient(string portName) : this(portName, new ClSerialTransport(portName))
        {
        }

        internal ClSerialCameraClient(string portName, IClSerialTransport transport, int timeoutMilliseconds = 200)
        {
            PortName = portName;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _timeoutMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            _lifetimeToken = _lifetime.Token;
        }

        /// <summary>포트를 연다. 다른 명령보다 먼저 호출해야 하며, 그 전에는 모든 명령이 예외를 던진다.</summary>
        public async Task InitializeAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (!_transport.IsOpen) _transport.Open();
                linked.Token.ThrowIfCancellationRequested();
                _isOpen = true;
            }
            finally { _gate.Release(); }
        }

        /// <summary>S/N 레지스터 4바이트를 차례로 읽어 9자리 시리얼 번호로 디코딩한다.</summary>
        public async Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        {
            byte a = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbA, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte b = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbB, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte c = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbC, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte d = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbD, ClRw.Read, 0, ct).ConfigureAwait(false);
            return ClPacket.DecodeSerialNumber(a, b, c, d);
        }

        public async Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default)
        {
            (byte msb, byte lsb) = await ReadFpaRegistersAsync(ct).ConfigureAwait(false);
            return ClPacket.DecodeFpaTemperature(msb, lsb);
        }

        public async Task<short> ReadFpaTemperatureRawAsync(CancellationToken ct = default)
        {
            (byte msb, byte lsb) = await ReadFpaRegistersAsync(ct).ConfigureAwait(false);
            return ClPacket.DecodeFpaTemperatureRaw(msb, lsb);
        }

        private async Task<(byte Msb, byte Lsb)> ReadFpaRegistersAsync(CancellationToken ct)
        {
            byte msb = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.FpaTempMsb, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte lsb = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.FpaTempLsb, ClRw.Read, 0, ct).ConfigureAwait(false);
            return (msb, lsb);
        }

        public Task SetShutterAsync(bool open, CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.Shutter, ClRw.Write, open ? (byte)1 : (byte)0, ct);

        public Task SetBiasAsync(byte value, CancellationToken ct = default)
            => SetBiasRegisterAsync(CameraBiasRegister.GskLsb, value, ct);

        public Task<byte> ReadBiasAsync(CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.GskLsb, ClRw.Read, 0, ct);

        public Task SetBiasRegisterAsync(CameraBiasRegister register, byte value, CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.Detector, (byte)ToDetectorSubId(register), ClRw.Write, value, ct);

        private static ClDetectorSubId ToDetectorSubId(CameraBiasRegister register) => register switch
        {
            CameraBiasRegister.Cint => ClDetectorSubId.Cint,
            CameraBiasRegister.TintMsb => ClDetectorSubId.TintMsb,
            CameraBiasRegister.TintLsb => ClDetectorSubId.TintLsb,
            CameraBiasRegister.GskMsb => ClDetectorSubId.GskMsb,
            CameraBiasRegister.GskLsb => ClDetectorSubId.GskLsb,
            CameraBiasRegister.Gfid => ClDetectorSubId.Gfid,
            _ => throw new ArgumentOutOfRangeException(nameof(register), register, null)
        };

        public Task SetCameraRunningAsync(bool running, CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.Camera, ClRw.Write, running ? (byte)1 : (byte)0, ct);

        public Task SaveConfigAsync(CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.SaveConfig, ClRw.Write, 1, ct);

        public async Task<CameraOutputFormat> ReadOutputFormatAsync(CancellationToken ct = default)
            => (CameraOutputFormat)ClPacket.ExtractOutputFormat(await ReadCameraConfigAsync(ct).ConfigureAwait(false));

        public async Task SetOutputFormatAsync(CameraOutputFormat format, CancellationToken ct = default)
        {
            // 레지스터 하나에 autoStart·dispMode·outFormat·captureType·opMode가 같이 들어 있다.
            // 읽어서 2비트만 갈아끼우지 않으면 나머지 네 필드가 0으로 밀린다.
            byte current = await ReadCameraConfigAsync(ct).ConfigureAwait(false);
            byte updated = ClPacket.ReplaceOutputFormat(current, (byte)format);
            if (updated == current) return;

            await QueryAsync((byte)ClMainId.UserConfig, (byte)ClUserConfigSubId.Camera, ClRw.Write, updated, ct)
                .ConfigureAwait(false);
        }

        private Task<byte> ReadCameraConfigAsync(CancellationToken ct)
            => QueryAsync((byte)ClMainId.UserConfig, (byte)ClUserConfigSubId.Camera, ClRw.Read, 0, ct);

        private async Task<byte> QueryAsync(byte mainId, byte subId, ClRw rw, byte data, CancellationToken ct)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeToken);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                EnsureOpen();
                // SerialPort is synchronous. Do not block the Dispatcher while the device responds.
                return await Task.Run(() => QueryCore(mainId, subId, rw, data, linked.Token)).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private byte QueryCore(byte mainId, byte subId, ClRw rw, byte data, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_needsRecovery) DrainUntilQuiet(ct);
                _transport.DiscardInBuffer();
                ct.ThrowIfCancellationRequested();
                byte[] request = ClPacket.BuildRequest(mainId, subId, rw, data);
                long started = Stopwatch.GetTimestamp();
                _transport.Write(request, 0, request.Length, _timeoutMilliseconds);

                var pending = new List<byte>(14);
                byte[] chunk = new byte[7];
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int remaining = _timeoutMilliseconds - (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (remaining <= 0) throw new TimeoutException("CL response timeout.");
                    int count = _transport.Read(chunk, 0, chunk.Length, remaining);
                    ct.ThrowIfCancellationRequested();
                    if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= _timeoutMilliseconds)
                        throw new TimeoutException("CL response timeout.");
                    if (count <= 0) throw new IOException("CL serial connection closed while reading a response.");
                    pending.AddRange(chunk.AsSpan(0, count).ToArray());

                    // Recover a frame boundary after noise/partial packets. Foreign replies must
                    // never acknowledge this command, even if their header and payload look valid.
                    while (pending.Count >= 2)
                    {
                        if (pending[0] != 0x43 || pending[1] != 0x4C)
                        {
                            pending.RemoveAt(0);
                            continue;
                        }
                        if (pending.Count < 7) break;
                        byte[] response = pending.GetRange(0, 7).ToArray();
                        pending.RemoveRange(0, 7);
                        if (response[2] != mainId || response[3] != subId || response[4] != (byte)rw)
                            continue;
                        byte payload = ClPacket.ExtractPayload(response);
                        ct.ThrowIfCancellationRequested();
                        return payload;
                    }
                }
            }
            catch
            {
                // A timed-out/cancelled packet may arrive after this operation releases the gate.
                // The next transaction must observe a quiet interval before sending another request.
                _needsRecovery = true;
                ct.ThrowIfCancellationRequested();
                throw;
            }
        }

        private void DrainUntilQuiet(CancellationToken ct)
        {
            _transport.DiscardInBuffer();
            long started = Stopwatch.GetTimestamp();
            byte[] discard = new byte[64];
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 4L * _timeoutMilliseconds)
                    throw new TimeoutException("CL serial input did not become quiet after a failed command.");
                try
                {
                    int count = _transport.Read(discard, 0, discard.Length, _timeoutMilliseconds);
                    if (count <= 0) throw new IOException("CL serial connection closed during recovery.");
                }
                catch (TimeoutException)
                {
                    ct.ThrowIfCancellationRequested();
                    _needsRecovery = false;
                    return;
                }
            }
        }

        private void EnsureOpen()
        {
            ThrowIfDisposed();
            if (!_isOpen || !_transport.IsOpen)
            {
                throw new InvalidOperationException("Serial port not open. Call InitializeAsync first.");
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        public void Dispose()
        {
            lock (_disposeGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _lifetime.Cancel();
                // Cancel queued reads, then let the bounded active read finish before closing COM.
                // Do not dispose SemaphoreSlim with WaitAsync continuations still unwinding.
                _gate.Wait();
                try
                {
                    _isOpen = false;
                    _transport.Dispose();
                }
                finally
                {
                    _gate.Release();
                    _lifetime.Dispose();
                }
            }
        }
    }
}
