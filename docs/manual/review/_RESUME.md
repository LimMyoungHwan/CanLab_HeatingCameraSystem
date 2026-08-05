# 매뉴얼 AI 검토 — 재개 노트 (다음 세션)

## 결정: 옵션 2
kimi 쿼터 리셋되는 다음 청구주기에 **미커버 8챕터 kimi 리뷰 → 17챕터 일괄 재종합 → 재반영 → docx 재생성** 을 한 번에.

## 현재 상태 (git `08221a1`)
- 22챕터: 3-AI(codex·claude·agy) 리뷰 + 종합 + 본문 반영 완료. docx 2종 생성됨(`45d1a04`).
- kimi 커버리지 **14/22**: 사용자 U01~U11 전부 + 운영자 O01·O02·O06.
- **kimi 미커버 8챕터** (다음에 할 것):
  - operator: 03-설치, 04-설정-파일-레퍼런스, 05-PLC-설정-화면, 07-Agent-Manager(자동-발견·승인), 08-시뮬레이션-모드, 09-LiteDB(data.db)-관리, 10-트러블슈팅, 11-부록-—-런타임-파일

## 다음 세션 절차
1. **kimi 확인**: `kimi -p "Reply READY" 2>&1` → 403 아니면 진행.
2. **8챕터 kimi 리뷰** (프롬프트는 이미 `_prompts/operator/`에 있음):
   - `[Console]::OutputEncoding=[Text.Encoding]::UTF8` 먼저. `kimi -p "Read '$prompt'... output ONLY Korean review" --add-dir <review> 2>$null | Out-File $out -Encoding UTF8`
   - 큰 챕터(03·04)는 동시성 낮춰 격리 실행. 0바이트면 재시도.
3. **재종합**: 4개 리뷰(codex·claude·agy·kimi)로 codex-moderator 재실행 → `{cid}_종합_R1.md` 덮어쓰기. (인라인 codex, 스크립트 리터럴 한글은 PS5.1이 깨뜨림 주의)
4. **재반영**: codex-applier로 `종합`의 "합의·다수 수정"만 챕터에 반영. **큰 챕터는 반드시 격리+최대TO(600s)** — 병렬이면 타임아웃.
5. **docx 재생성**: `python md_to_docx.py` (temp) → 사용자/운영자 docx 갱신.
6. 커밋+푸시.

## 스크립트 위치 (temp, 외부)
`C:\Users\p2062\AppData\Local\Temp\opencode\` — `make_prompt.py`, `md_to_docx.py`, `gen_manuals.py`(docx 빌더/Manual 클래스), `synth.ps1`(한글 리터럴 mojibake 있음 → 인라인 권장).

## 주요 교훈
- PowerShell 5.1: 네이티브 stdout 캡처 전 `[Console]::OutputEncoding=UTF8` 필수(안 하면 한글 mojibake). .ps1 파일 안 한글 리터럴도 ANSI로 깨짐 → 인라인 Bash 사용.
- kimi: `-p` 단독(+`--add-dir`), `--quiet`/`--auto`/`--yolo` 결합 불가. 쿼터 작음.
- codex/agy는 자체 파일쓰기(-o/도구)라 인코딩 안전. Start-Job은 프로필 PATH 없어 CLI 못 찾음 → 병렬 Bash 사용.
- 큰 챕터(설정·설치·수동조작·대시보드) codex 전체재작성은 격리+600s 필요.
- **보류(설비 안전)**: 비상정지·원점복귀 복구절차 등은 각 `_종합_R1.md` "보류" 섹션 참고해 운영자가 실제 규격으로 채워야 함.
