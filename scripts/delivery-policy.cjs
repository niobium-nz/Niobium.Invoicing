// API-only policy checks. Never check out or execute a pull request's code with privileged credentials.
const WORKFLOW = 'azure-deploy.yml';
const REQUIRED_JOBS = ['Build and local tests', 'Deploy test and run integration'];
const REQUIRED_STEP = 'Run deployed integration tests';

function eligibleRun(run, sha, repository) {
  return run.head_sha === sha && run.event === 'push' && run.head_branch !== 'main' &&
    run.conclusion === 'success' && run.head_repository?.full_name === repository;
}

function approvedHead(reviews, head, author) {
  const decisions = new Map();
  for (const review of [...reviews].sort((a, b) => a.id - b.id)) {
    if (['APPROVED', 'CHANGES_REQUESTED', 'DISMISSED'].includes(review.state)) {
      decisions.set(review.user.login, review);
    }
  }
  const latest = [...decisions.values()];
  return !latest.some(review => review.state === 'CHANGES_REQUESTED') &&
    latest.some(review => review.state === 'APPROVED' && review.commit_id === head && review.user.login !== author);
}

async function testedRun(github, owner, repo, sha, before) {
  const runs = await github.paginate(github.rest.actions.listWorkflowRuns, {
    owner, repo, workflow_id: WORKFLOW, event: 'push', head_sha: sha, per_page: 100,
  });
  for (const run of runs) {
    if (!eligibleRun(run, sha, `${owner}/${repo}`) || (before && Date.parse(run.updated_at) > Date.parse(before))) continue;
    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {
      owner, repo, run_id: run.id, filter: 'latest', per_page: 100,
    });
    if (!REQUIRED_JOBS.every(name => jobs.some(job => job.name === name && job.conclusion === 'success'))) continue;
    const deployment = jobs.find(job => job.name === REQUIRED_JOBS[1]);
    if (!deployment.steps.some(step => step.name === REQUIRED_STEP && step.conclusion === 'success')) continue;
    return run;
  }
  throw new Error('This feature commit has no successful build, test deployment and deployed integration run.');
}

async function production({ github, context, core }) {
  const { owner, repo } = context.repo;
  if (context.ref !== 'refs/heads/main' || context.eventName !== 'push') throw new Error('Production requires a push to main.');
  const pulls = await github.paginate(github.rest.repos.listPullRequestsAssociatedWithCommit, {
    owner, repo, commit_sha: context.sha, per_page: 100,
  });
  const candidates = pulls.filter(pr => pr.merged_at && pr.base.ref === 'main' && pr.merge_commit_sha === context.sha &&
    pr.head.repo?.full_name === `${owner}/${repo}`);
  if (candidates.length !== 1) throw new Error('Production requires exactly one merged PR for this main commit.');
  const pr = candidates[0];
  const reviews = await github.paginate(github.rest.pulls.listReviews, { owner, repo, pull_number: pr.number, per_page: 100 });
  if (!approvedHead(reviews, pr.head.sha, pr.user.login)) throw new Error('An independent approval of the current PR head is required.');
  const run = await testedRun(github, owner, repo, pr.head.sha, pr.merged_at);
  const main = await github.rest.git.getCommit({ owner, repo, commit_sha: context.sha });
  const tested = await github.rest.git.getCommit({ owner, repo, commit_sha: pr.head.sha });
  if (main.data.tree.sha !== tested.data.tree.sha) throw new Error('Merged content differs from the tested feature tree. Update the feature from main and retest.');
  core.notice(`Reviewed PR #${pr.number}; successful test deployment: ${run.html_url}`);
  return run;
}

async function readiness({ github, context, core }) {
  const { owner, repo } = context.repo;
  const pr = context.payload.pull_request;
  const eventRun = context.payload.workflow_run;
  if (pr && pr.head.repo?.full_name !== `${owner}/${repo}`) throw new Error('Test deployments require a repository-owned feature branch.');
  if (eventRun && (eventRun.head_repository?.full_name !== `${owner}/${repo}` || eventRun.event !== 'push' || eventRun.head_branch === 'main')) return;
  const sha = pr?.head.sha ?? eventRun?.head_sha;
  if (!sha) throw new Error('Missing feature commit.');
  try {
    const run = await testedRun(github, owner, repo, sha);
    await github.rest.repos.createCommitStatus({ owner, repo, sha, state: 'success', context: 'Test deployment verified',
      description: 'This commit passed deployed integration tests.', target_url: run.html_url });
    core.notice(`Feature commit is eligible for review: ${run.html_url}`);
  } catch (error) {
    await github.rest.repos.createCommitStatus({ owner, repo, sha, state: 'failure', context: 'Test deployment verified',
      description: 'Push this commit and pass test deployment/integration before review.' });
    throw error;
  }
}

module.exports = { production, readiness, testedRun, eligibleRun, approvedHead };
