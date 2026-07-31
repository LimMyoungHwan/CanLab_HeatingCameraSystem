using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeMigrationTests
    {
        private static string NewTempDir()
            => Path.Combine(Path.GetTempPath(), "HcsRecipeMigTests_" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task FirstMigration_SeedsFilesFromLegacyLiteDb()
        {
            string baseDir = NewTempDir();
            using var db = new LiteDatabase(new MemoryStream());
            try
            {
                var legacy = new LiteDbRecipeRepository(db);
                await legacy.SaveAsync(new Recipe { Name = "레시피A" });
                await legacy.SaveAsync(new Recipe { Name = "레시피B" });

                var files = new FileRecipeRepository(baseDir);
                MigrationService.MigrateRecipesToFiles(db, files);

                var names = (await files.GetAllAsync()).Select(r => r.Name).OrderBy(n => n).ToList();
                Assert.Equal(new[] { "레시피A", "레시피B" }, names);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task SecondInvocation_AfterDeletingAllJsonFiles_DoesNotResurrect()
        {
            string baseDir = NewTempDir();
            using var db = new LiteDatabase(new MemoryStream());
            try
            {
                var legacy = new LiteDbRecipeRepository(db);
                await legacy.SaveAsync(new Recipe { Name = "삭제될레시피" });

                var files = new FileRecipeRepository(baseDir);
                MigrationService.MigrateRecipesToFiles(db, files);   // first run: seeds one file

                // Operator deletes every JSON recipe on purpose.
                foreach (var f in Directory.GetFiles(Path.Combine(baseDir, "recipe"), "*.json"))
                    File.Delete(f);

                MigrationService.MigrateRecipesToFiles(db, files);   // second run: marker set -> no reseed

                Assert.Empty(await files.GetAllAsync());
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task ExistingFiles_WithoutMarker_ArePreserved()
        {
            string baseDir = NewTempDir();
            using var db = new LiteDatabase(new MemoryStream());
            try
            {
                var files = new FileRecipeRepository(baseDir);
                var edited = new Recipe { Name = "운영자편집레시피" };
                await files.SaveAsync(edited);

                var legacy = new LiteDbRecipeRepository(db);
                await legacy.SaveAsync(new Recipe { Id = edited.Id, Name = "오래된LiteDB레시피" });

                MigrationService.MigrateRecipesToFiles(db, files);

                var loaded = Assert.Single(await files.GetAllAsync());
                Assert.Equal("운영자편집레시피", loaded.Name);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task EmptyLegacyStore_SeedsNothing_AndStillMarksDone()
        {
            string baseDir = NewTempDir();
            using var db = new LiteDatabase(new MemoryStream());
            try
            {
                var files = new FileRecipeRepository(baseDir);
                MigrationService.MigrateRecipesToFiles(db, files);   // legacy store is empty
                Assert.Empty(await files.GetAllAsync());

                // Marker is set even for an empty legacy store, so a later LiteDB write is never seeded.
                await new LiteDbRecipeRepository(db).SaveAsync(new Recipe { Name = "나중레시피" });
                MigrationService.MigrateRecipesToFiles(db, files);

                Assert.Empty(await files.GetAllAsync());
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task FirstSeedFails_LeavesMarkerUnset_SoItRetries()
        {
            string baseDir = NewTempDir();
            using var db = new LiteDatabase(new MemoryStream());
            try
            {
                await new LiteDbRecipeRepository(db).SaveAsync(new Recipe { Name = "재시도레시피" });

                // First run: the file store throws mid-seed, so the marker must stay unset.
                Assert.Throws<IOException>(() =>
                    MigrationService.MigrateRecipesToFiles(db, new ThrowingRecipeRepository()));

                // Second run with a working store completes the seed (proves the marker was not set).
                var files = new FileRecipeRepository(baseDir);
                MigrationService.MigrateRecipesToFiles(db, files);

                var loaded = Assert.Single(await files.GetAllAsync());
                Assert.Equal("재시도레시피", loaded.Name);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        private sealed class ThrowingRecipeRepository : IRecipeRepository
        {
            public Task<IEnumerable<Recipe>> GetAllAsync() => Task.FromResult<IEnumerable<Recipe>>(Array.Empty<Recipe>());
            public Task<Recipe?> GetByIdAsync(string id) => throw new NotSupportedException();
            public Task SaveAsync(Recipe recipe) => throw new IOException("disk full");
            public Task DeleteAsync(string id) => throw new NotSupportedException();
        }
    }
}
