using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// File-based recipe store: one JSON file per recipe under <c>&lt;baseDir&gt;/recipe</c>,
    /// with the previous version copied into <c>&lt;baseDir&gt;/recipe bak</c> on every modify/delete.
    /// </summary>
    public class FileRecipeRepository : IRecipeRepository
    {
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private readonly string _recipeDir;
        private readonly string _backupDir;

        public FileRecipeRepository(string baseDir)
        {
            _recipeDir = Path.Combine(baseDir, "recipe");
            _backupDir = Path.Combine(baseDir, "recipe bak");
            Directory.CreateDirectory(_recipeDir);
            Directory.CreateDirectory(_backupDir);
        }

        // ponytail: filename is the GUID Id, so it stays stable across recipe renames.
        private string PathFor(string id) => Path.Combine(_recipeDir, id + ".json");

        public Task<IEnumerable<Recipe>> GetAllAsync()
        {
            var recipes = new List<Recipe>();
            foreach (var file in Directory.EnumerateFiles(_recipeDir, "*.json"))
            {
                try
                {
                    var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllText(file));
                    if (recipe != null) recipes.Add(recipe);
                }
                catch (Exception ex)
                {
                    // ponytail: skip one corrupt file instead of failing the whole load.
                    Debug.WriteLine($"[FileRecipeRepository] skip unreadable recipe '{file}': {ex.Message}");
                }
            }
            return Task.FromResult<IEnumerable<Recipe>>(recipes);
        }

        public Task<Recipe?> GetByIdAsync(string id)
        {
            var path = PathFor(id);
            if (!File.Exists(path)) return Task.FromResult<Recipe?>(null);
            try
            {
                return Task.FromResult(JsonSerializer.Deserialize<Recipe>(File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileRecipeRepository] failed to read recipe '{id}': {ex.Message}");
                return Task.FromResult<Recipe?>(null);
            }
        }

        public Task SaveAsync(Recipe recipe)
        {
            var path = PathFor(recipe.Id);
            if (File.Exists(path)) BackupExisting(path);   // preserve the previous on-disk version
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, _jsonOpts));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            var path = PathFor(id);
            if (File.Exists(path))
            {
                BackupExisting(path);
                File.Delete(path);
            }
            return Task.CompletedTask;
        }

        private void BackupExisting(string livePath)
        {
            string name = TryReadName(livePath) ?? Path.GetFileNameWithoutExtension(livePath);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            string dest = Path.Combine(_backupDir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            // ponytail: second-precision stamp; a same-second re-modify keeps only the latest backup.
            File.Copy(livePath, dest, overwrite: true);
        }

        private static string? TryReadName(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<Recipe>(File.ReadAllText(path))?.Name;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileRecipeRepository] backup name read failed '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
