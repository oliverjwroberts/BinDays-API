/**
 * Parse issue details for the /fix command.
 *
 * @param {Object} github - GitHub API client
 * @param {Object} context - GitHub Actions context
 * @param {Object} core - GitHub Actions core utilities
 */
module.exports = async ({ github, context, core }) => {
  const issueNumber = context.payload.issue.number;
  const issueTitle = context.payload.issue.title;
  const issueBody = context.payload.issue.body;

  core.setOutput('issue_number', issueNumber);
  core.setOutput('issue_title', issueTitle);
  core.setOutput('issue_body', issueBody);
  core.info(`Issue Number: ${issueNumber}`);
  core.info(`Issue Title: ${issueTitle}`);
};
