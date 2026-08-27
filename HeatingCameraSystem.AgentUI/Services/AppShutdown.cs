using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// 종료 스텝을 스레드 풀에서 전체 타임아웃을 걸고 순차 실행한다. 멈췄거나 뽑힌 시리얼 포트에서
    /// hang하는 스텝(셔터 닫기, <c>SerialPort.Dispose</c>)이 <c>App.OnExit</c>의 WPF UI 스레드를
    /// 영원히 막지 못하게 하기 위해 존재한다. best-effort: 스텝별 예외는 삼킨다.
    /// </summary>
    internal static class AppShutdown
    {
        /// <summary>
        /// 각 스텝을 호출 스레드 밖에서 순서대로 실행한다. 모든 스텝이 <paramref name="totalTimeout"/>
        /// 안에 끝나면 true, 예산 초과면 false를 반환한다(hang한 스텝이 타임아웃 너머까지 막지 못하므로
        /// 프로세스가 빠져나가 포트를 해제할 수 있다).
        /// </summary>
        public static bool Run(IEnumerable<Func<Task>> steps, TimeSpan totalTimeout)
        {
            var work = Task.Run(async () =>
            {
                foreach (Func<Task> step in steps)
                {
                    try { await step().ConfigureAwait(false); }
                    catch { /* best-effort shutdown */ }
                }
            });
            return work.Wait(totalTimeout);
        }
    }
}
