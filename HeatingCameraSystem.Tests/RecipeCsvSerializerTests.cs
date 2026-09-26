using System.Reflection;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Tests;

public class RecipeCsvSerializerTests
{
    [Fact]
    public void FullRoundTrip_ReflectionAsserted()
    {
        var recipe = BuildFullRecipe("고온, 저온 혼합");

        Recipe roundTrip = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(recipe));

        AssertRecipeEqual(recipe, roundTrip);
    }

    [Fact]
    public void HeaderRoundTrip_PreservesAllRecipeProperties()
    {
        var recipe = BuildHeaderRecipe("고온, 저온 혼합");

        Recipe roundTrip = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(recipe));

        AssertRecipeHeaderEqual(recipe, roundTrip);
    }

    [Fact]
    public void Deserialize_AutoDetectsSemicolonDelimiter()
    {
        var recipe = BuildHeaderRecipe("Semicolon Recipe");
        recipe.Steps.Add(BuildStep(RecipeStepKind.CameraCommand, 1));
        string semicolonCsv = RecipeCsvSerializer.Serialize(recipe).Replace(',', ';');

        Recipe roundTrip = RecipeCsvSerializer.Deserialize(semicolonCsv);

        AssertRecipeEqual(recipe, roundTrip);
    }

    [Fact]
    public void WriteFile_EmitsUtf8Bom_AndReadFilePreservesKorean()
    {
        string path = Path.Combine(Path.GetTempPath(), $"recipe-{Guid.NewGuid():N}.csv");
        var recipe = BuildHeaderRecipe("고온 캘리브레이션");

        try
        {
            RecipeCsvSerializer.WriteFile(path, recipe);
            byte[] bytes = File.ReadAllBytes(path);

            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
            Assert.Equal(recipe.Name, RecipeCsvSerializer.ReadFile(path).Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Deserialize_IgnoresUnknownColumns_AndMissingWaitColumnKeepsDefaultTrue()
    {
        const string csv = """
        [RECIPE]
        Key,Value
        Name,호환성 테스트

        [STEPS]
        StepId,Kind,UnknownColumn
        step-1,CameraCommand,ignored
        """;

        Recipe recipe = RecipeCsvSerializer.Deserialize(csv);

        RecipeStep step = Assert.Single(recipe.Steps);
        Assert.NotEqual("step-1", step.StepId);
        Assert.Equal(RecipeStepKind.CameraCommand, step.Kind);
        Assert.True(step.WaitForCaptureResult);
    }

    [Fact]
    public void Deserialize_MalformedNumericCell_ThrowsInvalidDataExceptionWithColumnName()
    {
        const string csv = """
        [RECIPE]
        Key,Value
        Name,잘못된 숫자

        [STEPS]
        StepId,Kind,TargetChamberTemperature
        step-1,ChamberControl,not-a-number
        """;

        var ex = Assert.Throws<InvalidDataException>(() => RecipeCsvSerializer.Deserialize(csv));

        Assert.Contains(nameof(RecipeStep.TargetChamberTemperature), ex.Message);
        Assert.Contains("not-a-number", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Serialize_InvalidAgentIdSeparator_ThrowsInvalidDataException()
    {
        var recipe = BuildHeaderRecipe("잘못된 AgentId");
        recipe.Steps.Add(new RecipeStep
        {
            Kind = RecipeStepKind.CameraCommand,
            CameraTargets = new List<RecipeCameraTarget>
            {
                new() { AgentId = "Agent|1", CameraIndex = 1 }
            }
        });

        Assert.Throws<InvalidDataException>(() => RecipeCsvSerializer.Serialize(recipe));
    }

    [Fact]
    public void Deserialize_WithoutRecipeMarker_ThrowsInvalidDataException()
    {
        const string csv = """
        [STEPS]
        StepId,Kind
        step-1,Wait
        """;

        Assert.Throws<InvalidDataException>(() => RecipeCsvSerializer.Deserialize(csv));
    }

    [Fact]
    public void Deserialize_RecipeWithoutStepsSection_HasZeroSteps()
    {
        const string csv = """
        [RECIPE]
        Key,Value
        Name,스텝 없음
        """;

        Recipe recipe = RecipeCsvSerializer.Deserialize(csv);

        Assert.Equal("스텝 없음", recipe.Name);
        Assert.Empty(recipe.Steps);
    }

    [Fact]
    public void LegacyCapture_RoundTripsAllStepFields()
    {
        var recipe = BuildHeaderRecipe("레거시 보존");
        RecipeStep legacy = BuildStep(RecipeStepKind.LegacyCapture, 9);
        recipe.Steps.Add(legacy);

        Recipe imported = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(recipe));

        RecipeStep roundTrip = Assert.Single(imported.Steps);
        AssertStepEqual(legacy, roundTrip);
        Assert.Equal(legacy.ShotCount, roundTrip.ShotCount);
        Assert.Equal(legacy.CameraIndex, roundTrip.CameraIndex);
        Assert.Equal(legacy.TargetChamberTemperature, roundTrip.TargetChamberTemperature);
        Assert.Equal(legacy.WaitForCaptureResult, roundTrip.WaitForCaptureResult);
    }

    [Fact]
    public void Deserialize_DuplicatedCameraCommandRow_GetsFreshStepIdAndEditedTargets()
    {
        var recipe = BuildHeaderRecipe("행 복사 테스트");
        recipe.Steps.Add(new RecipeStep
        {
            Kind = RecipeStepKind.CameraCommand,
            CameraOperation = CameraControlOps.Capture,
            CameraIndex = 1,
            ShotCount = 1,
            WaitForCaptureResult = true,
            CameraTargets = new List<RecipeCameraTarget>
            {
                new() { CameraIndex = 1, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Hot }
            }
        });

        List<string> lines = RecipeCsvSerializer.Serialize(recipe).Replace("\r\n", "\n").Split('\n').ToList();
        int stepsMarker = lines.FindIndex(line => line.Trim() == "[STEPS]");
        int headerIndex = stepsMarker + 1;
        int rowIndex = headerIndex + 1;
        string[] headers = lines[headerIndex].Split(',');
        string[] copiedCells = lines[rowIndex].Split(',', StringSplitOptions.None);
        int targetsColumn = Array.IndexOf(headers, nameof(RecipeStep.CameraTargets));
        copiedCells[targetsColumn] = "1:Low:Hot|2:Low:Hot|3:Low:Hot|4:Low:Hot";
        lines.Insert(rowIndex + 1, string.Join(",", copiedCells));

        Recipe imported = RecipeCsvSerializer.Deserialize(string.Join(Environment.NewLine, lines));

        Assert.Equal(2, imported.Steps.Count);
        Assert.NotEqual(imported.Steps[0].StepId, imported.Steps[1].StepId);
        AssertCameraTargetsEqual(
            new[]
            {
                new RecipeCameraTarget { CameraIndex = 1, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Hot },
                new RecipeCameraTarget { CameraIndex = 2, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Hot },
                new RecipeCameraTarget { CameraIndex = 3, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Hot },
                new RecipeCameraTarget { CameraIndex = 4, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Hot }
            },
            imported.Steps[1].CameraTargets);
    }

    [Fact]
    public void Serialize_WritesGuideFirst_RemovesStepId_AndDocumentsEveryStepColumn()
    {
        string csv = RecipeCsvSerializer.Serialize(BuildHeaderRecipe("가이드 테스트"));
        List<string> lines = csv.Replace("\r\n", "\n").Split('\n').ToList();
        int stepsMarker = lines.FindIndex(line => line.Trim() == "[STEPS]");
        string[] stepColumns = lines[stepsMarker + 1].Split(',');
        IReadOnlySet<string> catalogColumns = StepCatalogColumns();

        Assert.Equal("[GUIDE]", lines[0]);
        Assert.DoesNotContain(nameof(RecipeStep.StepId), stepColumns);
        foreach (string column in stepColumns)
            Assert.Contains(column, catalogColumns);
    }

    private static Recipe BuildFullRecipe(string name)
    {
        var recipe = BuildHeaderRecipe(name);
        int index = 1;
        foreach (RecipeStepKind kind in Enum.GetValues<RecipeStepKind>())
        {
            recipe.Steps.Add(BuildStep(kind, index));
            index++;
        }

        return recipe;
    }

    private static Recipe BuildHeaderRecipe(string name)
    {
        return new Recipe
        {
            Id = $"recipe-{Guid.NewGuid():N}",
            Name = name,
            TemperatureRampMinutes = 12,
            RecordOnTemperatureDelta = 1.25f,
            RecordOnHumidityDelta = 2.5f,
            RecordIntervalSeconds = 30,
            SaveRootPath = @"\\master\capture",
            ProductNumber = "P-001",
            SaveFormat = ProductionCaptureFormat.Jpeg
        };
    }

    private static RecipeStep BuildStep(RecipeStepKind kind, int index)
    {
        var step = new RecipeStep();
        foreach (var property in StepProperties())
        {
            if (property.Name == nameof(RecipeStep.CameraTargets))
                continue;

            property.SetValue(step, SampleStepValue(property, kind, index));
        }

        step.CameraTargets = index == 1
            ? new List<RecipeCameraTarget>
            {
                new() { AgentId = "Agent_1", CameraIndex = 1 },
                new() { AgentId = "Agent_2", CameraIndex = 2, TargetChamber = ChamberRange.High, TargetBlackBody = BlackBodyRole.Hot }
            }
            : new List<RecipeCameraTarget>
            {
                new() { AgentId = $"Agent_{index}", CameraIndex = index, TargetChamber = ChamberRange.Low, TargetBlackBody = BlackBodyRole.Cold }
            };

        return step;
    }

    private static object SampleStepValue(PropertyInfo property, RecipeStepKind kind, int index)
    {
        if (property.Name == nameof(RecipeStep.Kind))
            return kind;

        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
            return $"{property.Name}-{index}";
        if (type == typeof(int))
            return index * 100 + property.Name.Length;
        if (type == typeof(float))
            return index * 10 + property.Name.Length + 0.25f;
        if (type == typeof(double))
            return index * 20 + property.Name.Length + 0.5d;
        if (type == typeof(bool))
            return property.GetValue(new RecipeStep()) is bool defaultValue ? !defaultValue : true;
        if (type == typeof(MotorMoveType))
            return MotorMoveType.Automatic;
        if (type.IsEnum)
            return EnumValue(type, index);

        throw new NotSupportedException($"테스트 샘플 값을 만들 수 없는 속성입니다: {property.Name}");
    }

    private static object EnumValue(Type type, int index)
    {
        Array values = Enum.GetValues(type);
        object? value = values.GetValue(index % values.Length);
        if (value == null)
            throw new InvalidOperationException($"Enum 값이 없습니다: {type.Name}");

        return value;
    }

    private static void AssertRecipeEqual(Recipe expected, Recipe actual)
    {
        AssertRecipeHeaderEqual(expected, actual);
        Assert.Equal(expected.Steps.Count, actual.Steps.Count);
        for (int i = 0; i < expected.Steps.Count; i++)
            AssertStepEqual(expected.Steps[i], actual.Steps[i]);
    }

    private static void AssertRecipeHeaderEqual(Recipe expected, Recipe actual)
    {
        foreach (var property in RecipeProperties())
        {
            if (property.Name == nameof(Recipe.Steps))
                continue;

            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
    }

    private static void AssertStepEqual(RecipeStep expected, RecipeStep actual)
    {
        RecipeStep defaultStep = new();
        IReadOnlyDictionary<string, HashSet<RecipeStepKind>> catalog = StepCatalogKinds();
        foreach (var property in StepProperties())
        {
            if (property.Name == nameof(RecipeStep.StepId))
            {
                // StepId is intentionally not serialized: copy-pasted CSV rows must receive fresh correlation ids.
                Assert.NotEqual(expected.StepId, actual.StepId);
                continue;
            }

            bool relevant = catalog[property.Name].Contains(expected.Kind);
            if (property.Name == nameof(RecipeStep.CameraTargets))
            {
                if (relevant)
                    AssertCameraTargetsEqual(expected.CameraTargets, actual.CameraTargets);
                else
                    Assert.Empty(actual.CameraTargets);
                continue;
            }

            Assert.Equal(relevant ? property.GetValue(expected) : property.GetValue(defaultStep), property.GetValue(actual));
        }
    }

    private static void AssertCameraTargetsEqual(IReadOnlyList<RecipeCameraTarget> expected, IReadOnlyList<RecipeCameraTarget> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].AgentId, actual[i].AgentId);
            Assert.Equal(expected[i].CameraIndex, actual[i].CameraIndex);
            Assert.Equal(expected[i].TargetChamber, actual[i].TargetChamber);
            Assert.Equal(expected[i].TargetBlackBody, actual[i].TargetBlackBody);
        }
    }

    private static PropertyInfo[] RecipeProperties()
    {
        return typeof(Recipe).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
    }

    private static PropertyInfo[] StepProperties()
    {
        return typeof(RecipeStep).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
    }

    private static IReadOnlySet<string> StepCatalogColumns()
    {
        return StepCatalogKinds().Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, HashSet<RecipeStepKind>> StepCatalogKinds()
    {
        var field = typeof(RecipeCsvSerializer).GetField("StepCatalog", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var entries = (IEnumerable<object>)field.GetValue(null)!;
        var result = new Dictionary<string, HashSet<RecipeStepKind>>(StringComparer.Ordinal);

        foreach (object entry in entries)
        {
            Type type = entry.GetType();
            var property = (PropertyInfo)type.GetProperty("Property")!.GetValue(entry)!;
            var kinds = (IEnumerable<RecipeStepKind>)type.GetProperty("Kinds")!.GetValue(entry)!;
            result[property.Name] = kinds.ToHashSet();
        }

        return result;
    }
}
