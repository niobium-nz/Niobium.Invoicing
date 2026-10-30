const { test } = require('node:test');
const assert = require('node:assert/strict');
const { approvedHead, eligibleRun, testedRun, mergeEligibility, openPullRequest, readiness } = require('./delivery-policy.cjs');
const run = { id: 42, head_sha: 'head', event: 'push', head_branch: 'feature/test', conclusion: 'success', head_repository: { full_name: 'owner/repo' } };
function fixture() {
  const state = {
    pr: { number: 7, state: 'open', draft: false, base: { ref: 'main' }, head: { sha: 'head', ref: 'feature/test', repo: { full_name: 'owner/repo' } }, user: { login: 'author' } },
    reviews: [{ id: 1, state: 'APPROVED', commit_id: 'head', user: { login: 'author' } }],
    runs: [{ ...run, html_url: 'https://github.com/owner/repo/actions/runs/42' }],
    jobs: [{ name: 'Build and local tests', conclusion: 'success' },
      { name: 'Deploy test and run integration', conclusion: 'success', steps: [{ name: 'Run deployed integration tests', conclusion: 'success' }] }],
    comparison: { status: 'ahead', commits: [{ commit: { message: 'Change' } }] },
    head: 'head', openPulls: [], created: [], statuses: [], notices: [],
  };
  const github = {
    paginate: async (method, args) => { if (method === 'runs') assert.equal(args.head_sha, 'head'); return state[method]; },
    rest: {
      actions: { listWorkflowRuns: 'runs', listJobsForWorkflowRun: 'jobs' },
      pulls: { get: async () => ({ data: state.pr }), listReviews: 'reviews', list: 'openPulls',
        create: async args => { state.created.push(args); return { data: { ...state.pr, html_url: 'https://github.com/owner/repo/pull/7' } }; } },
      repos: { compareCommits: async () => ({ data: state.comparison }), createCommitStatus: async status => state.statuses.push(status) },
      git: { getRef: async () => ({ data: { object: { sha: state.head } } }) },
    },
  };
  return { state, github, pullNumber: 7,
    context: { repo: { owner: 'owner', repo: 'repo' }, sha: 'head', runId: 42, ref: 'refs/heads/feature/test', eventName: 'push', payload: { head_commit: { message: 'Implement delivery changes' } } },
    core: { notice: message => state.notices.push(message) } };
}

test('only a successful push of the exact same-repository feature commit qualifies', () => {
  assert.equal(eligibleRun(run, 'head', 'owner/repo'), true);
  for (const patch of [{ head_sha: 'old' }, { event: 'pull_request' }, { head_branch: 'main' }, { conclusion: 'skipped' }, { head_repository: { full_name: 'fork/repo' } }]) {
    assert.equal(eligibleRun({ ...run, ...patch }, 'head', 'owner/repo'), false);
  }
});

test('one permitted approval suffices, with no restriction on the PR author', () => {
  const approval = fixture().state.reviews[0];
  assert.equal(approvedHead([approval], 'head'), true);
  assert.equal(approvedHead([{ ...approval, user: { login: 'other' } }], 'head'), true);
  assert.equal(approvedHead([approval], 'new-head'), false);
  assert.equal(approvedHead([approval, { ...approval, id: 2, state: 'DISMISSED' }], 'head'), false);
  assert.equal(approvedHead([approval, { ...approval, id: 2, state: 'CHANGES_REQUESTED', user: { login: 'other' } }], 'head'), false);
  assert.equal(approvedHead([approval, { ...approval, id: 2, state: 'COMMENTED' }], 'head'), true);
});

test('skipped deployed tests and failed required jobs cannot qualify', async () => {
  const skipped = fixture(); skipped.state.jobs[1].steps[0].conclusion = 'skipped';
  await assert.rejects(testedRun(skipped.github, 'owner', 'repo', 'head'));
  for (const job of [0, 1]) for (const conclusion of ['failure', 'cancelled', 'skipped', null]) {
    const input = fixture(); input.state.jobs[job].conclusion = conclusion;
    await assert.rejects(testedRun(input.github, 'owner', 'repo', 'head'));
  }
});

test('pre-merge validation accepts an approved, tested current head', async () => {
  assert.equal((await mergeEligibility(fixture())).run.id, 42);
});

test('pre-merge validation rejects stale approval, missing test success, and a newer main', async () => {
  for (const mutate of [s => { s.reviews[0].commit_id = 'old'; }, s => { s.runs = []; }, s => { s.comparison.status = 'diverged'; }]) {
    const input = fixture(); mutate(input.state); await assert.rejects(mergeEligibility(input));
  }
});

test('pre-merge validation rejects closed, draft, foreign, and wrong-base PRs', async () => {
  for (const mutate of [s => { s.pr.state = 'closed'; }, s => { s.pr.draft = true; }, s => { s.pr.head.repo.full_name = 'fork/repo'; }, s => { s.pr.base.ref = 'other'; }]) {
    const input = fixture(); mutate(input.state); await assert.rejects(mergeEligibility(input));
  }
});

test('post-test job opens a ready PR to main without another instruction', async () => {
  const input = fixture(); await openPullRequest(input);
  assert.equal(input.state.created.length, 1);
  assert.equal(input.state.created[0].base, 'main');
  assert.equal(input.state.created[0].head, 'feature/test');
  assert.equal(input.state.created[0].draft, false);
  assert.match(input.state.created[0].body, /actions\/runs\/42/);
  assert.equal(input.state.statuses[0].sha, 'head');
  assert.equal(input.state.statuses[0].context, 'Test deployment verified');
});

test('automatic creation reuses existing PRs and ignores superseded or merged heads', async () => {
  for (const mutate of [s => { s.openPulls = [s.pr]; }, s => { s.head = 'new'; }, s => { s.comparison.status = 'identical'; }, s => { s.comparison.status = 'behind'; }]) {
    const input = fixture(); mutate(input.state); await openPullRequest(input); assert.equal(input.state.created.length, 0);
  }
});

test('automatic creation cannot run for main or a PR event', async () => {
  for (const change of [{ ref: 'refs/heads/main' }, { eventName: 'pull_request' }]) {
    const input = fixture(); Object.assign(input.context, change); await assert.rejects(openPullRequest(input)); assert.equal(input.state.created.length, 0);
  }
});

test('readiness publishes exact-commit success and fails without deployed evidence', async () => {
  const input = fixture(); input.context.payload.pull_request = input.state.pr; await readiness(input);
  assert.equal(input.state.statuses[0].sha, 'head'); assert.equal(input.state.statuses[0].state, 'success');
  input.state.runs = []; await assert.rejects(readiness(input)); assert.equal(input.state.statuses[1].state, 'failure');
});

test('readiness handles workflow completion without trusting forks or PR runs', async () => {
  const input = fixture(); input.context.payload.workflow_run = run; await readiness(input);
  input.context.payload.workflow_run = { ...run, head_repository: { full_name: 'fork/repo' } }; await readiness(input);
  input.context.payload.workflow_run = { ...run, event: 'pull_request' }; await readiness(input);
  assert.equal(input.state.statuses.length, 1);
});
