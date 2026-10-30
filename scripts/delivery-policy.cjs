// API-only policy checks. Never check out or execute a pull request's code with privileged credentials.
const WORKFLOW = 'azure-deploy.yml';
const REQUIRED_JOBS = ['Build and local tests', 'Deploy test and run integration'];
const REQUIRED_STEP = 'Run deployed integration tests';

function eligibleRun(run, sha, repository) {
  return run.head_sha === sha && run.event === 'push' && run.head_branch !== 'main' &&
    run.conclusion === 'success' && run.head_repository?.full_name === repository;
}

function approvedHead(reviews, head) {
  const decisions = new Map();
  for (const review of [...reviews].sort((a, b) => a.id - b.id)) {
    if (['APPROVED', 'CHANGES_REQUESTED', 'DISMISSED'].includes(review.state)) {
      decisions.set(review.user.login, review);
    }
  }
  const latest = [...decisions.values()];
  return !latest.some(review => review.state === 'CHANGES_REQUESTED') &&
    latest.some(review => review.state === 'APPROVED' && review.commit_id === head);
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

// This is a pre-merge agent check, never a gate between a merged PR and production.
async function mergeEligibility({ github, context, pullNumber }) {
  const { owner, repo } = context.repo;
  const { data: pr } = await github.rest.pulls.get({ owner, repo, pull_number: pullNumber });
  if (pr.state !== 'open' || pr.draft || pr.base.ref !== 'main' || pr.head.repo?.full_name !== `${owner}/${repo}`) {
    throw new Error('Merge requires an open, ready, repository-owned PR to main.');
  }
  const reviews = await github.paginate(github.rest.pulls.listReviews, { owner, repo, pull_number: pullNumber, per_page: 100 });
  if (!approvedHead(reviews, pr.head.sha)) throw new Error('Approval of the current PR head is required.');
  const run = await testedRun(github, owner, repo, pr.head.sha);
  const { data: comparison } = await github.rest.repos.compareCommits({ owner, repo, base: 'main', head: pr.head.sha });
  if (!['ahead', 'identical'].includes(comparison.status)) throw new Error('Update the feature branch from main and repeat deployment/tests.');
  return { pr, run };
}

// Called only after the feature workflow's build and deployed integration jobs succeed.
async function openPullRequest({ github, context, core }) {
  const { owner, repo } = context.repo;
  if (context.eventName !== 'push' || !context.ref.startsWith('refs/heads/') || context.ref === 'refs/heads/main') {
    throw new Error('Automatic PR creation requires a feature push.');
  }
  const branch = context.ref.slice('refs/heads/'.length);
  const { data: current } = await github.rest.git.getRef({ owner, repo, ref: `heads/${branch}` });
  if (current.object.sha !== context.sha) { core.notice('A newer feature commit is being tested; leave PR creation to that run.'); return; }
  // The workflow's needs:[verify,test] supplies completed test evidence, including during bootstrap
  // before the default-branch workflow_run listener exists.
  await github.rest.repos.createCommitStatus({ owner, repo, sha: context.sha, state: 'success',
    context: 'Test deployment verified', description: 'Build, test deployment and deployed integration passed.',
    target_url: `https://github.com/${owner}/${repo}/actions/runs/${context.runId}` });
  const pulls = await github.paginate(github.rest.pulls.list, { owner, repo, state: 'open', base: 'main', head: `${owner}:${branch}`, per_page: 100 });
  const existing = pulls.find(pr => pr.head.repo?.full_name === `${owner}/${repo}` && pr.head.ref === branch);
  if (existing) { core.notice(`Continue review on ${existing.html_url}`); return existing; }
  const { data: comparison } = await github.rest.repos.compareCommits({ owner, repo, base: 'main', head: context.sha });
  if (comparison.status === 'identical' || comparison.status === 'behind') { core.notice('No feature changes remain to review.'); return; }
  const title = (context.payload.head_commit?.message?.split('\n')[0] || `Tested changes from ${branch}`).slice(0,200);
  const body = `This feature commit passed the build, coverage checks, test deployment and deployed integration jobs.\n\n` +
    `Commit: \`${context.sha}\`\n\nVerification: https://github.com/${owner}/${repo}/actions/runs/${context.runId}\n\n` +
    `Review the current head before merging. The agent must address actionable review comments, retest changes, merge only after approval, and monitor production deployment and read-only tests afterward.\n\n` +
    `Commits:\n` + comparison.commits.map(commit => `- ${commit.commit.message.split('\n')[0]}`).join('\n');
  const { data: pr } = await github.rest.pulls.create({ owner, repo, base: 'main', head: branch, title, body, draft: false });
  core.notice(`Ready for review: ${pr.html_url}`);
  return pr;
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

module.exports = { mergeEligibility, openPullRequest, readiness, testedRun, eligibleRun, approvedHead };
