#!/usr/bin/env bash
# Builds cs/Sim and lists compile errors with their source lines: all files (counts), or one file's errors.
# Usage: bash cs/tools/errs.sh            -> errors per file
#        bash cs/tools/errs.sh Motion      -> Motion.g.cs's errors with their lines
cd "$(dirname "$0")/../.." || exit 1
dotnet build cs/Sim/Sim.csproj 2>&1 | grep -E " error " | sed 's/ \[.*//' | sort -u > "${TMPDIR:-/tmp}/cserr.txt"
if [ -z "$1" ]; then
  echo "$(wc -l < "${TMPDIR:-/tmp}/cserr.txt") errors"
  grep -oE "(Generated.)?[A-Za-z]+\.(g\.)?cs\(" "${TMPDIR:-/tmp}/cserr.txt" | sed 's/($//' | sort | uniq -c | sort -rn
  exit 0
fi
grep -E "[\\/]$1(\.g)?\.cs\(" "${TMPDIR:-/tmp}/cserr.txt" | while IFS= read -r l; do
  f=$(echo "$l" | sed -E 's/^(.*)\(([0-9]+),([0-9]+)\).*/\1/')
  n=$(echo "$l" | sed -E 's/^.*\(([0-9]+),([0-9]+)\).*/\1/')
  msg=$(echo "$l" | sed -E 's/.*error (CS[0-9]+): /\1 /')
  echo "$n: $msg"
  echo "    $(sed -n "${n}p" "$f" | sed 's/^ *//' | cut -c1-230)"
done
