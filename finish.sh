#!/usr/bin/env bash
# The one-command end of a session: run the check, and if it is green commit everything. It never pushes.
#   ./finish.sh "Commit subject and body"            ./finish.sh -F message.txt      (multi-line messages, trailers)
#   ./finish.sh --all "..."                          the check with the Godot self-test too (./check.sh --all)
# Refuses when there is nothing to commit. Runs ./check.sh (its summary is printed as it prints it); on red nothing is
# staged or committed and the exit is non-zero. On green: `git add -A` (.gitignore keeps .check/ and the like out), prints
# what was staged and the new commit's hash and subject. Warns (does not fail) when the commit adds no new file under
# log/: the convention is an entry per non-trivial change (log/_template.md has the shape; NOTES.md "Session log").
# Commit messages end with the attribution trailer your instructions give, if any: put it in the message.
set -uo pipefail
cd "$(dirname "$0")"
ALL=""; msg=(); usage="usage: ./finish.sh [--all] \"message\"   or   ./finish.sh [--all] -F message-file"
while [ $# -gt 0 ]; do
  case "$1" in
    --all) ALL="--all"; shift;;
    -F) [ -n "${2:-}" ] && [ -f "$2" ] || { echo "finish: -F needs a message file that exists"; echo "$usage"; exit 2; }
        msg=(-F "$2"); shift 2;;
    *) [ ${#msg[@]} = 0 ] || { echo "finish: one message only"; echo "$usage"; exit 2; }
       msg=(-m "$1"); shift;;
  esac
done
[ ${#msg[@]} -gt 0 ] || { echo "finish: no commit message"; echo "$usage"; exit 2; }
[ -n "$(git status --porcelain)" ] || { echo "finish: nothing to commit"; exit 1; }

./check.sh $ALL || { echo "finish: the check is red: nothing committed"; exit 1; }

git add -A
echo "staged:"; git diff --cached --name-status | sed 's/^/  /'
if ! git commit -q "${msg[@]}"; then echo "finish: git commit failed (a hook?): the changes are staged, not committed"; exit 1; fi
echo "committed: $(git log -1 --format='%h %s')"
if [ -z "$(git diff --name-only --diff-filter=A HEAD~1 HEAD -- 'log/*.md' | grep -v '^log/_')" ]; then
  echo "finish: WARNING: no new file in log/ in this commit. A non-trivial change gets an entry, log/YYYY-MM-DD-NN-slug.md (shape: log/_template.md); add it and amend, or ignore for a trivial change"
fi
exit 0
