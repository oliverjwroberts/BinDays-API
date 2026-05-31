# Task: Fix a GitHub Issue

You are fixing a bug or problem described in a GitHub issue for the BinDays API project.

**IMPORTANT: This is a fully automated, non-interactive task running in a CI/CD pipeline.** There is NO user present to answer questions, provide clarification, or give approval. You cannot ask for help or confirmation - you must make all decisions autonomously and complete the entire task independently.

## Input

**Issue Title:** `$ISSUE_TITLE`
**Issue Number:** `#$ISSUE_NUMBER`

**Issue Body:**

```
$ISSUE_BODY
```

## Style Guide

When making code changes, you must follow all project conventions:

$STYLE_GUIDE

## Fix Process

### 1. Understand the Problem

Read the issue title and body to determine what needs fixing. Read `CLAUDE.md` for project overview and conventions.

### 2. Locate and Fix the Code

Find the relevant source file(s) and fix the problem. Follow all project conventions from the style guide and CLAUDE.md.

### 3. Verify

Run the relevant integration or unit tests. If the issue is collector-specific, filter by council name:

```bash
dotnet test BinDays.Api.IntegrationTests --filter "FullyQualifiedName~{CouncilName}"
```

Otherwise run the full test suite:

```bash
dotnet test
```

If tests fail, diagnose and iterate until they pass.

### 4. Create a PR

Once tests pass:

```bash
git checkout -b fix/issue-$ISSUE_NUMBER
git add <changed files>
git commit -m "{short description of fix}"
git push -u origin fix/issue-$ISSUE_NUMBER
cat > /tmp/pr-body.md << 'EOF'
Fixes #$ISSUE_NUMBER

{description of what was wrong and how you fixed it}
EOF
gh pr create --title "Fix: $ISSUE_TITLE" --body-file /tmp/pr-body.md
```
