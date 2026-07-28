using System;

namespace HeatingCameraSystem.Protocols
{
    public interface ISrLink : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void Close();
        void Write(byte[] data);
        byte[] Read();
        void DiscardInBuffer();
    }
}
