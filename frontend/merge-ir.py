#!/usr/bin/env python3
"""Merge selected records from a supplement IR into a base IR.

Used when a module run emits dependency declarations (for example enums that
are declared outside a module filter but referenced by its signatures) that a
previously assembled full IR does not contain yet.

Only records of the requested record type/kind are copied; records already
present by id are skipped. The output manifest's shard hash, record count, and
counts are recomputed.
"""

import argparse
import hashlib
import json
import os
import shutil


def read_records(path):
    with open(path, encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def record_key(record):
    return record.get("recordType", "") + "|" + record.get("id", "")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--supplement", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--record-type", default="declaration")
    parser.add_argument("--kind", default="")
    args = parser.parse_args()

    if os.path.exists(args.output):
        shutil.rmtree(args.output)
    shutil.copytree(args.base, args.output)

    base_records = read_records(os.path.join(args.base, "shard.jsonl"))
    seen = {record_key(record) for record in base_records}
    added = []
    for record in read_records(os.path.join(args.supplement, "shard.jsonl")):
        if record.get("recordType") != args.record_type:
            continue
        if args.kind and record.get("kind") != args.kind:
            continue
        key = record_key(record)
        if key not in seen:
            seen.add(key)
            added.append(record)

    records = base_records + added
    shard_text = "".join(
        json.dumps(record, ensure_ascii=False, separators=(",", ":"), sort_keys=True)
        + "\n"
        for record in records
    )
    with open(
        os.path.join(args.output, "shard.jsonl"), "w", encoding="utf-8", newline="\n"
    ) as stream:
        stream.write(shard_text)

    manifest_path = os.path.join(args.base, "ir-manifest.json")
    with open(manifest_path, encoding="utf-8") as stream:
        manifest = json.load(stream)

    counts = {"declaration": 0, "variant": 0, "member": 0}
    for record in records:
        record_type = record.get("recordType")
        if record_type in counts:
            counts[record_type] += 1

    shard = manifest["shards"][0]
    shard["records"] = len(records)
    digest = "sha256:" + hashlib.sha256(shard_text.encode("utf-8")).hexdigest()
    shard["sha256"] = digest
    shard["sha256Compressed"] = digest
    manifest["counts"]["declarations"] = counts["declaration"]
    manifest["counts"]["variants"] = counts["variant"]
    manifest["counts"]["members"] = counts["member"]

    with open(
        os.path.join(args.output, "ir-manifest.json"),
        "w",
        encoding="utf-8",
        newline="\n",
    ) as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
        stream.write("\n")

    print(f"merged {len(added)} records; total {len(records)}")


if __name__ == "__main__":
    main()
