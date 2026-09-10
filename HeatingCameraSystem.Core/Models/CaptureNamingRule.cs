using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 생산 저장 규칙(<c>[AISEN-TI][GEN3][열캘]</c>)의 폴더·파일명 계산. 고객 후처리 툴이 이 이름을
    /// 그대로 파싱하므로 코드 값과 장수는 하드웨어 계약처럼 다뤄야 한다.
    /// 레퍼런스 구현: <c>참고/AISEN_CODE/main.py:950-1066</c>.
    /// </summary>
    public static class CaptureNamingRule
    {
        private static readonly Dictionary<ChamberRange, string> RangeCodes = new()
        {
            [ChamberRange.Low] = "LN",
            [ChamberRange.Mid] = "RP",
            [ChamberRange.High] = "H1P"
        };

        /// <summary>대역별 폴더 코드 온도(℃). 고객 규칙에 정의된 9개 값이며 폴더명 후보일 뿐이다.</summary>
        private static readonly Dictionary<ChamberRange, int[]> AllowedTemperatures = new()
        {
            [ChamberRange.Low] = new[] { -30, -10, 10 },
            [ChamberRange.Mid] = new[] { 10, 25, 40 },
            [ChamberRange.High] = new[] { 40, 55, 70 }
        };

        /// <summary>
        /// 3번째 계층 폴더명을 만든다(예: 상온 +40℃ → <c>RPP40</c>).
        /// <para>
        /// 입력은 챔버 실측 온도라 39.98℃처럼 딱 떨어지지 않으므로 대역의 코드 온도 중 가장 가까운
        /// 값으로 스냅한다. 얼마나 벗어났든 예외를 던지지 않는다 — 이건 검증이 아니라 저장 규칙이고,
        /// 온도가 어긋났다고 촬영 데이터를 버리는 쪽이 더 나쁘다.
        /// </para>
        /// </summary>
        public static string ConditionFolder(ChamberRange range, double celsius)
        {
            int[] codes = AllowedTemperatures[range];

            int nearest = codes[0];
            double nearestDiff = Math.Abs(celsius - nearest);
            foreach (int candidate in codes)
            {
                double diff = Math.Abs(celsius - candidate);
                if (diff < nearestDiff)
                {
                    nearest = candidate;
                    nearestDiff = diff;
                }
            }

            string sign = nearest < 0 ? "N" : "P";
            return $"{RangeCodes[range]}{sign}{Math.Abs(nearest):D2}";
        }

        /// <summary>4번째 계층 폴더명. <see cref="BlackBodyRole.Room"/>은 흑체 없이 찍은 것이다.</summary>
        public static string BlackBodyFolder(BlackBodyRole role) => role switch
        {
            BlackBodyRole.Hot => "hot",
            BlackBodyRole.Cold => "cold",
            BlackBodyRole.Room => "room",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        /// <summary>
        /// 파일명 접두사. 흑체 목표 온도가 대역에 따라 달라서 대역과 역할을 함께 봐야 한다
        /// (저온 cold=10℃/hot=70℃, 상온·고온 cold=20℃/hot=80℃).
        /// </summary>
        public static string FilePrefix(ChamberRange range, BlackBodyRole role) => (range, role) switch
        {
            (_, BlackBodyRole.Room) => "BBroom",
            (ChamberRange.Low, BlackBodyRole.Cold) => "BB10",
            (ChamberRange.Low, BlackBodyRole.Hot) => "BB70",
            (_, BlackBodyRole.Cold) => "BB20",
            (_, BlackBodyRole.Hot) => "BB80",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        /// <summary>bias.json은 cold 조건에서만 남긴다(<c>main.py:1035</c>).</summary>
        public static bool WritesBiasJson(BlackBodyRole role) => role == BlackBodyRole.Cold;

        public static string FileName(string filePrefix, int index) => $"{filePrefix}_{index:D3}.raw";
    }
}
