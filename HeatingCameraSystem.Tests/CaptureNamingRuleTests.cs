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

        [Fact]
        public void ConditionFolder_WithinTolerance_SnapsToAllowed()
        {
            Assert.Equal("RPP40", CaptureNamingRule.ConditionFolder(ChamberRange.Mid, 39.4, toleranceCelsius: 1.0));
            Assert.Equal("LNN30", CaptureNamingRule.ConditionFolder(ChamberRange.Low, -29.2, toleranceCelsius: 1.0));
        }

        [Fact]
        public void ConditionFolder_OutsideTolerance_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CaptureNamingRule.ConditionFolder(ChamberRange.Mid, 37.5, toleranceCelsius: 1.0));
        }

        [Theory]
        [InlineData(ChamberRange.Low, 25)]
        [InlineData(ChamberRange.Mid, 70)]
        [InlineData(ChamberRange.High, 10)]
        [InlineData(ChamberRange.Mid, 41)]
        public void ConditionFolder_DisallowedPair_Throws(ChamberRange range, double celsius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CaptureNamingRule.ConditionFolder(range, celsius));
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
    }
}
