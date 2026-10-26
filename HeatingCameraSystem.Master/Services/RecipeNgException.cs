using System;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>필수 보정 또는 보정 파일 확인 실패. 후속 작업을 실행하지 않고 레시피를 NG로 끝낸다.</summary>
    public sealed class RecipeNgException : Exception
    {
        public string AlarmCode { get; }

        public RecipeNgException(string message, string alarmCode = AlarmCodes.BiasFailed, Exception? innerException = null)
            : base(message, innerException)
        {
            AlarmCode = alarmCode;
        }
    }
}
