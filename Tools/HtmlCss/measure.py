"""Compare recorded costs only when environment, inputs and measurement protocol match."""
import argparse
import json
import math
from pathlib import Path
import statistics


def statistics_for(values):
    if not isinstance(values, list) or not values or any(
            isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0
            for value in values):
        return None
    ordered = sorted(values)
    return {"count": len(values), "minimum": ordered[0], "median": statistics.median(values),
            "mean": statistics.mean(values), "p95": ordered[math.ceil(len(values) * .95) - 1], "maximum": ordered[-1]}


def compare_measurements(baseline, candidate):
    environment = candidate.get("environment", {}) if candidate else {}
    before = {case.get("name"): case for case in (baseline or {}).get("cases", [])}
    rows = []
    names = [case.get("name") for case in (candidate or {}).get("cases", [])]
    baseline_names = [case.get("name") for case in (baseline or {}).get("cases", [])]
    for case in (candidate or {}).get("cases", []):
        name = case.get("name")
        original = before.get(name)
        reasons = []
        if (candidate or {}).get("schemaVersion") != 1 or (baseline and baseline.get("schemaVersion") != 1):
            reasons.append("unsupported measurement schema")
        if not isinstance(name, str) or not name or names.count(name) != 1 or baseline_names.count(name) > 1:
            reasons.append("workload name missing or duplicated")
        after_stats = statistics_for(case.get("samples"))
        before_stats = statistics_for(original.get("samples")) if original else None
        if not baseline or not original:
            reasons.append("matching baseline sample missing")
        if not all(environment.get(key) for key in ["os", "cpu", "runtime"]) or environment.get("isolated") is not True:
            reasons.append("environment is incomplete or not isolated")
        if baseline and baseline.get("environment") != environment:
            reasons.append("environment differs")
        inputs = case.get("inputs")
        if not isinstance(inputs, dict) or not inputs or any(
                not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value)
                for value in (inputs or {}).values()):
            reasons.append("input hashes missing or invalid")
        if original and original.get("inputs") != inputs:
            reasons.append("inputs differ")
        if not case.get("method") or not case.get("unit"):
            reasons.append("measurement method or unit missing")
        if original and any(original.get(key) != case.get(key) for key in ["method", "unit"]):
            reasons.append("measurement protocol differs")
        if after_stats is None or before_stats is None:
            reasons.append("valid samples missing")
        rows.append({"name": name, "unit": case.get("unit"), "method": case.get("method"),
                     "comparable": not reasons, "reasons": reasons, "baseline": before_stats, "candidate": after_stats,
                     "observations": case.get("observations", {})})
    return {"schemaVersion": 1, "status": "reported" if rows and all(row["comparable"] for row in rows) else "insufficient",
            "environment": environment, "percentileMethod": "nearest rank", "cases": rows,
            "scope": "Absolute recorded observations. No performance budget or acceptance threshold is inferred."}


def read_measurements(path):
    path = Path(path)
    if path.is_dir():
        path /= "measurements.json"
    return json.loads(path.read_text()) if path.is_file() else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = compare_measurements(read_measurements(args.baseline), read_measurements(args.candidate))
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "comparison.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    lines = ["# URUI cost observations", "", "Status: " + report["status"], "", report["scope"], "",
             "| Workload | Unit | Baseline median | Candidate median | Comparison |", "|---|---|---:|---:|---|"]
    for row in report["cases"]:
        before = row["baseline"]["median"] if row["baseline"] else "unavailable"
        after = row["candidate"]["median"] if row["candidate"] else "unavailable"
        lines.append(f"| {row['name']} | {row['unit']} | {before} | {after} | {'comparable' if row['comparable'] else '; '.join(row['reasons'])} |")
    if not report["cases"]:
        lines += ["", "No candidate measurements were supplied."]
    (args.output / "comparison.md").write_text("\n".join(lines) + "\n")
    print(json.dumps({"status": report["status"], "cases": len(report["cases"]), "output": str(args.output)}))


if __name__ == "__main__":
    main()
