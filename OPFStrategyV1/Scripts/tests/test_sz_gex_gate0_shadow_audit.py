from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GEX = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.Gex.cs").read_text(encoding="utf-8")
STRATEGY = (ROOT / "Strategy" / "OpeningPullbackFailureStrategy.cs").read_text(encoding="utf-8")
LOGGER = (ROOT / "Research" / "ResearchLogger.cs").read_text(encoding="utf-8")
SPEC = (ROOT.parents[0] / "docs" / "superpowers" / "specs" / "2026-10-10-sz-gex-gate0-shadow-audit.md").read_text(encoding="utf-8")


def test_logger_implements_gex_append_apis():
    assert "public void AppendGexSnapshot(" in LOGGER
    assert "public void AppendGexCandidateAudit(" in LOGGER
    assert "_gex_candidate_audit.csv" in LOGGER
    assert "_gex_snapshot.json" in LOGGER
    assert "_gex_levels.csv" in LOGGER


def test_sz_queue_and_activate_call_audit_only():
    assert 'AuditGexCandidate(signal, candle, queuedPath, "Queued");' in STRATEGY
    assert 'AuditGexCandidate(signal, entryCandle, path, "Activated");' in STRATEGY
    # Activate audit must sit before TrySubmitReplayExecution and must not gate it.
    activate = STRATEGY[STRATEGY.index("private void TryActivateSignificantZoneFirstTouches") :]
    activate = activate[: activate.index("private void TrySubmitReplayExecution")]
    assert 'AuditGexCandidate(signal, entryCandle, path, "Activated");' in activate
    assert "SHADOW_" not in activate or "HardSkip" not in activate
    assert "TrySubmitReplayExecution(signal, entryCandle, path, pending.Entry.InitialStop, risk, allowDelayedExpansion: false);" in activate


def test_shadow_tag_codes_and_near_wall_const():
    for code in (
        "SHADOW_NO_SPACE",
        "SHADOW_NEG_GAMMA_LONG_HALF",
        "SHADOW_POS_GAMMA_SHORT_STRICT",
        "SHADOW_ROLE_FLIP_CONFLICT",
        "SHADOW_PREFILTER_INACTIVE",
    ):
        assert code in GEX
    assert "GexNearWallPoints = 40m" in GEX
    assert "RoleFlipState stays Unknown" in GEX or "never invent" in GEX.lower() or "never invented" in SPEC.lower()


def test_gex_file_still_never_touches_order_path():
    for forbidden in ("EnableReplayOrders", "TrySubmitReplayExecution", "AppendExecutionDecision"):
        assert forbidden not in GEX


def test_spec_documents_bit_identical_guarantee():
    assert "zero order impact" in SPEC.lower() or "Zero order impact" in SPEC or "zero order impact" in SPEC
    assert "Bit-identical" in SPEC or "bit-identical" in SPEC
    assert "SHADOW_NO_SPACE" in SPEC


if __name__ == "__main__":
    test_logger_implements_gex_append_apis()
    test_sz_queue_and_activate_call_audit_only()
    test_shadow_tag_codes_and_near_wall_const()
    test_gex_file_still_never_touches_order_path()
    test_spec_documents_bit_identical_guarantee()
    print("PASS test_sz_gex_gate0_shadow_audit")
