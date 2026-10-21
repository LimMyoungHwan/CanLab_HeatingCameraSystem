# 템플릿 전환 테스트 감사 (2026-10-21)

## 기준과 전체 결과

- 사용자 제공 기준: 보드 674 → UI 678 → shot-numbering 추가 약 8. 교체 직후 593, 이번 일반 안전 회귀 복구 후 **613 통과 / 실패 0 / 건너뜀 0**.
- `git diff --stat HEAD -- HeatingCameraSystem.Tests`: tracked 변경은 CaptureNamingRuleTests, ProductionCaptureSinkTests, RecipeCopyTests, RecipeCsvSerializerTests, RecipeEngineTests 5개뿐(259 insertions / 4 changed lines). HEAD의 모든 public void/Task 메서드 이름을 현재 파일과 대조했고 **누락 0**. tracked 파일 자체 삭제 0. 기존 비계획/엔진/모션 테스트는 보존했다.
- 아래 before는 사용자 제공 당시 실행 케이스 수(≥/범위 그대로 표시), after는 최종 TRX 실행 케이스 수(InlineData 확장 포함). 과거 새 파일은 HEAD에 없어 역사 transcript의 write/edit 선언으로 메서드 이름을 복구했으며, 과거 정확한 전체 InlineData 행 수를 추측하지 않았다.

## 그룹별 전환

| 그룹 | before | after | 이유 / 대응 |
|---|---:|---:|---|
| CameraPositionRepositoryTests.cs | 20 | 5 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| CameraPositionsViewModelTests.cs | 8 | 4 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| CapturePlanBoardTests.cs | 2+ | 6 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| CapturePlanCompilerTests.cs | 18+ | 15 | 고정 순서/조건별 BB dedup/강제 precondition 모델 제거. 전체 카메라 확장·번호·ID·원본 보존으로 교체. |
| CapturePlanConverterTests.cs | 22+ | 3 | 정확 문법 변환기 삭제. live 링크 생성·중첩 거절·flatten으로 교체. Wait/NUC 자유 스텝을 더 이상 거절하지 않음. |
| CapturePlanEditorViewModelTests.cs | 14 | 4 | 64행/행열토글/조건/park/pattern 전용 UI 제거. 항목/실제 카메라/live 슬롯 UI로 교체. 로드 실패 stale snapshot 회귀는 Safety에 복구. |
| CapturePlanPersistenceTests.cs | 8+ | 4 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| CapturePlanRunTests.cs | 5 | 4 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| DashboardCapturePlanTests.cs | 13 | 2 | 매트릭스 행/역할 열·64행 스크롤 전용 렌더 제거. 영속 카메라 보드 및 Safety의 missing-position/cancel/no-dialog/recovery 가드로 대체. |
| RecipePlanEditorIntegrationTests.cs | 19 | 3 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| RecipePlanEditorRenderingTests.cs | 1–3 | 1 | 구형 키/셀 모델의 테스트를 실제 identity/전체 스텝 셀로 재작성. 일반 저장/잠금/영수증/초안/복사 의미는 새 모델 테스트 및 Safety 회귀로 유지. |
| RecipeEngineOutcomeTests | 14 | 14 | 파일 변경 없음. 부분 배치/반복 실패/fork/join/이력 저장 실패/취소 이벤트 모두 유지 |
| TemplatePlanSafetyRegressionTests | 0 | 20 | 일반 회귀 복구: invalid 위치 8, 저장 오류 1, Point 쓰기 확인 2, 취소 영속 1, 초안 저장소 3, 위치 로드 실패 1, Dashboard 가드 4 |

## 교체된 파일과 확인된 이전 메서드 선언

11개 파일은 삭제한 것이 아니라 같은 경로에서 새 모델 테스트로 재작성했다. 아래 목록은 이전 transcript에서 확인된 선언이다. 매트릭스 전용 이외의 동작은 위 대응과 Safety 회귀로 보존한다.

### CameraPositionRepositoryTests.cs
- `ConcurrentRepositories_CannotReplacePersistedPointWithCoordinate`
- `CoordinateCanBecomePoint_PointNumberCanChange_DeleteUnlocksCoordinate`
- `InvalidIndices_AreRejectedByRulesAndRepository`
- `NonFiniteCoordinates_AreRejectedForBothPositionKinds`
- `PersistedPoint_RejectsCoordinateAcrossRepositories_WithoutChangingSavedRow`
- `PointBelowTwo_IsRejectedByRulesAndRepository`
- `Reopen_PreservesRowsAndPointLock_AndDeletionPersists`
- `Save_IsImmediatelyVisibleAcrossRepositories_UsesIndicesInsteadOfSuppliedId`
- `SavedAndReturnedObjects_AreDefensiveCopies`
- `UnknownPositionKind_IsRejectedByRulesAndRepository`

### CameraPositionsViewModelTests.cs
- `FetchCurrent_CopiesServoPositionIntoCoordinateRow`
- `Filter_ByCameraNumber_AndRegisteredOnly`
- `MoveButtons_DisabledWithoutPlcOrWhileRecipeRuns`
- `PersistedPoint_LocksKindAndRefusesRevertToCoordinate`
- `PointOptions_ExcludePointOne_AndFollowServoPointCount`
- `SaveError_SurfacesInStatusMessage_AndRowStaysEdited`
- `Save_Valid_PersistsRaisesEventAndSnapshotsRegistered`
- `WriteToPoint_WritesPlcThenSavesAsPoint_AndRequiresConfirmation`

### CapturePlanBoardTests.cs
- `BoardLabelsAndCounts_AreDerivedFromCellStates`
- `DiskReload_LatestSelectedReceiptOverridesDone_ExcludedRetryDoesNotEraseOthers`
- `Summary_HidesZeroPartsAndIncludesSkipped`

### CapturePlanCompilerTests.cs
- `CompileRemaining_ReensuresUnchangedPairAfterCompletedCondition`
- `CompileRemaining_RestoresPreconditionsAndRetainsPendingCaptureIds`
- `Compile_AndDeepCloneDoNotMutateOrShareSourceState`
- `Compile_CellEnsureForcesUnchangedPairIncludingRoomWithoutChangingDefaultDedup`
- `Compile_ConditionKeysDisambiguateIdenticalConditionsAndRejectDuplicates`
- `Compile_DeduplicatesPairWithinAndAcrossConditionsButEmitsChangedPair`
- `Compile_EveryCellEnsuresPairBeforeEachMoveIncludingRoom`
- `Compile_IdsSurviveRegenerationReorderingAndCellRemoval`
- `Compile_MissingCheckedPositionIdentifiesCell`
- `Compile_OrdersThreeCameraMatrixAndPreservesParameters`
- `Compile_RejectsReservedPointOneFromEveryPositionSource`
- `Compile_RolesResolvePhysicalUnitsRatherThanAssumingHotIsUnitZero`
- `Compile_RoomRequiresParkOrOverrideAndDoesNotImplicitlySetBlackBodies`
- `Compile_SetOnlyChamberIsPreservedButResumeAlwaysWaits`
- `RecipeCopies_LegacyAndPlanCopiesLeaveOriginalUnchanged`

### CapturePlanConverterTests.cs
- `Convert_BlackBodyBeforeEveryCell_PreservesEveryEnsure`
- `Convert_ManualThreeCameraTwoConditionFlow_PreservesEveryParameter`
- `Convert_OverridesAndFlattenedCopies_AreIndependentAndEquivalent`
- `Convert_UnsupportedFlow_RefusesFirstOffendingStepWithoutMutation`
- `Convert_ZeroSecondTargetSentinel_PreservesLegacyFallback`

### CapturePlanEditorViewModelTests.cs
- `Compilation_UsesCurrentDomainPatternAndCountsOnlyKnownWaitTime`
- `ConditionCommands_PreserveKeysAndCopyAllTemplatesWithoutSharing`
- `HumanLabels_HideKeysAndDescribeDefaultToleranceAndMissingPositions`
- `LocalOverrides_WinOverRepositoryAndSurviveMatrixEdits`
- `MatrixDetailEdits_KeepFilteredCollectionAndSelectedCellStable`
- `Matrix_RowColumnToggleAndCheckedFilterCoverAll64Cameras`
- `OptionalTemplates_PreserveZeroValuesAndUnexposedFieldsAcrossDisableEnable`
- `Park_AbsentUntilIncludedRejectsReservedPointAndCompilesExplicitCoordinate`
- `PatternSummary_DescribesActualRepeatsAndIgnoredRemainder`
- `PlanValidationChange_UsesOneErrorSurfaceForDraftSaveAndFlatten`
- `PositionLoadFailure_ReportsReasonAndDoesNotKeepStaleSnapshot`
- `Positions_RefreshWarningsAndImmediateCompilationUseNewSnapshot`
- `RealView_RendersKoreanChoiceLabelsDarkSelectionsAndMissingPositionIcon`
- `RoleSwap_UsesCorrespondingPhysicalUnitPositionAndKeepsTargets`
- `SetOnlyChamber_IsPreservedInNormalCompile`
- `TryCompile_IsImmediateAndSynchronizesInvalidEditsWithoutMutatingInput`

### CapturePlanPersistenceTests.cs
- `Csv_LegacyMissingOrEmptyPlan_RemainsNull`
- `Csv_QuotedPlanJsonAndCellKeys_RoundTrip`
- `Editor_CopyAndDomainConversions_KeepIndependentPlans`
- `FileRepository_PlanAndLegacyRecipes_RoundTrip`
- `InvalidDraft_StatusAndEmptySteps_RoundTripAllStorageFormats`
- `Json_PlanAndLegacyRecipes_RoundTrip`
- `LiteDbRepository_PlanAndLegacyRecipes_RoundTrip`

### CapturePlanRunTests.cs
- `CancelledRun_IsSavedBeforeExceptionReturns`
- `EngineFailure_RetryAndReopenedBoard_KeepLinkedDurableReceipts`
- `PartialRepeatedCapture_HistorySuccessDoesNotMakeCellDone`
- `Prepare_UsesCurrentPositionsAndHumanValidation_WithoutSaving`
- `RecoveryAndCopies_PreservePendingAndLatestRootOnly`

### DashboardCapturePlanTests.cs
- `BoardToggle_PreservesCameraModeAndFeedSlots`
- `BoardView_CompactRowsVirtualizeAndRevealCurrentCameraAndReason`
- `BoardView_ReplacingBoardKeepsSelectedConditionAndRenderedCurrentCell`
- `Commands_DisabledDuringPreparationAndRecovery_NoSecondRun`
- `Commands_DisabledWhileEngineAlreadyRunning_NoSecondRun`
- `Commands_DisabledWithoutChainOrApplicableCells_AndLegacyHasNoBoard`
- `Dispose_UnsubscribesBoardChangesAndIgnoresPendingReload`
- `Reload_AfterDashboardRecreation_UsesLatestPersistedChainWithoutPositions`
- `Reload_IgnoresSlowResultForPreviousSelection`
- `Resume_SkippedAndFailedCountAsRemaining`
- `RetryConfirmed_SavesFreshFullCompilationNotRetrySubset`
- `Retry_CancelledConfirmation_ShowsExactFreshCellCountAndHasNoPersistence`
- `Retry_WithoutDialog_FailsClosed`
- `Start_MissingCurrentPosition_RejectsStaleStoredStepsAndRaisesAlarm`

### RecipePlanEditorIntegrationTests.cs
- `Conversion_AddsAndSelectsIndependentCopyWithoutChangingOriginal`
- `Conversion_MissingBandOrRole_ProminentlyExplainsRefusalWithoutSaving`
- `CopyRecipe_UsesCurrentPlanAndRegeneratesIdentityOrPersistsEmptyDraft`
- `ExportSnapshot_UsesCurrentPlanWithoutPersisting`
- `Flatten_CompilesCurrentEditorAndCreatesExactIndependentLegacyCopy`
- `Flatten_InvalidPlan_DoesNotCreateOrPersistCopy`
- `HumanLabels_HideKeysAndDescribeDefaultToleranceAndMissingPositions`
- `InvalidSave_PersistsIsolatedDraftWithNoStaleStepsAndReloadableReason`
- `NewRecipeChoices_PreserveLegacyCommandAndCreateUncheckedPlanDraft`
- `PlanValidationChange_ClearsStaleDraftSaveOrFlattenError`
- `PlanValidationChange_PreservesUnrelatedOperationErrorAfterDraftError`
- `PlanValidationChange_UsesOneErrorSurfaceForDraftSaveAndFlatten`
- `RecipeTypeAndValidationFlags_NotifyWhenChanged`
- `SwitchingRecipes_RetainsUnsavedPlanEditsWithoutPersisting`
- `ValidSave_StoresCompiledStepsAndClearsPersistedDraftReason`
- `ValidationNotifications_UpdateCurrentListBadgeAndDetachOnSelection`

### RecipePlanEditorRenderingTests.cs
- `Dashboard_PersistedChainBoardRendersAndHasNoBindingWarnings`
- `Editor_CameraTargetSelectorDrawsSummaryText`
- `Editor_RealViewWorkflowRendersAndHasNoBindingWarnings`

## 반드시 보존한 offset / motion 케이스 (최종 TRX 모두 Passed)

- `HeatingCameraSystem.Tests.RecipeEngineTests.ExecuteRecipeAsync_ManualMotorMove_WaitsUntilServoPositionReachesTarget`
- `HeatingCameraSystem.Tests.RecipeEngineTests.DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(temperature: 20, humidity: 95, expectViolation: True)`
- `HeatingCameraSystem.Tests.ProductionCaptureSinkTests.WriteShot_AppliesShotNumberOffset(offset: 100, shotIndex: 99, expectedName: "BB80_199.raw")`
- `HeatingCameraSystem.Tests.RecipeCsvSerializerTests.Deserialize_MissingShotNumberOffsetColumn_IsZero_AndNewColumnImports`
- `HeatingCameraSystem.Tests.ProductionCaptureSinkTests.WriteShot_AppliesShotNumberOffset(offset: -5, shotIndex: 2, expectedName: "BB80_002.raw")`
- `HeatingCameraSystem.Tests.CaptureNamingRuleTests.CaptureCommandMessage_ShotNumberOffset_RoundTripsAndDefaultsToZero`
- `HeatingCameraSystem.Tests.ProductionCaptureSinkTests.WriteShot_AppliesShotNumberOffset(offset: 0, shotIndex: 0, expectedName: "BB80_000.raw")`
- `HeatingCameraSystem.Tests.CaptureNamingRuleTests.FileName_OffsetHundred_HundredShots_Is100To199`
- `HeatingCameraSystem.Tests.RecipeCopyTests.PointMotorStep_LoadedOrSaved_LocksRevertToManual`
- `HeatingCameraSystem.Tests.RecipeEngineTests.ExecuteRecipeAsync_ManualMotorMove_WhenServoMovedButPositionMismatches_WarnsAndContinues`
- `HeatingCameraSystem.Tests.RecipeEngineTests.DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(temperature: 9.89999962, humidity: 50, expectViolation: True)`
- `HeatingCameraSystem.Tests.RecipeEngineTests.DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(temperature: 80, humidity: 50, expectViolation: True)`
- `HeatingCameraSystem.Tests.ProductionCaptureSinkTests.WriteShot_AppliesShotNumberOffset(offset: 100, shotIndex: 0, expectedName: "BB80_100.raw")`
- `HeatingCameraSystem.Tests.CapturePlanCompilerTests.OffsetsContinueAcrossCapturesRoundsEntriesAndSnappedTemperatureCodes`
- `HeatingCameraSystem.Tests.RecipeEngineTests.DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(temperature: 55, humidity: 50, expectViolation: False)`
- `HeatingCameraSystem.Tests.RecipeEngineTests.DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(temperature: 10, humidity: 50, expectViolation: False)`
- `HeatingCameraSystem.Tests.RecipeEngineTests.ExecuteRecipeAsync_CameraCapture_RepeatedRounds_ContinueShotNumberOffset`

## 렌더

- `%TEMP%/HeatingCameraSystem-template-plan-renders/01-template-plan-editor.png`
- `02-template-plan-board.png`
- `03-legacy-template-slots.png`
- `04-legacy-empty-camera.png`
- 실제 underscore 포함 AgentId의 CheckBox.Content가 TextBlock이고 정확한 Text인지 STA에서 검증. 빈 선택 placeholder도 검증. binding warnings 0.
- 최종 상세 실행 증거: 같은 폴더 `template-review.trx`.
