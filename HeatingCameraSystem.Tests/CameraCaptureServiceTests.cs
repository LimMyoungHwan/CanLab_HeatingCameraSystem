using System;
using System.IO;
using HeatingCameraSystem.Agent.Services;
using HeatingCameraSystem.Core.Interfaces;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraCaptureServiceTests
    {
        [Fact]
        public void Constructor_CreatesStorageDirectory_IfNotExist()
        {
            // Arrange
            string testDir = Path.Combine(Path.GetTempPath(), "HeatingCameraTest_" + Guid.NewGuid());
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }

            // Act
            using var service = new CameraCaptureService(testDir);

            // Assert
            Assert.True(Directory.Exists(testDir));

            // Cleanup
            Directory.Delete(testDir, true);
        }

        [Fact]
        public void InitializeCamera_WithInvalidIndex_ReturnsFalse()
        {
            // Arrange
            string testDir = Path.Combine(Path.GetTempPath(), "HeatingCameraTest_" + Guid.NewGuid());
            using var service = new CameraCaptureService(testDir);

            // Act
            // 999 is likely an invalid camera index
            bool result = service.InitializeCamera(999);

            // Assert
            Assert.False(result);

            // Cleanup
            Directory.Delete(testDir, true);
        }

        [Fact]
        public void InitializeCamera_WithResolutionParams_InvalidIndex_ReturnsFalseNoThrow()
        {
            string testDir = Path.Combine(Path.GetTempPath(), "HeatingCameraTest_" + Guid.NewGuid());
            using var service = new CameraCaptureService(testDir, 640, 480);

            bool result = service.InitializeCamera(999);

            Assert.False(result);

            Directory.Delete(testDir, true);
        }

        [Fact]
        public void ICameraCaptureService_MockTest()
        {
            // Arrange
            var mockService = new Mock<ICameraCaptureService>();
            string expectedPath = "test/path.jpg";
            
            mockService.Setup(s => s.InitializeCamera(It.IsAny<int>())).Returns(true);
            mockService.Setup(s => s.CaptureFrame(out expectedPath)).Returns(true);

            // Act
            bool initResult = mockService.Object.InitializeCamera(0);
            bool captureResult = mockService.Object.CaptureFrame(out string outPath);

            // Assert
            Assert.True(initResult);
            Assert.True(captureResult);
            Assert.Equal(expectedPath, outPath);
        }
    }
}
