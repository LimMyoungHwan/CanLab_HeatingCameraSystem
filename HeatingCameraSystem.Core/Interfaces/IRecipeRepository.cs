using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 레시피(챔버/촬영 단계 모음)를 저장·조회·삭제한다.
    /// </summary>
    public interface IRecipeRepository
    {
        /// <summary>모든 레시피를 반환한다.</summary>
        Task<IEnumerable<Recipe>> GetAllAsync();

        /// <summary>ID로 레시피를 조회한다. 없으면 null.</summary>
        Task<Recipe?> GetByIdAsync(string id);

        /// <summary>레시피를 저장한다. 동일 ID가 있으면 덮어쓴다.</summary>
        Task SaveAsync(Recipe recipe);

        /// <summary>ID로 레시피를 삭제한다.</summary>
        Task DeleteAsync(string id);
    }
}
