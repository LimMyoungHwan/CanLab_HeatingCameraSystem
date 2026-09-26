using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services;

public static class RecipeCsvSerializer
{
    private const char WriteDelimiter = ',';
    private const string GuideMarker = "[GUIDE]";
    private const string RecipeMarker = "[RECIPE]";
    private const string StepsMarker = "[STEPS]";

    private static readonly PropertyInfo[] RecipeProperties = GetSerializableProperties(typeof(Recipe), nameof(Recipe.Steps));
    private static readonly PropertyInfo[] StepProperties = GetSerializableProperties(typeof(RecipeStep), nameof(RecipeStep.StepId));
    private static readonly StepColumn[] StepCatalog = CreateStepCatalog();
    private static readonly Dictionary<string, StepColumn> StepCatalogByName = StepCatalog.ToDictionary(column => column.Property.Name, StringComparer.Ordinal);
    private static readonly HashSet<PropertyInfo> NullableStringProperties = GetNullableStringProperties(RecipeProperties.Concat(StepProperties));

    public static string Serialize(Recipe recipe)
    {
        var sb = new StringBuilder();

        AppendGuide(sb);

        sb.AppendLine(RecipeMarker);
        AppendRecord(sb, new[] { "Key", "Value" }, WriteDelimiter);
        foreach (var property in RecipeProperties)
            AppendRecord(sb, new[] { property.Name, ValueToText(property, property.GetValue(recipe)) }, WriteDelimiter);

        sb.AppendLine();
        sb.AppendLine(StepsMarker);
        AppendRecord(sb, StepProperties.Select(property => property.Name), WriteDelimiter);
        foreach (var step in recipe.Steps)
            AppendRecord(sb, StepProperties.Select(property => StepValueToText(step, property)), WriteDelimiter);

        return sb.ToString();
    }

    /// <summary>
    /// CSV를 레시피로 읽는다. 빈 셀은 nullable string 속성에서는 null로, non-null string 속성에서는 빈 문자열로 읽는다.
    /// </summary>
    public static Recipe Deserialize(string csv)
    {
        var records = ReadRecords(csv).Where(record => record.Trim().Length > 0).ToList();
        int recipeIndex = FindMarker(records, RecipeMarker);
        if (recipeIndex < 0)
            throw new InvalidDataException("레시피 CSV 파일이 아닙니다. [RECIPE] 섹션이 없습니다.");

        int stepsIndex = FindMarker(records, StepsMarker);
        char delimiter = DetectDelimiter(records, recipeIndex, stepsIndex);
        var recipe = new Recipe();

        ReadRecipeSection(recipe, records, recipeIndex, stepsIndex, delimiter);
        if (stepsIndex >= 0)
            ReadStepsSection(recipe, records, stepsIndex, delimiter);

        return recipe;
    }

    public static void WriteFile(string path, Recipe recipe)
    {
        File.WriteAllText(path, Serialize(recipe), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static Recipe ReadFile(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        return Deserialize(reader.ReadToEnd());
    }

    private static PropertyInfo[] GetSerializableProperties(Type type, params string[] excludedNames)
    {
        var excluded = excludedNames.ToHashSet(StringComparer.Ordinal);
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod?.IsPublic == true
                && property.SetMethod?.IsPublic == true
                && !excluded.Contains(property.Name))
            .OrderBy(property => property.MetadataToken)
            .ToArray();
    }

    private sealed record StepColumn(PropertyInfo Property, string Meaning, string UnitRange, string UsedBy, string Example, IReadOnlySet<RecipeStepKind> Kinds);

    private static StepColumn[] CreateStepCatalog()
    {
        var common = Enum.GetValues<RecipeStepKind>().ToHashSet();
        var motorMove = Set(RecipeStepKind.MotorMove);
        var chamber = Set(RecipeStepKind.ChamberControl);
        var humidity = Set(RecipeStepKind.HumidityControl);
        var blackBody = Set(RecipeStepKind.BlackBodyControl);
        var camera = Set(RecipeStepKind.CameraCommand);
        var wait = Set(RecipeStepKind.Wait);
        var captureJoin = Set(RecipeStepKind.CaptureJoin);
        var fan = Set(RecipeStepKind.FanControl);

        return new[]
        {
            Column(nameof(RecipeStep.Kind), "실행할 장치·동작 종류", "RecipeStepKind 값", "전체", "CameraCommand", common),
            Column(nameof(RecipeStep.CameraOperation), "카메라 명령", "CameraControlOps 값", "CameraCommand", CameraControlOps.Capture, camera),
            Column(nameof(RecipeStep.CameraTargets), "카메라별 촬영 대상", "CameraIndex:대역:흑체[:AgentId] | 로 여러 개", "CameraCommand", "1:Low:Hot|2:Low:Hot|3:Low:Hot|4:Low:Hot", camera),
            Column(nameof(RecipeStep.CameraIndex), "대상 카메라 인덱스", "1~64", "CameraCommand", "1", camera),
            Column(nameof(RecipeStep.CameraAlias), "운영자가 부여한 카메라 별칭", "비우면 CameraIndex 기반", "CameraCommand", "CAM-01", camera),
            Column(nameof(RecipeStep.TargetPositionIndex), "서보 유닛이 이동해야 할 위치", "포인트 번호", "MotorMove", "3", motorMove),
            Column(nameof(RecipeStep.MotorMoveType), "서보 이동 방식", "Manual 또는 Automatic", "MotorMove", "Automatic", motorMove),
            Column(nameof(RecipeStep.TargetBlackBodyTemperature), "블랙바디 목표 온도", "℃", "BlackBodyControl", "35.5", blackBody),
            Column(nameof(RecipeStep.TargetBlackBodyTemperature1), "두 번째 블랙바디 목표 온도", "℃, 0이면 첫 번째 목표 사용", "BlackBodyControl", "35.5", blackBody),
            Column(nameof(RecipeStep.BlackBodyIndex), "제어할 블랙바디 인덱스", "0=흑체1, 1=흑체2", "BlackBodyControl", "0", blackBody),
            Column(nameof(RecipeStep.WaitForStabilization), "목표 온도 도달까지 대기", "True/False 또는 1/0", "BlackBodyControl", "True", blackBody),
            Column(nameof(RecipeStep.ShotCount), "한 번에 찍을 장수", "1 이상", "CameraCommand", "1", camera),
            Column(nameof(RecipeStep.CaptureIntervalSeconds), "몇 초마다 다시 찍는가", "초, 0이면 반복 안 함", "CameraCommand", "60", camera),
            Column(nameof(RecipeStep.CaptureDurationSeconds), "캡처 반복 전체 시간", "초", "CameraCommand", "1800", camera),
            Column(nameof(RecipeStep.BiasTargetLevel), "BIAS 자동 탐색 목표 레벨", "0이면 Agent 기본값", "CameraCommand", "8500", camera),
            Column(nameof(RecipeStep.PositionX), "서보 유닛 직접 이동 X 좌표", "mm", "MotorMove", "120.5", motorMove),
            Column(nameof(RecipeStep.PositionY), "서보 유닛 직접 이동 Y 좌표", "mm", "MotorMove", "45.0", motorMove),
            Column(nameof(RecipeStep.TargetChamberTemperature), "챔버 목표 온도", "℃", "ChamberControl", "25.0", chamber),
            Column(nameof(RecipeStep.TargetFanSpeedHz), "챔버 순환 팬 목표 속도", "Hz, 10.00~60.00", "FanControl", "30.0", fan),
            Column(nameof(RecipeStep.TargetChamberHumidity), "챔버 목표 습도", "%RH", "HumidityControl", "50.0", humidity),
            Column(nameof(RecipeStep.DisableHumidityControl), "챔버 습도 제어 끄기", "True/False 또는 1/0", "HumidityControl", "False", humidity),
            Column(nameof(RecipeStep.WaitForChamberStabilization), "챔버 목표값 도달까지 대기", "True/False 또는 1/0", "ChamberControl, HumidityControl", "True", chamber, humidity),
            Column(nameof(RecipeStep.StabilizationToleranceC), "온도 도달 판정 폭", "±℃, 0 이하면 전역 설정", "ChamberControl, BlackBodyControl", "0.5", chamber, blackBody),
            Column(nameof(RecipeStep.StabilizationToleranceRh), "습도 도달 판정 폭", "±%RH, 0 이하면 5%RH", "HumidityControl", "5.0", humidity),
            Column(nameof(RecipeStep.SoakMinutes), "목표 도달 후 유지 시간", "분, 0이면 즉시 진행", "ChamberControl, HumidityControl, BlackBodyControl", "10", chamber, humidity, blackBody),
            Column(nameof(RecipeStep.UseSafetyTemperature), "챔버 온도 안전 범위 검사", "True/False 또는 1/0", "ChamberControl", "True", chamber),
            Column(nameof(RecipeStep.SafetyTempMin), "안전 최저 온도", "℃", "ChamberControl", "15.0", chamber),
            Column(nameof(RecipeStep.SafetyTempMax), "안전 최고 온도", "℃", "ChamberControl", "60.0", chamber),
            Column(nameof(RecipeStep.UseSafetyHumidity), "챔버 습도 안전 범위 검사", "True/False 또는 1/0", "HumidityControl", "True", humidity),
            Column(nameof(RecipeStep.SafetyHumidityMin), "안전 최저 습도", "%RH", "HumidityControl", "20.0", humidity),
            Column(nameof(RecipeStep.SafetyHumidityMax), "안전 최고 습도", "%RH", "HumidityControl", "80.0", humidity),
            Column(nameof(RecipeStep.WaitDurationSeconds), "대기 스텝의 총 대기 시간", "초", "Wait", "300", wait),
            Column(nameof(RecipeStep.WaitForCaptureResult), "캡처 결과를 기다릴지 여부", "False면 CaptureJoin 필요", "CameraCommand", "True", camera),
            Column(nameof(RecipeStep.CaptureJoinTimeoutSeconds), "촬영 대기 한도", "초, 0이면 전역 설정", "CaptureJoin", "600", captureJoin)
        };

        static IReadOnlySet<RecipeStepKind> Set(params RecipeStepKind[] kinds) => kinds.Append(RecipeStepKind.LegacyCapture).ToHashSet();
    }

    private static StepColumn Column(string propertyName, string meaning, string unitRange, string usedBy, string example, params IReadOnlySet<RecipeStepKind>[] kindSets)
    {
        var property = StepProperties.Single(p => p.Name == propertyName);
        string usedByText = usedBy == "전체" ? usedBy : $"LegacyCapture, {usedBy}";
        return new StepColumn(property, meaning, unitRange, usedByText, example, kindSets.SelectMany(kinds => kinds).ToHashSet());
    }

    private static void AppendGuide(StringBuilder sb)
    {
        sb.AppendLine(GuideMarker);
        AppendRecord(sb, new[] { "사용법: 새 스텝은 [STEPS] 표에서 비슷한 기존 행을 복사해 아래에 붙여넣고 필요한 셀만 바꾸세요." }, WriteDelimiter);
        AppendRecord(sb, new[] { "[STEPS] 표의 행 순서가 곧 실행 순서입니다." }, WriteDelimiter);
        AppendRecord(sb, new[] { "중간에 행을 끼워 넣으면 그 위치의 스텝으로 삽입됩니다 — 맨 아래에만 붙일 필요가 없습니다." }, WriteDelimiter);
        AppendRecord(sb, new[] { "행을 위아래로 옮기면 실행 순서가 그대로 바뀝니다." }, WriteDelimiter);
        AppendRecord(sb, new[] { "Kind 값이 그 행의 동작 종류를 정합니다. Kind와 무관한 열은 비어 있는 것이 정상입니다." }, WriteDelimiter);
        AppendRecord(sb, new[] { "비어 있지 않은 관련 열은 실제 장비 동작에 쓰입니다. 관련 열을 지우거나 잘못 쓰면 가져오기에서 오류가 납니다." }, WriteDelimiter);
        AppendRecord(sb, new[] { "StepId는 자동 발급되므로 입력 열이 없습니다. 복사한 행도 가져올 때 새 StepId를 받습니다." }, WriteDelimiter);
        sb.AppendLine();

        AppendRecord(sb, new[] { "컬럼", "뜻", "단위/범위", "쓰는 스텝", "예시" }, WriteDelimiter);
        foreach (var column in StepCatalog)
            AppendRecord(sb, new[] { column.Property.Name, column.Meaning, column.UnitRange, column.UsedBy, column.Example }, WriteDelimiter);
        sb.AppendLine();

        AppendRecord(sb, new[] { "구분", "값", "뜻" }, WriteDelimiter);
        AppendEnumLegend(sb, "RecipeStepKind", Enum.GetValues<RecipeStepKind>().Select(value => (value.ToString(), StepKindMeaning(value))));
        AppendEnumLegend(sb, "MotorMoveType", Enum.GetValues<MotorMoveType>().Select(value => (value.ToString(), MotorMoveTypeMeaning(value))));
        AppendEnumLegend(sb, "ChamberRange", Enum.GetValues<ChamberRange>().Select(value => (value.ToString(), ChamberRangeMeaning(value))));
        AppendEnumLegend(sb, "BlackBodyRole", Enum.GetValues<BlackBodyRole>().Select(value => (value.ToString(), BlackBodyRoleMeaning(value))));
        AppendEnumLegend(sb, "ProductionCaptureFormat", Enum.GetValues<ProductionCaptureFormat>().Select(value => (value.ToString(), ProductionCaptureFormatMeaning(value))));
        AppendEnumLegend(sb, "CameraControlOps", CameraControlValues().Select(value => (value, CameraControlMeaning(value))));
        sb.AppendLine();

        AppendRecord(sb, new[] { "CameraTargets 형식: CameraIndex:대역:흑체[:AgentId], 여러 대는 | 로 연결합니다. 예: 1:Low:Hot|2:Low:Hot|3:Low:Hot|4:Low:Hot" }, WriteDelimiter);
        sb.AppendLine();
    }

    private static void AppendEnumLegend(StringBuilder sb, string group, IEnumerable<(string Value, string Meaning)> rows)
    {
        foreach (var row in rows)
            AppendRecord(sb, new[] { group, row.Value, row.Meaning }, WriteDelimiter);
    }

    private static IEnumerable<string> CameraControlValues()
    {
        return typeof(CameraControlOps)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue())
            .Where(value => value != null)
            .Cast<string>();
    }

    private static string StepKindMeaning(RecipeStepKind value) => value switch
    {
        RecipeStepKind.LegacyCapture => "구버전 종류입니다. 현재 실행되지 않으므로 새 스텝에 사용하지 마세요.",
        RecipeStepKind.MotorMove => "PLC 모터 이동",
        RecipeStepKind.ChamberControl => "챔버 온도 제어",
        RecipeStepKind.CameraCommand => "카메라 명령",
        RecipeStepKind.BlackBodyControl => "블랙바디 온도 제어",
        RecipeStepKind.HumidityControl => "챔버 습도 제어",
        RecipeStepKind.Wait => "지정 시간 대기",
        RecipeStepKind.CaptureJoin => "fork 캡처 완료 대기",
        RecipeStepKind.FanControl => "챔버 순환 팬 속도 지정",
        _ => value.ToString()
    };

    private static string MotorMoveTypeMeaning(MotorMoveType value) => value switch
    {
        MotorMoveType.Manual => "X/Y 좌표로 직접 이동",
        MotorMoveType.Automatic => "포인트 번호로 자동 이동",
        _ => value.ToString()
    };

    private static string ChamberRangeMeaning(ChamberRange value) => value switch
    {
        ChamberRange.Low => "저온 대역",
        ChamberRange.Mid => "상온 대역",
        ChamberRange.High => "고온 대역",
        _ => value.ToString()
    };

    private static string BlackBodyRoleMeaning(BlackBodyRole value) => value switch
    {
        BlackBodyRole.Hot => "hot 흑체",
        BlackBodyRole.Cold => "cold 흑체",
        BlackBodyRole.Room => "room, 흑체 없음",
        _ => value.ToString()
    };

    private static string ProductionCaptureFormatMeaning(ProductionCaptureFormat value) => value switch
    {
        ProductionCaptureFormat.Raw => "16비트 원본",
        ProductionCaptureFormat.Jpeg => "8비트 JPEG, 육안 검사·보고서용",
        _ => value.ToString()
    };

    private static string CameraControlMeaning(string value) => value switch
    {
        CameraControlOps.Run => "카메라 RUN",
        CameraControlOps.Stop => "카메라 STOP",
        CameraControlOps.ShutterOpen => "셔터 열기",
        CameraControlOps.ShutterClose => "셔터 닫기",
        CameraControlOps.Capture => "캡처",
        CameraControlOps.Nuc => "NUC 실행",
        CameraControlOps.Bias => "타겟 온도대역에 맞춰 BIAS 치환",
        CameraControlOps.BiasLow => "BIAS LOW",
        CameraControlOps.BiasMid => "BIAS MID",
        CameraControlOps.BiasHigh => "BIAS HIGH",
        CameraControlOps.SaveConfig => "설정 저장",
        CameraControlOps.RefreshInfo => "정보 갱신",
        CameraControlOps.CaptureAbort => "진행 중인 버스트 캡처 중단",
        CameraControlOps.RuntimeLoad => "카메라 런타임 로드",
        CameraControlOps.RuntimeUnload => "카메라 런타임 언로드",
        _ => value
    };

    private static void AppendRecord(StringBuilder sb, IEnumerable<string> fields, char delimiter)
    {
        bool first = true;
        foreach (string field in fields)
        {
            if (!first)
                sb.Append(delimiter);

            sb.Append(Escape(field, delimiter));
            first = false;
        }

        sb.AppendLine();
    }

    private static string Escape(string field, char delimiter)
    {
        if (field.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) < 0)
            return field;

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static string ValueToText(PropertyInfo property, object? value)
    {
        if (property.Name == nameof(RecipeStep.CameraTargets))
            return FormatCameraTargets(value as IEnumerable<RecipeCameraTarget>);

        if (value == null)
            return string.Empty;

        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
            return (string)value;
        if (type == typeof(int))
            return ((int)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(float))
            return ((float)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(double))
            return ((double)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(bool))
            return ((bool)value).ToString();
        if (type.IsEnum)
            return value.ToString() ?? string.Empty;

        throw new NotSupportedException($"CSV로 저장할 수 없는 속성 형식입니다: {property.Name} ({property.PropertyType.Name})");
    }

    private static string StepValueToText(RecipeStep step, PropertyInfo property)
    {
        if (!StepCatalogByName.TryGetValue(property.Name, out var column))
            throw new InvalidOperationException($"CSV 컬럼 카탈로그에 누락된 속성입니다: {property.Name}");

        return column.Kinds.Contains(step.Kind) ? ValueToText(property, property.GetValue(step)) : string.Empty;
    }

    private static string FormatCameraTargets(IEnumerable<RecipeCameraTarget>? targets)
    {
        if (targets == null)
            return string.Empty;

        return string.Join("|", targets.Select(target =>
        {
            if (target.AgentId.Contains(':') || target.AgentId.Contains('|'))
                throw new InvalidDataException($"AgentId에 ':' 또는 '|' 문자를 사용할 수 없습니다: {target.AgentId}");

            string[] parts =
            {
                target.CameraIndex.ToString(CultureInfo.InvariantCulture),
                target.TargetChamber?.ToString() ?? string.Empty,
                target.TargetBlackBody?.ToString() ?? string.Empty,
                target.AgentId
            };
            int last = parts.Length - 1;
            while (last > 0 && parts[last].Length == 0)
                last--;

            return string.Join(":", parts.Take(last + 1));
        }));
    }

    private static void ReadRecipeSection(Recipe recipe, IReadOnlyList<string> records, int recipeIndex, int stepsIndex, char delimiter)
    {
        var properties = RecipeProperties.ToDictionary(property => property.Name, StringComparer.Ordinal);
        int end = stepsIndex >= 0 ? stepsIndex : records.Count;
        int start = recipeIndex + 1;

        if (start < end)
        {
            var first = ParseFields(records[start], delimiter);
            if (first.Count >= 2
                && string.Equals(first[0], "Key", StringComparison.OrdinalIgnoreCase)
                && string.Equals(first[1], "Value", StringComparison.OrdinalIgnoreCase))
                start++;
        }

        for (int i = start; i < end; i++)
        {
            var fields = ParseFields(records[i], delimiter);
            if (fields.Count == 0 || !properties.TryGetValue(fields[0], out var property))
                continue;

            SetValue(recipe, property, fields.Count > 1 ? fields[1] : string.Empty);
        }
    }

    private static void ReadStepsSection(Recipe recipe, IReadOnlyList<string> records, int stepsIndex, char delimiter)
    {
        int headerIndex = stepsIndex + 1;
        if (headerIndex >= records.Count)
            return;

        var header = ParseFields(records[headerIndex], delimiter);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            if (!columns.ContainsKey(header[i]))
                columns.Add(header[i], i);
        }

        foreach (string record in records.Skip(headerIndex + 1))
        {
            if (IsMarker(record))
                break;

            var fields = ParseFields(record, delimiter);
            if (fields.All(string.IsNullOrEmpty))
                continue;

            var step = new RecipeStep { Kind = ReadStepKind(columns, fields) };
            foreach (var property in StepProperties)
            {
                if (!columns.TryGetValue(property.Name, out int column))
                    continue;

                if (property.Name == nameof(RecipeStep.Kind))
                    continue;

                string text = column < fields.Count ? fields[column] : string.Empty;
                bool relevant = IsRelevant(property, step.Kind);
                if (text.Length == 0 && !relevant)
                    continue;

                if (property.Name == nameof(RecipeStep.CameraTargets))
                    step.CameraTargets = ParseCameraTargets(property, text);
                else
                    SetValue(step, property, text);
            }

            recipe.Steps.Add(step);
        }
    }

    private static RecipeStepKind ReadStepKind(IReadOnlyDictionary<string, int> columns, IReadOnlyList<string> fields)
    {
        var property = StepProperties.Single(p => p.Name == nameof(RecipeStep.Kind));
        if (!columns.TryGetValue(property.Name, out int column))
            return RecipeStepKind.LegacyCapture;

        string text = column < fields.Count ? fields[column] : string.Empty;
        return (RecipeStepKind)(TextToValue(property, text) ?? RecipeStepKind.LegacyCapture);
    }

    private static bool IsRelevant(PropertyInfo property, RecipeStepKind kind)
    {
        return StepCatalogByName.TryGetValue(property.Name, out var column) && column.Kinds.Contains(kind);
    }

    private static void SetValue(object target, PropertyInfo property, string text)
    {
        property.SetValue(target, TextToValue(property, text));
    }

    private static object? TextToValue(PropertyInfo property, string text)
    {
        Type type = property.PropertyType;
        Type? nullableType = Nullable.GetUnderlyingType(type);
        Type valueType = nullableType ?? type;

        try
        {
            if (valueType == typeof(string))
                return text.Length == 0 && IsNullableString(property) ? null : text;
            if (valueType == typeof(int))
                return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (valueType == typeof(float))
                return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (valueType == typeof(double))
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (valueType == typeof(bool))
                return ParseBool(text);
            if (valueType.IsEnum)
                return text.Length == 0 && nullableType != null ? null : Enum.Parse(valueType, text, ignoreCase: true);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw CannotReadColumn(property, text, ex);
        }

        throw new NotSupportedException($"CSV에서 읽을 수 없는 속성 형식입니다: {property.Name} ({property.PropertyType.Name})");
    }

    private static bool IsNullableString(PropertyInfo property)
    {
        return NullableStringProperties.Contains(property);
    }

    private static HashSet<PropertyInfo> GetNullableStringProperties(IEnumerable<PropertyInfo> properties)
    {
        var context = new NullabilityInfoContext();
        return properties
            .Where(property => property.PropertyType == typeof(string))
            .Where(property =>
            {
                var info = context.Create(property);
                return info.ReadState == NullabilityState.Nullable || info.WriteState == NullabilityState.Nullable;
            })
            .ToHashSet();
    }

    private static bool ParseBool(string text)
    {
        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new FormatException($"Boolean 값이 아닙니다: {text}");
    }

    private static List<RecipeCameraTarget> ParseCameraTargets(PropertyInfo property, string text)
    {
        var targets = new List<RecipeCameraTarget>();
        if (string.IsNullOrEmpty(text))
            return targets;

        try
        {
            foreach (string item in text.Split('|'))
            {
                string[] parts = item.Split(':');
                if (parts.Length is < 1 or > 4)
                    throw new FormatException($"CameraTargets 형식이 올바르지 않습니다: {item}");

                targets.Add(new RecipeCameraTarget
                {
                    CameraIndex = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture),
                    TargetChamber = parts.Length < 2 || parts[1].Length == 0 ? null : (ChamberRange)Enum.Parse(typeof(ChamberRange), parts[1], ignoreCase: true),
                    TargetBlackBody = parts.Length < 3 || parts[2].Length == 0 ? null : (BlackBodyRole)Enum.Parse(typeof(BlackBodyRole), parts[2], ignoreCase: true),
                    AgentId = parts.Length < 4 ? string.Empty : parts[3]
                });
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw CannotReadColumn(property, text, ex);
        }

        return targets;
    }

    private static InvalidDataException CannotReadColumn(PropertyInfo property, string text, Exception inner)
    {
        return new InvalidDataException($"'{property.Name}' 열의 값을 읽을 수 없습니다: \"{text}\"", inner);
    }

    private static char DetectDelimiter(IReadOnlyList<string> records, int recipeIndex, int stepsIndex)
    {
        string sample = string.Empty;
        if (stepsIndex >= 0 && stepsIndex + 1 < records.Count)
            sample = records[stepsIndex + 1];
        else if (recipeIndex + 1 < records.Count)
            sample = records[recipeIndex + 1];

        return new[] { ',', ';', '\t' }
            .OrderByDescending(delimiter => CountFields(sample, delimiter))
            .First();
    }

    private static int CountFields(string record, char delimiter)
    {
        int count = 1;
        bool inQuotes = false;
        for (int i = 0; i < record.Length; i++)
        {
            char c = record[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < record.Length && record[i + 1] == '"')
                    i++;
                else
                    inQuotes = !inQuotes;
            }
            else if (!inQuotes && c == delimiter)
            {
                count++;
            }
        }

        return count;
    }

    private static List<string> ReadRecords(string csv)
    {
        var records = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"')
            {
                sb.Append(c);
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    sb.Append(csv[i + 1]);
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (!inQuotes && (c == '\r' || c == '\n'))
            {
                records.Add(sb.ToString());
                sb.Clear();
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            records.Add(sb.ToString());

        return records;
    }

    private static List<string> ParseFields(string record, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < record.Length; i++)
        {
            char c = record[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < record.Length && record[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"' && sb.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }

    private static int FindMarker(IReadOnlyList<string> records, string marker)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].Trim(), marker, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool IsMarker(string record)
    {
        string text = record.Trim();
        return string.Equals(text, GuideMarker, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, RecipeMarker, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, StepsMarker, StringComparison.OrdinalIgnoreCase);
    }
}
