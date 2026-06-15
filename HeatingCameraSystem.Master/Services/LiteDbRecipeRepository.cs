using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
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
