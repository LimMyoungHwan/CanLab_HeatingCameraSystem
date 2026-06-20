using System;
using System.IO;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Master.Services
{
    public static class MigrationService
    {
        private const string MigrationFlag = "MigrationDone_CameraSerialSettings_To_CameraDevice";

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

        public static void BackupDatabase(string dbPath)
        {
            if (!File.Exists(dbPath)) return;
            string backup = $"{dbPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(dbPath, backup, overwrite: false);
            System.Diagnostics.Debug.WriteLine($"[Migration] DB backup: {backup}");
        }
    }
}
