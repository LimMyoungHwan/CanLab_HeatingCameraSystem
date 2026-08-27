using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 레시피를 LiteDB <c>recipes</c> 컬렉션에 보관하는 저장소 구현. Name 인덱스를 만든다.
    /// (레시피를 JSON 파일로 보관하는 FileRecipeRepository 구현도 따로 존재한다.)
    /// </summary>
    public class LiteDbRecipeRepository : IRecipeRepository
    {
        private readonly ILiteCollection<Recipe> _col;

        public LiteDbRecipeRepository(LiteDatabase db)
        {
            _col = db.GetCollection<Recipe>("recipes");
            _col.EnsureIndex(x => x.Name);
        }

        public Task<IEnumerable<Recipe>> GetAllAsync()
            => Task.FromResult<IEnumerable<Recipe>>(_col.FindAll().ToList());

        public Task<Recipe?> GetByIdAsync(string id)
            => Task.FromResult<Recipe?>(_col.FindOne(x => x.Id == id));

        /// <summary>Upsert 방식이라 신규 저장과 수정을 모두 처리한다.</summary>
        public Task SaveAsync(Recipe recipe)
        {
            _col.Upsert(recipe);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            _col.DeleteMany(x => x.Id == id);
            return Task.CompletedTask;
        }
    }
}
