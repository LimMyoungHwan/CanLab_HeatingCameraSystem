namespace HeatingCameraSystem.Core.Models;

/// <summary>Creates a live link, never guesses slots or rewrites the source recipe.</summary>
public static class CapturePlanConverter
{
    public static Recipe CreateFromTemplate(Recipe source)
    {
        if (source.Plan != null) throw new ArgumentException("촬영 계획을 템플릿으로 사용할 수 없습니다.");
        return new Recipe
        {
            Name = source.Name + " (촬영 계획)",
            Plan = new CapturePlan { Entries = new() { new() { TemplateRecipeId = source.Id } } },
            PlanValidationError = "실행 불가: 템플릿의 슬롯을 지정하고 연결된 카메라를 선택하세요."
        };
    }
}
