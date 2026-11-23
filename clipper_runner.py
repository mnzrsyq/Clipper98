"""Trusted CLI bridge for Clipper 98; Gemini job files are data and are never imported here."""

import argparse
import json
import os
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--engine", required=True)
    parser.add_argument("--job", required=True)
    args = parser.parse_args()

    with open(args.job, "r", encoding="utf-8-sig") as stream:
        job = json.load(stream)
    progress_path = job.get("progress_file")
    if not progress_path:
        raise SystemExit("Render job has no progress_file.")

    process = subprocess.Popen(
        [args.engine, "--render-cli", os.path.abspath(args.job)],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    offset = 0
    while process.poll() is None:
        try:
            with open(progress_path, "rb") as stream:
                stream.seek(offset)
                for line in stream:
                    print(line.decode("utf-8-sig", errors="replace").rstrip("\r\n"), flush=True)
                offset = stream.tell()
        except FileNotFoundError:
            pass
        time.sleep(0.2)

    try:
        with open(progress_path, "rb") as stream:
            stream.seek(offset)
            for line in stream:
                print(line.decode("utf-8-sig", errors="replace").rstrip("\r\n"), flush=True)
    except FileNotFoundError:
        pass
    return process.returncode or 0


if __name__ == "__main__":
    raise SystemExit(main())
