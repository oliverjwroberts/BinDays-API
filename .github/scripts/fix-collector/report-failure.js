/**
 * Report fix failure by commenting on the issue.
 *
 * Required environment variables:
 *   ISSUE_NUMBER - GitHub issue number
 *
 * @param {Object} github - GitHub API client
 * @param {Object} context - GitHub Actions context
 * @param {Object} core - GitHub Actions core utilities
 */
module.exports = async ({ github, context, core }) => {
  const issueNumber = parseInt(process.env.ISSUE_NUMBER);
  const runUrl = `https://github.com/${context.repo.owner}/${context.repo.repo}/actions/runs/${context.runId}`;

  await github.rest.issues.createComment({
    owner: context.repo.owner,
    repo: context.repo.repo,
    issue_number: issueNumber,
    body: `**Automated fix failed.** The fix workflow encountered an error.\n\n[View workflow run](${runUrl})`,
  });
};
