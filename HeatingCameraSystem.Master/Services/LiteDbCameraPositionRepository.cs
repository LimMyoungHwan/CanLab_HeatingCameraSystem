using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Master.Services;

public sealed class LiteDbCameraPositionRepository : ICameraPositionRepository
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<CameraPositionEntry> _collection;
    public string MigrationWarning { get; private set; } = string.Empty;
    public LiteDbCameraPositionRepository(LiteDatabase db)
    {
        _db = db; _collection = db.GetCollection<CameraPositionEntry>("camera_positions");
    }
    public Task<IReadOnlyList<CameraPositionEntry>> GetAllAsync()
    {
        lock (_db)
        {
            var rows = _collection.FindAll().ToList();
            int unresolved = rows.Count(e => string.IsNullOrWhiteSpace(e.AgentId));
            MigrationWarning = unresolved == 0 ? string.Empty : $"이전 위치표 {unresolved}건의 실제 카메라/역할을 확인할 수 없습니다. 기존 행은 보존했습니다. 실제 카메라로 위치를 다시 등록하세요.";
            return Task.FromResult<IReadOnlyList<CameraPositionEntry>>(rows.Where(e => !string.IsNullOrWhiteSpace(e.AgentId)).OrderBy(e => e.AgentId).ThenBy(e => e.CameraIndex).ThenBy(e => e.Role).ToArray());
        }
    }
    public Task SaveAsync(CameraPositionEntry entry)
    {
        CameraPositionRules.ValidateEntry(entry);
        var saved = new CameraPositionEntry
        {
            Id = CapturePlanCompiler.PositionKey(entry.Camera, entry.Role), AgentId = entry.AgentId,
            CameraIndex = entry.CameraIndex, Role = entry.Role, Position = CapturePlanRecipeCopies.ClonePosition(entry.Position) ?? new()
        };
        lock (_db)
        {
            if (_collection.FindById(saved.Id)?.Position.Kind == CameraPositionKind.Point && saved.Position.Kind == CameraPositionKind.Coordinate)
                throw new InvalidOperationException("Point로 저장된 위치는 좌표로 바꿀 수 없습니다. 삭제 후 다시 등록하세요.");
            _collection.Upsert(saved);
        }
        return Task.CompletedTask;
    }
    public Task DeleteAsync(CameraIdentity camera, BlackBodyRole role)
    {
        CapturePlanCompiler.ValidateCamera(camera);
        lock (_db) _collection.Delete(CapturePlanCompiler.PositionKey(camera, role));
        return Task.CompletedTask;
    }

    /// <summary>Only migrate when recipe evidence uniquely identifies both camera and role; never guess AgentId suffixes.</summary>
    public Task MigrateLegacyAsync(IReadOnlyList<Recipe> recipes)
    {
        lock (_db)
        {
            var raw = _db.GetCollection<BsonDocument>("camera_positions");
            foreach (var old in raw.FindAll().Where(d => !d.ContainsKey("AgentId") || string.IsNullOrWhiteSpace(d["AgentId"].AsString)).ToArray())
            {
                var candidates = new Dictionary<string, (CameraIdentity Camera, BlackBodyRole Role)>();
                if (!old.ContainsKey("CameraIndex") || !old.ContainsKey("Position")) continue;
                int oldIndex = old["CameraIndex"].AsInt32;
                var position = BsonMapper.Global.ToObject<CameraPosition>(old["Position"].AsDocument);
                foreach (var recipe in recipes.Where(r => r.Plan == null))
                for (int i = 0; i < recipe.Steps.Count; i++)
                {
                    var move = recipe.Steps[i];
                    if (move.Kind != RecipeStepKind.MotorMove || !Matches(position, move)) continue;
                    var capture = recipe.Steps.Skip(i + 1).TakeWhile(s => s.Kind != RecipeStepKind.MotorMove)
                        .FirstOrDefault(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture);
                    if (capture?.CameraTargets.Count != 1) continue;
                    var target = capture.CameraTargets[0];
                    if (target.CameraIndex != oldIndex || string.IsNullOrWhiteSpace(target.AgentId) || target.TargetBlackBody is not { } role) continue;
                    var camera = new CameraIdentity { AgentId = target.AgentId, CameraIndex = target.CameraIndex };
                    candidates[CapturePlanCompiler.PositionKey(camera, role)] = (camera, role);
                }
                if (candidates.Count != 1) continue;
                var mapped = candidates.Values.Single();
                string id = CapturePlanCompiler.PositionKey(mapped.Camera, mapped.Role);
                // A previously taught v2 position wins; do not overwrite it with a migration.
                if (_collection.FindById(id) == null)
                    _collection.Upsert(new CameraPositionEntry { Id = id, AgentId = mapped.Camera.AgentId, CameraIndex = mapped.Camera.CameraIndex, Role = mapped.Role, Position = position });
                raw.Delete(old["_id"]);
            }
        }
        return Task.CompletedTask;
    }
    private static bool Matches(CameraPosition position, RecipeStep step) => position.Kind == CameraPositionKind.Point
        ? step.MotorMoveType == MotorMoveType.Automatic && position.PointNumber == step.TargetPositionIndex
        : step.MotorMoveType == MotorMoveType.Manual && position.X == step.PositionX && position.Y == step.PositionY;
}
