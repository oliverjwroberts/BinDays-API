#!/bin/bash
set -e

# Fix a GitHub issue using Codex CLI.
#
# Reads the prompt template from .agent/prompts/fix-collector.md,
# substitutes the style guide and issue context, and invokes Codex CLI.
#
# Required environment variables:
#   ISSUE_TITLE  - Issue title
#   ISSUE_NUMBER - GitHub issue number

# Ensure dotnet is in PATH for Codex subshells
if [ -n "$DOTNET_ROOT" ]; then
  export PATH="$DOTNET_ROOT:$PATH"
fi

echo "dotnet location: $(which dotnet)"
echo "dotnet version: $(dotnet --version)"

ISSUE_BODY=$(cat .agent/fix-collector/issue_body.txt)
STYLE_GUIDE=$(cat .gemini/styleguide.md)

PROMPT=$(cat .agent/prompts/fix-collector.md)
PROMPT="${PROMPT//\$ISSUE_TITLE/$ISSUE_TITLE}"
PROMPT="${PROMPT//\$ISSUE_NUMBER/$ISSUE_NUMBER}"
PROMPT="${PROMPT//\$ISSUE_BODY/$ISSUE_BODY}"
PROMPT="${PROMPT//\$STYLE_GUIDE/$STYLE_GUIDE}"

echo "Running Codex to fix issue #$ISSUE_NUMBER..."
codex exec --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox "$PROMPT"
