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
    /// 파일 기반 레시피 저장소: 레시피 하나당 <c>&lt;baseDir&gt;/recipe</c> 아래 JSON 파일 하나로 두고,
    /// 수정·삭제 때마다 직전 버전을 <c>&lt;baseDir&gt;/recipe bak</c>으로 복사해 둔다.
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

        // ponytail: 파일명은 GUID Id라서 레시피 이름이 바뀌어도 경로가 그대로 유지된다.
        private string PathFor(string id) => Path.Combine(_recipeDir, id + ".json");

        /// <summary>recipe 폴더의 모든 JSON을 읽는다. 읽을 수 없는 파일은 건너뛰고 나머지를 돌려준다.</summary>
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
                    // ponytail: 손상된 파일 하나 때문에 전체 로드를 실패시키지 않고 그 파일만 건너뛴다.
                    Debug.WriteLine($"[FileRecipeRepository] skip unreadable recipe '{file}': {ex.Message}");
                }
            }
            return Task.FromResult<IEnumerable<Recipe>>(recipes);
        }

        /// <summary>id의 레시피를 읽는다. 파일이 없거나 파싱에 실패하면 null이다.</summary>
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

        /// <summary>레시피를 저장한다. 기존 파일이 있으면 백업을 남긴 뒤 덮어쓴다.</summary>
        public Task SaveAsync(Recipe recipe)
        {
            var path = PathFor(recipe.Id);
            if (File.Exists(path)) BackupExisting(path);   // 디스크의 직전 버전을 보존한다
            File.WriteAllText(path, JsonSerializer.Serialize(recipe, _jsonOpts));
            return Task.CompletedTask;
        }

        /// <summary>레시피를 삭제한다. 지우기 전에 백업을 남긴다.</summary>
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

        /// <summary>
        /// 라이브 파일을 백업 폴더에 "&lt;레시피이름&gt;_&lt;타임스탬프&gt;.json"으로 복사한다.
        /// 이름을 읽지 못하면 파일명(GUID)을 대신 쓴다.
        /// </summary>
        private void BackupExisting(string livePath)
        {
            string name = TryReadName(livePath) ?? Path.GetFileNameWithoutExtension(livePath);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            string dest = Path.Combine(_backupDir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            // ponytail: 초 단위 스탬프 — 같은 초 안에 다시 수정하면 마지막 백업만 남는다.
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
