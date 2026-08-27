using System;
using System.IO;
using System.Linq;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 일회성 데이터 마이그레이션 모음. LiteDB의 <c>_migrations</c> 컬렉션에 마커 문서를 남겨
    /// 완료 여부를 추적하므로 이후 기동에서는 다시 실행되지 않는다.
    /// </summary>
    public static class MigrationService
    {
        private const string MigrationFlag = "MigrationDone_CameraSerialSettings_To_CameraDevice";
        private const string RecipeMigrationFlag = "MigrationDone_LiteDbRecipes_To_Files";

        /// <summary>
        /// 레거시 CameraSerialSettings 컬렉션을 CameraDevice로 승격하는 1회 마이그레이션.
        /// 데이터가 있었으면 원본 컬렉션을 비우고, 마지막에 완료 마커를 남긴다.
        /// </summary>
        public static void Run(LiteDatabase db, ICameraDeviceRepository deviceRepo)
        {
            var meta = db.GetCollection<BsonDocument>("_migrations");
            if (meta.FindById(MigrationFlag) != null) return;

            var oldCol = db.GetCollection<CameraSerialSettings>("CameraSerialSettings");
            var oldItems = oldCol.FindAll();
            bool hasData = false;

            foreach (var s in oldItems)
            {
                hasData = true;
                var device = new CameraDevice
                {
                    HardwareId     = $"legacy_{s.CameraIndex}",
                    AgentId        = $"Agent_{s.CameraIndex}",
                    Alias          = $"(legacy CAM-{s.CameraIndex})",
                    PCId           = Environment.MachineName,
                    OpenCvIndex    = s.CameraIndex,
                    SerialSettings = s,
                    IsApproved     = true,
                    FirstSeen      = DateTime.UtcNow,
                    LastSeen       = DateTime.UtcNow,
                };
                deviceRepo.UpsertAsync(device).GetAwaiter().GetResult();
            }

            if (hasData)
            {
                oldCol.DeleteAll();
                System.Diagnostics.Debug.WriteLine("[Migration] CameraSerialSettings → CameraDevice: done");
            }

            meta.Upsert(new BsonDocument { ["_id"] = MigrationFlag, ["DoneAt"] = DateTime.UtcNow.ToString("O") });
        }

        /// <summary>레거시 LiteDB 레시피를 파일 저장소로 1회 시드한다.</summary>
        public static void MigrateRecipesToFiles(LiteDatabase db, IRecipeRepository fileRepo)
        {
            var meta = db.GetCollection<BsonDocument>("_migrations");
            if (meta.FindById(RecipeMigrationFlag) != null) return;

            // 마커가 없는 설치본이라도 예전 방식의 빈-폴더 마이그레이션을 이미 마쳤을 수 있다.
            // 그 파일들을 보존한다 — 편집본을 덮어쓰거나 삭제된 레시피를 되살리지 않는다.
            if (fileRepo.GetAllAsync().GetAwaiter().GetResult().Any())
            {
                meta.Upsert(new BsonDocument { ["_id"] = RecipeMigrationFlag, ["DoneAt"] = DateTime.UtcNow.ToString("O") });
                return;
            }

            var legacyRepo = new LiteDbRecipeRepository(db);
            foreach (var recipe in legacyRepo.GetAllAsync().GetAwaiter().GetResult())
                fileRepo.SaveAsync(recipe).GetAwaiter().GetResult();

            // 마커는 맨 마지막에 남긴다: 시드가 중간에 실패하면 마커가 없는 채로 남아 다음 기동에서 재시도된다.
            meta.Upsert(new BsonDocument { ["_id"] = RecipeMigrationFlag, ["DoneAt"] = DateTime.UtcNow.ToString("O") });
        }

        /// <summary>기동 시마다 data.db를 타임스탬프가 붙은 .bak 파일로 복사해 둔다. 파일이 없으면 아무것도 하지 않는다.</summary>
        public static void BackupDatabase(string dbPath)
        {
            if (!File.Exists(dbPath)) return;
            string backup = $"{dbPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(dbPath, backup, overwrite: false);
            System.Diagnostics.Debug.WriteLine($"[Migration] DB backup: {backup}");
        }
    }
}
