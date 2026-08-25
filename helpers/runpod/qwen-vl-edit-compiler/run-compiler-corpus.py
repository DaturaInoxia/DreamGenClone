#!/usr/bin/env python3

import argparse
import base64
import hashlib
import json
import struct
import subprocess
import threading
import time
import urllib.request
from pathlib import Path
from typing import Any

SYSTEM_MESSAGE = """You are a vision-grounded compiler for Qwen Image Edit. Inspect the supplied source image and compile the user's request into one concise edit instruction.

Observe only visible facts needed to satisfy the request. Identify targets with visible locators such as clothing, position, laterality, or nearby objects. Do not invent names, relationships, hidden anatomy, unseen details, or story facts. Preserve visible identity, wardrobe unless requested, unaffected people, objects, composition, lighting, and style.

When the target is ambiguous, contradictory, impossible, unsupported by the source, or unsafe under the configured policy, return clarification_required or invalid. Never guess a ready edit. Ready instructions must be direct, feasible, and describe only the requested change plus necessary visible disambiguation and preservation.

Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text."""

ROOT_FIELDS = {
    "schemaVersion",
    "status",
    "sourceSummary",
    "targets",
    "requestedChanges",
    "preserve",
    "clarificationQuestion",
    "invalidReason",
    "compiledPrompt",
}

RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": False,
    "required": sorted(ROOT_FIELDS),
    "properties": {
        "schemaVersion": {"const": "scene-image-edit-compiler-v1"},
        "status": {"enum": ["ready", "clarification_required", "invalid"]},
        "sourceSummary": {"type": "string", "minLength": 1},
        "targets": {
            "type": "array",
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": ["key", "visibleLocator", "region"],
                "properties": {
                    "key": {"type": "string", "minLength": 1},
                    "visibleLocator": {"type": "string", "minLength": 1},
                    "region": {
                        "anyOf": [
                            {"type": "null"},
                            {
                                "type": "object",
                                "additionalProperties": False,
                                "required": ["x", "y", "width", "height"],
                                "properties": {
                                    "x": {"type": "number", "minimum": 0, "maximum": 1},
                                    "y": {"type": "number", "minimum": 0, "maximum": 1},
                                    "width": {"type": "number", "exclusiveMinimum": 0, "maximum": 1},
                                    "height": {"type": "number", "exclusiveMinimum": 0, "maximum": 1},
                                },
                            },
                        ]
                    },
                },
            },
        },
        "requestedChanges": {"type": "array", "items": {"type": "string", "minLength": 1}},
        "preserve": {"type": "array", "items": {"type": "string", "minLength": 1}},
        "clarificationQuestion": {"type": ["string", "null"]},
        "invalidReason": {"type": ["string", "null"]},
        "compiledPrompt": {"type": ["string", "null"]},
    },
}


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the frozen Phase 1B compiler corpus exactly once.")
    parser.add_argument("port", type=int)
    parser.add_argument("corpus", type=Path)
    parser.add_argument("output", type=Path)
    return parser.parse_args()


def gpu_used_mib() -> int | None:
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            check=True,
            capture_output=True,
            text=True,
        )
        return max(int(value.strip()) for value in result.stdout.splitlines() if value.strip())
    except (FileNotFoundError, subprocess.CalledProcessError, ValueError):
        return None


def sample_gpu(stop: threading.Event, samples: list[int]) -> None:
    while not stop.wait(0.25):
        value = gpu_used_mib()
        if value is not None:
            samples.append(value)


def validate_terminal_result(result: dict[str, Any], expected_schema: str) -> list[str]:
    failures: list[str] = []
    if set(result) != ROOT_FIELDS:
        failures.append("root fields do not exactly match the application schema")
        return failures
    if result["schemaVersion"] != expected_schema:
        failures.append("schema version mismatch")
    status = result["status"]
    if status == "ready":
        if not result["targets"] or not result["requestedChanges"] or not result["preserve"]:
            failures.append("ready result is missing targets, changes, or preservation")
        if not isinstance(result["compiledPrompt"], str) or not result["compiledPrompt"].strip():
            failures.append("ready result is missing compiledPrompt")
        if result["clarificationQuestion"] is not None or result["invalidReason"] is not None:
            failures.append("ready result contains another terminal state's fields")
    elif status == "clarification_required":
        if not isinstance(result["clarificationQuestion"], str) or not result["clarificationQuestion"].strip():
            failures.append("clarification result is missing its question")
        if result["compiledPrompt"] is not None or result["invalidReason"] is not None:
            failures.append("clarification result contains executable or invalid fields")
    elif status == "invalid":
        if not isinstance(result["invalidReason"], str) or not result["invalidReason"].strip():
            failures.append("invalid result is missing its reason")
        if result["compiledPrompt"] is not None or result["clarificationQuestion"] is not None:
            failures.append("invalid result contains executable or clarification fields")
    else:
        failures.append("unknown terminal status")
    return failures


def main() -> int:
    arguments = parse_arguments()
    corpus_path = arguments.corpus.resolve()
    corpus = json.loads(corpus_path.read_text(encoding="utf-8"))
    source_path = (corpus_path.parent / corpus["source"]["path"]).resolve()
    source_bytes = source_path.read_bytes()
    source_sha256 = hashlib.sha256(source_bytes).hexdigest().upper()
    if len(source_bytes) != corpus["source"]["bytes"] or source_sha256 != corpus["source"]["sha256"]:
        raise SystemExit("Frozen source byte count or SHA-256 does not match the corpus manifest.")
    if source_bytes[:8] != b"\x89PNG\r\n\x1a\n" or len(source_bytes) < 24:
        raise SystemExit("Frozen corpus source is not a valid PNG header.")
    width, height = struct.unpack(">II", source_bytes[16:24])
    if width <= 0 or height <= 0:
        raise SystemExit("Frozen corpus source has invalid dimensions.")

    arguments.output.mkdir(parents=True, exist_ok=False)
    data_url = f"data:{corpus['source']['mediaType']};base64,{base64.b64encode(source_bytes).decode('ascii')}"
    case_results: list[dict[str, Any]] = []
    for case in corpus["cases"]:
        payload = {
            "model": corpus["modelIdentifier"],
            "messages": [
                {"role": "system", "content": SYSTEM_MESSAGE},
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": f"Raw edit intent:\n{case['intent']}"},
                        {"type": "image_url", "image_url": {"url": data_url}},
                    ],
                },
            ],
            "temperature": corpus["settings"]["temperature"],
            "top_p": corpus["settings"]["topP"],
            "max_tokens": corpus["settings"]["maxTokens"],
            "response_format": {
                "type": "json_schema",
                "json_schema": {"name": "scene_image_edit_compilation", "strict": True, "schema": RESPONSE_SCHEMA},
            },
        }
        request = urllib.request.Request(
            f"http://127.0.0.1:{arguments.port}/v1/chat/completions",
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        samples: list[int] = []
        stop = threading.Event()
        sampler = threading.Thread(target=sample_gpu, args=(stop, samples), daemon=True)
        sampler.start()
        started = time.monotonic()
        try:
            with urllib.request.urlopen(request, timeout=corpus["settings"]["requestTimeoutSeconds"]) as response:
                raw_response = response.read()
        finally:
            elapsed = time.monotonic() - started
            stop.set()
            sampler.join()
        raw_path = arguments.output / f"{case['id']}.response.json"
        raw_path.write_bytes(raw_response)
        response_document = json.loads(raw_response)
        content = response_document["choices"][0]["message"]["content"]
        result = json.loads(content)
        failures = validate_terminal_result(result, corpus["compilerSchemaVersion"])
        if response_document.get("model") != corpus["modelIdentifier"]:
            failures.append("served model identity mismatch")
        if result.get("status") != case["expectedStatus"]:
            failures.append("terminal status mismatch")
        locator_text = " ".join(target.get("visibleLocator", "") for target in result.get("targets", [])).lower()
        for term in case["requiredTargetTerms"]:
            if term.lower() not in locator_text:
                failures.append(f"missing required target term: {term}")
        grounded_text = json.dumps(
            {"sourceSummary": result.get("sourceSummary"), "targets": result.get("targets"), "compiledPrompt": result.get("compiledPrompt")},
            ensure_ascii=True,
        ).lower()
        for term in case["forbiddenInventions"]:
            if term.lower() in grounded_text:
                failures.append(f"forbidden invention term: {term}")
        if elapsed > corpus["acceptance"]["maximumRequestSeconds"]:
            failures.append("request exceeded latency gate")
        case_results.append(
            {
                "id": case["id"],
                "expectedStatus": case["expectedStatus"],
                "actualStatus": result.get("status"),
                "elapsedSeconds": round(elapsed, 3),
                "peakGpuMemoryUsedMiB": max(samples) if samples else None,
                "rawResponseSha256": hashlib.sha256(raw_response).hexdigest().upper(),
                "schemaValid": not any("schema" in failure or "fields" in failure for failure in failures),
                "passed": not failures,
                "failures": failures,
                "parsedResult": result,
            }
        )

    summary = {
        "corpusVersion": corpus["corpusVersion"],
        "compilerSchemaVersion": corpus["compilerSchemaVersion"],
        "systemPromptVersion": corpus["systemPromptVersion"],
        "modelIdentifier": corpus["modelIdentifier"],
        "source": {"path": str(source_path), "bytes": len(source_bytes), "width": width, "height": height, "sha256": source_sha256},
        "settings": corpus["settings"],
        "cases": case_results,
        "passed": all(case["passed"] for case in case_results),
    }
    (arguments.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": summary["passed"], "cases": len(case_results), "output": str(arguments.output)}, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())