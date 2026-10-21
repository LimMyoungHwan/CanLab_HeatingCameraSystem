namespace HeatingCameraSystem.Core.Models;

public static class CapturePlanTemplateUsage
{
    public static string[] Find(IEnumerable<Recipe> recipes, string templateId) => recipes
        .Where(recipe => recipe.Plan?.Entries?.Any(entry => entry.TemplateRecipeId == templateId) == true)
        .Select(recipe => recipe.Name).ToArray();

    public static void RefuseUsedTemplateDeletion(IEnumerable<Recipe> recipes, string templateId)
    {
        var users = Find(recipes, templateId);
        if (users.Length > 0) throw new InvalidOperationException("이 템플릿을 사용하는 계획이 있어 삭제할 수 없습니다: " + string.Join(", ", users));
    }

    public static Recipe NormalizeLoaded(Recipe recipe)
    {
        if (recipe.Plan is { Entries: null })
        {
            recipe.PlanValidationError = CapturePlanCompiler.OldFormatMessage;
            recipe.Steps.Clear();
        }
        return recipe;
    }
}
