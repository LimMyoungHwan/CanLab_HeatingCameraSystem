# WPF Visual/CJK Precision Gate Review

- recommendation: REJECT
- reviewVerdict: REVISE
- originalIntent: Read-only inspection of `RecipeEditorView.xaml` and `HistoryView.xaml` for clipping, column alignment, label wrapping, and CJK/font problems, using the supplied image only as a prior user capture.
- desiredOutcome: Both views render their controls and Korean/English labels without clipping or unintended wrapping, and History table headers remain aligned with body columns.

## User outcome review

The XAML is well-formed and the prior 407x140 capture shows Korean glyphs rendering legibly without tofu or obvious baseline clipping in the visible blackbody card. However, the current XAML contains deterministic horizontal overflow paths and a header/body width mismatch when History scrollbars appear. A fresh post-change runtime capture was not supplied, so current pixel-level font fallback, DPI scaling, and actual clipping cannot be certified.

## Blockers

1. `violatedCriterion: VIS-CLIP-01` — Recipe editor horizontal content must fit its declared columns without clipping. `evidencePointer: HeatingCameraSystem.Master/Views/RecipeEditorView.xaml:169, HeatingCameraSystem.Master/Views/RecipeEditorView.xaml:339, HeatingCameraSystem.Master/Views/RecipeEditorView.xaml:440` — the page reserves 250px and 420px side columns; the center step area uses fixed-width horizontal stacks without horizontal scrolling, while the legacy row alone requires at least 508px before card padding/drag/delete controls. The 420px right panel has roughly 388px after margins, but its camera-control row contains eight non-wrapping buttons.
2. `violatedCriterion: VIS-ALIGN-02` — History headers and rows must retain column alignment while scrolling. `evidencePointer: HeatingCameraSystem.Master/Views/HistoryView.xaml:277, HeatingCameraSystem.Master/Views/HistoryView.xaml:299, HeatingCameraSystem.Master/Views/HistoryView.xaml:315` — header and body duplicate the same star ratios, but only the body is inside a `ScrollViewer` with an auto vertical scrollbar. When present, the scrollbar reduces body width while the header retains full width, shifting every column boundary.
3. `violatedCriterion: VIS-WRAP-03` — Filter and localized labels must not be clipped at supported window widths. `evidencePointer: HeatingCameraSystem.Master/Views/HistoryView.xaml:208, HeatingCameraSystem.Master/Views/HistoryView.xaml:209, HeatingCameraSystem.Master/Views/HistoryView.xaml:224, HeatingCameraSystem.Master/Views/HistoryView.xaml:255` — all filters and actions are in one non-wrapping horizontal `StackPanel`; fixed widths plus margins exceed a 1024px design surface, with no horizontal scrollbar or alternate row.

## Notes

- CJK/font: no explicit `FontFamily` is set in either view. WPF fallback should cover Hangul, and the prior capture confirms readable Korean on that machine, but symbol glyphs (`☰`, `✕`, `↗`, `«`, `‹`, `›`, `»`) and Korean metrics remain environment/font-fallback dependent. This is a risk note, not independently proven current failure.
- Label wrapping: table headers default to no wrapping. The Korean strings inspected are short; English `HUMIDITY (%RH)` and localized labels can still clip at narrow widths because no trimming/tooltips or minimum widths are specified.
- Slop/overfit pass: no tests were added in these two XAML diffs. The duplicated History header/body column definitions create maintenance burden and enable alignment drift, but no unrelated abstraction or test overfit was found in scope.
- Programming pass: no `.py/.rs/.ts/.tsx/.go` files are in scope. XAML changes are direct; the primary maintenance concern is duplicated column geometry and fixed-width horizontal layout.

## Checked artifacts

- `HeatingCameraSystem.Master/Views/RecipeEditorView.xaml`
- `HeatingCameraSystem.Master/Views/HistoryView.xaml`
- `HeatingCameraSystem.Master/Resources/Lang/ko.txt`
- `HeatingCameraSystem.Master/Resources/Lang/en.txt`
- `C:/Users/p2062/AppData/Local/Temp/codex-clipboard-5vPjNm.png` (407x140, historical capture)
- Git diff/status for both requested XAML files
- XML reader well-formedness check for both requested XAML files

## Exact evidence gaps

- No fresh post-change runtime screenshot of either full view.
- No screenshots at the app's minimum supported window size, 100%/125%/150% DPI, or both `ko` and `en` locales.
- No populated History capture/chamber/alarm table showing the vertical scrollbar state.
- No runtime font-family/fallback inspection.
- `omo ulw-loop status --json` could not be read because the installed `omo.cmd` returned a command-syntax error, so the required fallback report path was used.
