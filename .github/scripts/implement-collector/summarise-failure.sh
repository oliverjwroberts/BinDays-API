#!/bin/bash
set -o pipefail

# Ask Codex to summarise why the collector implementation failed.
#
# Collects context from test output and git state, then prompts Codex
# for a concise, human-readable summary suitable for a GitHub issue comment.
#
# Outputs:
#   Sets step output "summary" via $GITHUB_OUTPUT.
#
# Required environment variables:
#   COLLECTOR_NAME - Council name in PascalCase
#   GITHUB_OUTPUT  - Path to GitHub Actions output file

CONTEXT=""

# Collect last lines from the Codex implement step log
if [ -f .agent/codex-implement.log ]; then
  CONTEXT+="## Codex implement log (last 100 lines)
$(tail -100 .agent/codex-implement.log)

"
fi

PROMPT="You are analysing a failed GitHub Actions run that tried to automatically implement a BinDays collector for ${COLLECTOR_NAME:-an unknown council}.

Here is the context from the failed run:

${CONTEXT}

Write a short summary (2-4 sentences) explaining why the implementation failed. Be specific: mention the actual error messages, missing files, or test failures. Do not suggest fixes. Do not use markdown formatting. Write in plain English as if commenting on a GitHub issue."

# Run Codex for the summary (short task, no sandbox needed)
SUMMARY=$(codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox "$PROMPT" 2>/dev/null)

if [ -z "$SUMMARY" ]; then
  SUMMARY="Could not generate a failure summary. Please check the workflow logs."
fi

# Write to step output (handle multiline via delimiter)
{
  echo "summary<<SUMMARY_EOF"
  echo "$SUMMARY"
  echo "SUMMARY_EOF"
} >> "$GITHUB_OUTPUT"
