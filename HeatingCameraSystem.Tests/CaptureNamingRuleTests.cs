using System.Linq;
using System.Text.Json;
using System;
using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CaptureNamingRuleTests
    {
        [Theory]
        [InlineData(ChamberRange.Low, -30, "LNN30")]
        [InlineData(ChamberRange.Low, -10, "LNN10")]
        [InlineData(ChamberRange.Low, 10, "LNP10")]
        [InlineData(ChamberRange.Mid, 10, "RPP10")]
        [InlineData(ChamberRange.Mid, 25, "RPP25")]
        [InlineData(ChamberRange.Mid, 40, "RPP40")]
        [InlineData(ChamberRange.High, 40, "H1PP40")]
        [InlineData(ChamberRange.High, 55, "H1PP55")]
        [InlineData(ChamberRange.High, 70, "H1PP70")]
        public void ConditionFolder_AllowedPairs_MatchesCustomerRule(ChamberRange range, double celsius, string expected)
        {
            Assert.Equal(expected, CaptureNamingRule.ConditionFolder(range, celsius));
        }

        [Fact]
        public void ConditionFolder_RoundsNearInteger()
        {
            Assert.Equal("RPP40", CaptureNamingRule.ConditionFolder(ChamberRange.Mid, 39.98));
        }

        [Theory]
        [InlineData(ChamberRange.Mid, 26.6, "RPP25")]
        [InlineData(ChamberRange.Mid, 37.5, "RPP40")]
        [InlineData(ChamberRange.Low, 25, "LNP10")]
        [InlineData(ChamberRange.Mid, 70, "RPP40")]
        [InlineData(ChamberRange.High, 10, "H1PP40")]
        public void ConditionFolder_FarFromCode_SnapsInsteadOfThrowing(ChamberRange range, double celsius, string expected)
        {
            Assert.Equal(expected, CaptureNamingRule.ConditionFolder(range, celsius));
        }

        [Theory]
        [InlineData(BlackBodyRole.Hot, "hot")]
        [InlineData(BlackBodyRole.Cold, "cold")]
        [InlineData(BlackBodyRole.Room, "room")]
        public void BlackBodyFolder_MapsRole(BlackBodyRole role, string expected)
        {
            Assert.Equal(expected, CaptureNamingRule.BlackBodyFolder(role));
        }

        [Theory]
        [InlineData(ChamberRange.Low, BlackBodyRole.Cold, "BB10")]
        [InlineData(ChamberRange.Low, BlackBodyRole.Hot, "BB70")]
        [InlineData(ChamberRange.Mid, BlackBodyRole.Cold, "BB20")]
        [InlineData(ChamberRange.Mid, BlackBodyRole.Hot, "BB80")]
        [InlineData(ChamberRange.High, BlackBodyRole.Cold, "BB20")]
        [InlineData(ChamberRange.High, BlackBodyRole.Hot, "BB80")]
        [InlineData(ChamberRange.Low, BlackBodyRole.Room, "BBroom")]
        [InlineData(ChamberRange.High, BlackBodyRole.Room, "BBroom")]
        public void FilePrefix_DependsOnRangeAndRole(ChamberRange range, BlackBodyRole role, string expected)
        {
            Assert.Equal(expected, CaptureNamingRule.FilePrefix(range, role));
        }

        [Fact]
        public void WritesBiasJson_OnlyForCold()
        {
            Assert.True(CaptureNamingRule.WritesBiasJson(BlackBodyRole.Cold));
            Assert.False(CaptureNamingRule.WritesBiasJson(BlackBodyRole.Hot));
            Assert.False(CaptureNamingRule.WritesBiasJson(BlackBodyRole.Room));
        }

        [Theory]
        [InlineData("BB80", 0, "BB80_000.raw")]
        [InlineData("BBroom", 9, "BBroom_009.raw")]
        [InlineData("BB20", 99, "BB20_099.raw")]
        public void FileName_PadsIndexToThreeDigits(string prefix, int index, string expected)
        {
            Assert.Equal(expected, CaptureNamingRule.FileName(prefix, index));
        }

        [Fact]
        public void FileName_OffsetHundred_HundredShots_Is100To199()
        {
            string[] names = Enumerable.Range(0, 100)
                .Select(i => CaptureNamingRule.FileName("BB80", 100 + i))
                .ToArray();

            Assert.Equal("BB80_100.raw", names[0]);
            Assert.Equal("BB80_199.raw", names[99]);
            Assert.Equal(100, names.Distinct().Count());
        }

        [Fact]
        public void FileName_AtOrBeyond1000_GrowsToFourDigits()
        {
            // D3는 최소 폭이다. 1000 이상은 잘리지 않고 4자리가 된다(문자열 정렬 순서는 어긋남).
            Assert.Equal("BB80_999.raw", CaptureNamingRule.FileName("BB80", 999));
            Assert.Equal("BB80_1000.raw", CaptureNamingRule.FileName("BB80", 1000));
        }

        [Fact]
        public void CaptureCommandMessage_ShotNumberOffset_RoundTripsAndDefaultsToZero()
        {
            var cmd = new CaptureCommandMessage { ShotCount = 100, ShotNumberOffset = 100 };

            var back = JsonSerializer.Deserialize<CaptureCommandMessage>(JsonSerializer.Serialize(cmd));
            Assert.NotNull(back);
            Assert.Equal(100, back.ShotNumberOffset);

            var old = JsonSerializer.Deserialize<CaptureCommandMessage>("{\"ShotCount\":5}");
            Assert.NotNull(old);
            Assert.Equal(0, old.ShotNumberOffset);
        }
    }
}
