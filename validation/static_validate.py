#!/usr/bin/env python3
"""Static validation for the generated NT8 strategy sources and sample conformance."""
from __future__ import annotations
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = (ROOT / "Sample EAs" / "OvernightBiasRangeBreakout_NT8" / "OvernightBiasRangeBreakoutNT8.cs").read_text(encoding="utf-8")


def brace_end(src: str, open_pos: int) -> int:
    depth = 0
    i = open_pos
    state = "code"
    while i < len(src):
        c = src[i]
        n = src[i + 1] if i + 1 < len(src) else ""
        if state == "code":
            if c == "/" and n == "/": state = "line"; i += 2; continue
            if c == "/" and n == "*": state = "block"; i += 2; continue
            if c == "@" and n == '"': state = "verbatim"; i += 2; continue
            if c == '"': state = "string"; i += 1; continue
            if c == "'": state = "char"; i += 1; continue
            if c == "{": depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0: return i + 1
        elif state == "line":
            if c == "\n": state = "code"
        elif state == "block":
            if c == "*" and n == "/": state = "code"; i += 2; continue
        elif state == "string":
            if c == "\\": i += 2; continue
            if c == '"': state = "code"
        elif state == "verbatim":
            if c == '"' and n == '"': i += 2; continue
            if c == '"': state = "code"
        elif state == "char":
            if c == "\\": i += 2; continue
            if c == "'": state = "code"
        i += 1
    raise ValueError("Unclosed C# brace or string")


def extract_class(src: str, name: str) -> str:
    match = re.search(r"\bclass\s+" + re.escape(name) + r"\b", src)
    if not match:
        raise AssertionError(f"Missing class {name}")
    open_pos = src.find("{", match.end())
    return src[match.start():brace_end(src, open_pos)]


def lexical_check(src: str) -> dict:
    depth = 0
    i = 0
    line = 1
    state = "code"
    while i < len(src):
        c = src[i]
        n = src[i + 1] if i + 1 < len(src) else ""
        if c == "\n": line += 1
        if state == "code":
            if c == "/" and n == "/": state = "line"; i += 2; continue
            if c == "/" and n == "*": state = "block"; i += 2; continue
            if c == "@" and n == '"': state = "verbatim"; i += 2; continue
            if c == '"': state = "string"; i += 1; continue
            if c == "'": state = "char"; i += 1; continue
            if c == "{": depth += 1
            elif c == "}":
                depth -= 1
                if depth < 0: raise AssertionError(f"Unmatched }} at line {line}")
        elif state == "line":
            if c == "\n": state = "code"
        elif state == "block":
            if c == "*" and n == "/": state = "code"; i += 2; continue
        elif state == "string":
            if c == "\\": i += 2; continue
            if c == '"': state = "code"
        elif state == "verbatim":
            if c == '"' and n == '"': i += 2; continue
            if c == '"': state = "code"
        elif state == "char":
            if c == "\\": i += 2; continue
            if c == "'": state = "code"
        i += 1
    if state not in ("code", "line") or depth != 0:
        raise AssertionError(f"Unbalanced lexical state={state}, brace_depth={depth}")
    return {"brace_balance": "PASS", "ending_state": state}


def duplicate_method_signatures(src: str) -> dict:
    pat = re.compile(
        r"^\s*(?:(?:public|private|protected|internal)\s+)?"
        r"(?:(?:static|override|virtual|sealed|async|new|extern|unsafe|partial)\s+)*"
        r"(?:[\w:<>,\[\]?\.]+\s+)+(?P<name>\w+)\s*\((?P<args>[^;{}]*)\)\s*(?:where[^{}]+)?\s*\{",
        re.M,
    )
    seen: dict[tuple[str, str], list[int]] = {}
    for m in pat.finditer(src):
        key = (m.group("name"), re.sub(r"\s+", " ", m.group("args").strip()))
        seen.setdefault(key, []).append(src.count("\n", 0, m.start()) + 1)
    dup = {f"{name}({args})": lines for (name, args), lines in seen.items() if len(lines) > 1}
    if dup: raise AssertionError(f"Duplicate method signatures: {dup}")
    return {"method_count": sum(len(v) for v in seen.values()), "duplicate_signatures": 0}


def require(src: str, needle: str, label: str) -> None:
    if needle not in src:
        raise AssertionError(f"{label}: missing {needle!r}")


def check_strategy(filename: str, strategy_class: str, model: str) -> dict:
    path = ROOT / filename
    src = path.read_text(encoding="utf-8")
    result = {
        "sha256": hashlib.sha256(src.encode()).hexdigest(),
        "lines": len(src.splitlines()),
        **lexical_check(src),
        **duplicate_method_signatures(src),
    }
    require(src, f"public class {strategy_class} : Strategy", "strategy class")
    for val,label in [
        ("IsUnmanaged = true;", "unmanaged orders"),
        ("StartBehavior = StartBehavior.ImmediatelySubmit;", "ImmediatelySubmit"),
        ("RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;", "manual error/recovery control"),
        ("ConnectionLossHandling = ConnectionLossHandling.KeepRunning;", "connection recovery"),
        ("private void PrepareRecovery()", "recovery identity"),
        ("private void StartRecovery()", "journal restore"),
        ("private void SaveRecovery(", "durable journal"),
        ("private void ReconcileRecovery()", "broker reconciliation"),
        ("private void DriveClose()", "durable close driver"),
        ("private bool RepairOwnedProtection()", "protection repair"),
        ("ProductExposure.Reserve(", "shared instrument ownership"),
        ("protected override void OnExecutionUpdate(", "execution callback"),
        ("protected override void OnOrderUpdate(", "order callback"),
        ("TimeZoneInfo.ConvertTime", "platform timezone conversion"),
        ("ChartExecutionStyle.MarkersOnly", "native historical execution markers"),
        ("ShowTradeLines = true", "native historical trade lines"),
        ("OvernightExecutionVisual.DrawExecutionTool", "sample execution boxes"),
        ("OvernightExecutionVisual.DrawBoundedLine", "sample bounded lines"),
        ("protected override void OnRender(", "sample dashboard render"),
        ("HookDashboardMouse(attach: true)", "sample panel interactions"),
        ("private readonly ModelDashboard dashboard", "sample panel"),
        ("public string InstanceId { get; set; }", "unique instance ID"),
    ]:
        require(src,val,label)
    if "Closes[executionSeries][0]" in src:
        raise AssertionError("Historical sizing uses last-loaded Closes[0]; must use completed signal close")
    if re.search(r"UTC\s*[-+]\s*[45]", src):
        raise AssertionError("Fixed UTC offset found in C#")

    # Ensure the copied shared panel and shared exposure manager stay sample-identical.
    sample_hashes = {}
    for cls in ("ModelDashState", "ModelDashboard", "ProductExposure"):
        a = extract_class(SAMPLE, cls).replace("OvernightBiasRangeBreakoutNT8", "STRATEGY_CLASS")
        b = extract_class(src, cls).replace("SigmaBreakoutNT8", "STRATEGY_CLASS").replace("FlashTriggerOrbNT8", "STRATEGY_CLASS")
        if a != b:
            raise AssertionError(f"{cls} is not identical to the supplied sample")
        sample_hashes[cls] = hashlib.sha256(a.encode()).hexdigest()
    a = extract_class(SAMPLE, "OvernightExecutionVisual").replace("OvernightBiasRangeBreakoutNT8", "STRATEGY_CLASS")
    b = extract_class(src, "OvernightExecutionVisual").replace("SigmaBreakoutNT8", "STRATEGY_CLASS").replace("FlashTriggerOrbNT8", "STRATEGY_CLASS")
    if a != b:
        raise AssertionError("OvernightExecutionVisual differs beyond strategy type rename")
    sample_hashes["OvernightExecutionVisual"] = hashlib.sha256(a.encode()).hexdigest()

    if model == "sigma":
        for val,label in [
            ("NSigmaDays = 3;", "N3 default"),
            ("BandMultiplier = 1.7;", "1.7 band"),
            ("UseVwapReference = false;", "RTH open reference"),
            ("SlPct = 0.0;", "no-stop baseline"),
            ("FixedLot = 1;", "fixed-lot default"),
            ("pendingSigmaFlipSide", "durable reversal state"),
            ("IndependentNT8.SigmaN3.v3", "v3 retry recovery journal"),
            ("IndependentNT8.v2", "backwards-compatible v2 recovery reader"),
            ("entryCount == 1 && retryArmed && side == entryDirection", "same-side retry gate"),
            ("firstStopDistance * RetryStopFraction", "half original stop distance"),
            ("stopExitFilled", "protective-stop exit classification"),
            ("!targetFilled", "target fill prevents retry"),
            ("SubmitSigmaEntry(flipSide, flipClose, flipEnd)", "reverse entry after close"),
            ("endEt.TimeOfDay.TotalMinutes < 944", "15:44 ET cutoff"),
            ("SkipSetupsAboveRiskBudget && SlPct > 0", "variable-only risk cap"),
        ]: require(src,val,label)
    else:
        for val,label in [
            ("RewardRisk = 3.0;", "3R default"),
            ("TrailActivationPoints = 70.0;", "+70 trail activation"),
            ("TrailOffsetPoints = 25.0;", "25-point trail offset"),
            ("InitialSizingEquity = 10000.0;", "Analyzer default equity"),
            ("bucketStartUtc.TimeOfDay == new TimeSpan(14, 30, 0)", "14:30 UTC target"),
            ("candle.Close > flashTargetHigh", "strict M15 close break"),
            ("flashPeakHigh = Math.Max(flashPeakHigh, bar.High)", "completed M1 high trail"),
            ("internal static int Cutoff(DateTime d)", "daily cutoff"),
            ("return 1005", "16:45 ET cutoff"),
            ("SkipSetupsAboveRiskBudget && qty * perContract", "optional variable risk cap"),
        ]: require(src,val,label)
    result["copied_sample_components"] = sample_hashes
    result["model_contract"] = "PASS"
    result["native_compiled"] = False
    result["native_compiler_note"] = "No NinjaTrader/CLR compiler in this handoff environment"
    return result



def check_mql_retry() -> dict:
    core_path = ROOT / "7. SigmaBreakout_NT" / "StrategyEngine.mqh"
    runtime_path = ROOT / "7. SigmaBreakout_NT" / "ExecutionRuntime.mqh"
    core = core_path.read_text(encoding="utf-8")
    runtime = runtime_path.read_text(encoding="utf-8")
    require(core, "retryEntry=entries==1 && retryArmed", "MQL same-side retry gate")
    require(core, "firstStopDistance*SigmaRetryStopFraction", "MQL half original stop distance")
    require(core, "SlPct>0.0 && firstStopDistance>0.0", "MQL retry requires a real original stop")
    require(core, "signalEnd>retryEligibleAfter", "MQL retry must use a later completed signal")
    require(core, "!targetFilled", "MQL target fill prevents retry")
    require(runtime, 'SigmaStatePut(k+"firststopdist",firstStopDistance);', "MQL retry distance persistence")
    require(runtime, 'SigmaStatePut(k+"targetfilled",targetFilled ? 1 : 0);', "MQL target state persistence")
    require(runtime, 'firstStopDistance=GlobalVariableCheck(k+"firststopdist")', "MQL retry distance restoration")
    return {
        "model_contract": "PASS",
        "native_compiled": False,
        "native_compiler_note": "MetaEditor compile and Strategy Tester run are manual steps",
        "sources": {
            "strategy_engine": {
                "sha256": hashlib.sha256(core.encode()).hexdigest(),
                "lines": len(core.splitlines()),
            },
            "execution_runtime": {
                "sha256": hashlib.sha256(runtime.encode()).hexdigest(),
                "lines": len(runtime.splitlines()),
            },
        },
    }


def main() -> None:
    results = {
        "package": "Index Book Sigma N3/1.7 + Flash Trigger ORB port handoff",
        "sample_file_sha256": hashlib.sha256(SAMPLE.encode()).hexdigest(),
        "strategies": {
            "sigma": check_strategy("7. SigmaBreakout_NT/SigmaBreakoutNT8.cs", "SigmaBreakoutNT8", "sigma"),
            "flash": check_strategy("8. FlashTriggerORB_NT/FlashTriggerOrbNT8.cs", "FlashTriggerOrbNT8", "flash"),
        },
        "mql5_retry_contract": check_mql_retry(),
        "limitations": [
            "Static source validation does not establish NT8 or MetaTrader compilation.",
            "Strategy Analyzer, chart visuals, live-sim recovery lifecycle and broker integration remain manual test requirements.",
            "Committed compiler logs and binaries are evidence artifacts only; they do not prove the exact current source was compiled."
        ]
    }
    out = ROOT / "validation" / "static_results.json"
    out.write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(json.dumps(results, indent=2))

if __name__ == "__main__":
    main()
