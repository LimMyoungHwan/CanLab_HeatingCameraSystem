using System;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 앱 전역 서비스 로케이터 (DI 컨테이너 대용).
    /// App.xaml.cs에서 Initialize() 호출 후 사용.
    /// </summary>
    public static class AppServices
    {
        public static LiteDatabase Db { get; private set; } = null!;
        public static IRecipeRepository RecipeRepo { get; private set; } = null!;
        public static ICameraMappingRepository MappingRepo { get; private set; } = null!;
        public static ICaptureHistoryRepository HistoryRepo { get; private set; } = null!;

        public static NatsCommunicationService? NatsService { get; private set; }
        public static PlcModbusClient? PlcController { get; private set; }
        public static RecipeEngine? RecipeEngine { get; private set; }

        public static void Initialize()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeatingCameraSystem");
            Directory.CreateDirectory(dir);

            Db = new LiteDatabase(Path.Combine(dir, "data.db"));
            RecipeRepo = new LiteDbRecipeRepository(Db);
            MappingRepo = new LiteDbCameraMappingRepository(Db);
            HistoryRepo = new LiteDbCaptureHistoryRepository(Db);

            NatsService = new NatsCommunicationService();
            PlcController = new PlcModbusClient();
            RecipeEngine = new RecipeEngine(PlcController, NatsService, HistoryRepo);
        }

        /// <summary>
        /// NATS / PLC 연결 시도. 실패해도 예외를 삼켜 앱이 계속 실행될 수 있게 함.
        /// </summary>
        public static async Task TryConnectServicesAsync(
            string natsUrl = "nats://127.0.0.1:4222",
            string plcIp = "192.168.1.100",
            int plcPort = 502)
        {
            try
            {
                await NatsService!.ConnectAsync(natsUrl);
                System.Diagnostics.Debug.WriteLine("[AppServices] NATS connected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppServices] NATS connect failed: {ex.Message}");
            }

            try
            {
                await PlcController!.ConnectAsync(plcIp, plcPort);
                System.Diagnostics.Debug.WriteLine("[AppServices] PLC connected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppServices] PLC connect failed: {ex.Message}");
            }
        }

        public static async Task DisposeAsync()
        {
            if (NatsService != null)
                await NatsService.DisposeAsync();
            PlcController?.Dispose();
            Db?.Dispose();
        }
    }
}
