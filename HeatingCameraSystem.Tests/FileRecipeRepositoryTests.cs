using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class FileRecipeRepositoryTests
    {
        private static string NewTempDir()
            => Path.Combine(Path.GetTempPath(), "HcsRecipeTests_" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task SaveThenGetAll_RoundTrips()
        {
            string baseDir = NewTempDir();
            try
            {
                var repo = new FileRecipeRepository(baseDir);
                var recipe = new Recipe
                {
                    Name = "라운드트립",
                    Steps =
                    {
                        new RecipeStep { CameraIndex = 3, PositionX = 12.5f },
                        new RecipeStep { CameraIndex = 7, PositionX = 99f }
                    }
                };

                await repo.SaveAsync(recipe);
                var all = (await repo.GetAllAsync()).ToList();

                var loaded = Assert.Single(all);
                Assert.Equal(recipe.Id, loaded.Id);
                Assert.Equal("라운드트립", loaded.Name);
                Assert.Equal(2, loaded.Steps.Count);
                Assert.Equal(3, loaded.Steps[0].CameraIndex);
                Assert.Equal(12.5f, loaded.Steps[0].PositionX);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task Save_ExistingRecipe_WritesBackup()
        {
            string baseDir = NewTempDir();
            try
            {
                var repo = new FileRecipeRepository(baseDir);
                var recipe = new Recipe { Name = "v1" };

                await repo.SaveAsync(recipe);   // brand-new -> no backup
                recipe.Name = "v2";
                await repo.SaveAsync(recipe);   // existing -> backs up the previous (v1) version

                string backupDir = Path.Combine(baseDir, "recipe bak");
                var backups = Directory.GetFiles(backupDir, "*.json");
                Assert.Single(backups);

                var live = await repo.GetByIdAsync(recipe.Id);
                Assert.NotNull(live);
                Assert.Equal("v2", live?.Name);
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }

        [Fact]
        public async Task Delete_RemovesLiveFile()
        {
            string baseDir = NewTempDir();
            try
            {
                var repo = new FileRecipeRepository(baseDir);
                var recipe = new Recipe { Name = "삭제대상" };

                await repo.SaveAsync(recipe);
                await repo.DeleteAsync(recipe.Id);

                Assert.Null(await repo.GetByIdAsync(recipe.Id));
                Assert.False(File.Exists(Path.Combine(baseDir, "recipe", recipe.Id + ".json")));
            }
            finally
            {
                if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}
