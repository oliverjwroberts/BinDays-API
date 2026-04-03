/**
 * Collect failure information from a completed integration tests workflow run.
 *
 * Downloads the test-results.json artifact from the triggering workflow run,
 * reads the list of failed councils, and writes a failure-context.json file
 * for downstream investigation.
 *
 * Required environment variables:
 *   WORKFLOW_RUN_ID - The ID of the triggering workflow run
 *
 * Expects test-results.json to exist in the working directory (downloaded
 * by a prior workflow step).
 *
 * @param {Object} github - GitHub API client (unused, passed by actions/github-script)
 * @param {Object} context - GitHub Actions context
 * @param {Object} core - GitHub Actions core utilities
 */
module.exports = async ({ github, context, core }) => {
  const fs = require('fs');
  const runId = parseInt(process.env.WORKFLOW_RUN_ID);
  const runUrl = `https://github.com/${context.repo.owner}/${context.repo.repo}/actions/runs/${runId}`;

  if (!fs.existsSync('test-results.json')) {
    core.info('No test-results.json found — artifact may not have been uploaded');
    core.setOutput('has_failures', 'false');
    core.setOutput('failure_count', '0');
    core.setOutput('run_url', runUrl);
    return;
  }

  const results = JSON.parse(fs.readFileSync('test-results.json', 'utf8'));

  core.info(`Test results: ${results.passed.length} passed, ${results.failed.length} failed`);

  if (results.failed.length === 0) {
    core.setOutput('has_failures', 'false');
    core.setOutput('failure_count', '0');
    core.setOutput('run_url', runUrl);
    return;
  }

  const failures = results.failed.map(f => ({
    councilName: f.name,
    logs: f.message || 'Test failed (no details available)',
    needsInvestigation: true,
  }));

  const failureContext = { runId, runUrl, failures };
  fs.writeFileSync('failure-context.json', JSON.stringify(failureContext, null, 2));

  core.info(`Wrote failure-context.json with ${failures.length} failure(s)`);
  core.setOutput('has_failures', 'true');
  core.setOutput('failure_count', String(failures.length));
  core.setOutput('run_url', runUrl);
};
