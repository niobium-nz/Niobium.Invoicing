const { test } = require('node:test');
const assert = require('node:assert/strict');
const { approvedHead, eligibleRun, testedRun, production, readiness } = require('./delivery-policy.cjs');
const run = { id: 42, head_sha: 'head', event: 'push', head_branch: 'feature/test', conclusion: 'success', head_repository: { full_name: 'owner/repo' } };

test('only a successful push of the exact same-repository feature commit qualifies', () => {
  assert.equal(eligibleRun(run, 'head', 'owner/repo'), true);
  for (const patch of [{ head_sha: 'old' }, { event: 'pull_request' }, { head_branch: 'main' }, { conclusion: 'skipped' }, { head_repository: { full_name: 'fork/repo' } }]) {
    assert.equal(eligibleRun({ ...run, ...patch }, 'head', 'owner/repo'), false);
  }
});

test('review must approve the tested head, come from another reviewer and have no outstanding changes request', () => {
  const approval = { id: 1, state: 'APPROVED', commit_id: 'head', user: { login: 'reviewer' } };
  assert.equal(approvedHead([approval], 'head', 'author'), true);
  assert.equal(approvedHead([approval], 'new-head', 'author'), false);
  assert.equal(approvedHead([approval], 'head', 'reviewer'), false);
  assert.equal(approvedHead([approval, { ...approval, id: 2, state: 'DISMISSED' }], 'head', 'author'), false);
  assert.equal(approvedHead([approval, { ...approval, id: 2, state: 'CHANGES_REQUESTED', user: { login: 'other' } }], 'head', 'author'), false);
});

test('a green workflow with skipped deployed tests is rejected', async () => {
  const actions = { listWorkflowRuns: 'runs', listJobsForWorkflowRun: 'jobs' };
  let conclusion = 'skipped';
  const github = { rest: { actions }, paginate: async method => method === 'runs' ? [run] : [
    { name: 'Build and local tests', conclusion: 'success' },
    { name: 'Deploy test and run integration', conclusion: 'success', steps: [{ name: 'Run deployed integration tests', conclusion }] },
  ] };
  await assert.rejects(testedRun(github, 'owner', 'repo', 'head'));
  conclusion = 'success';
  assert.equal((await testedRun(github, 'owner', 'repo', 'head')).id, 42);
});

test('production rejects PR events, feature pushes and manual dispatch', async () => {
  for (const [ref, eventName] of [['refs/heads/main', 'pull_request'], ['refs/heads/feature', 'push'], ['refs/heads/main', 'workflow_dispatch']]) {
    await assert.rejects(production({ github: {}, context: { repo: {}, ref, eventName }, core: {} }));
  }
});

function fixture() {
  const state = {
    pulls: [{ number: 7, merged_at: '2026-10-30T12:00:00Z', base: { ref: 'main' }, merge_commit_sha: 'merge',
      head: { sha: 'head', repo: { full_name: 'owner/repo' } }, user: { login: 'author' } }],
    reviews: [{ id: 1, state: 'APPROVED', commit_id: 'head', user: { login: 'reviewer' } }],
    runs: [{ ...run, html_url: 'https://github.com/owner/repo/actions/runs/42', updated_at: '2026-10-30T11:00:00Z' }],
    jobs: [{ name: 'Build and local tests', conclusion: 'success' },
      { name: 'Deploy test and run integration', conclusion: 'success', steps: [{ name: 'Run deployed integration tests', conclusion: 'success' }] }],
    trees: { head: 'same-tree', merge: 'same-tree' }, statuses: [], notices: [],
  };
  const github = {
    paginate: async (method, args) => {
      if (method === 'runs') assert.equal(args.head_sha, 'head');
      return state[method];
    },
    rest: {
      actions: { listWorkflowRuns: 'runs', listJobsForWorkflowRun: 'jobs' },
      pulls: { listReviews: 'reviews' },
      repos: { listPullRequestsAssociatedWithCommit: 'pulls', createCommitStatus: async status => state.statuses.push(status) },
      git: { getCommit: async ({ commit_sha }) => ({ data: { tree: { sha: state.trees[commit_sha] } } }) },
    },
  };
  return { state, github, context: { repo: { owner: 'owner', repo: 'repo' }, sha: 'merge', ref: 'refs/heads/main', eventName: 'push', payload: {} },
    core: { notice: message => state.notices.push(message) } };
}

test('production accepts reviewed, previously tested content including squash/merge commit identities', async () => {
  const input = fixture();
  assert.equal((await production(input)).id, 42);
  assert.match(input.state.notices[0], /Reviewed PR #7/);
});

test('production rejects direct pushes, ambiguous merges and foreign PRs', async () => {
  for (const mutate of [
    s => { s.pulls = []; },
    s => { s.pulls.push(s.pulls[0]); },
    s => { s.pulls[0].head.repo.full_name = 'fork/repo'; },
    s => { s.pulls[0].merged_at = null; },
    s => { s.pulls[0].base.ref = 'other'; },
    s => { s.pulls[0].merge_commit_sha = 'old'; },
  ]) {
    const input = fixture();
    mutate(input.state);
    await assert.rejects(production(input), /exactly one merged PR/);
  }
});

test('production rejects unreviewed updates and content introduced after feature verification', async () => {
  const stale = fixture();
  stale.state.reviews[0].commit_id = 'old-head';
  await assert.rejects(production(stale), /independent approval/);
  const changed = fixture();
  changed.state.trees.merge = 'new-base-content';
  await assert.rejects(production(changed), /differs from the tested feature tree/);
  const late = fixture();
  late.state.runs[0].updated_at = '2026-10-30T13:00:00Z';
  await assert.rejects(production(late), /no successful build/);
});

test('every required job must succeed and approval comments do not erase formal decisions', async () => {
  for (const job of [0, 1]) {
    for (const conclusion of ['failure', 'cancelled', 'skipped', null]) {
      const input = fixture();
      input.state.jobs[job].conclusion = conclusion;
      await assert.rejects(testedRun(input.github, 'owner', 'repo', 'head'));
    }
  }
  const reviews = fixture().state.reviews;
  assert.equal(approvedHead([...reviews, { ...reviews[0], id: 2, state: 'COMMENTED' }], 'head', 'author'), true);
  assert.equal(approvedHead([{ ...reviews[0], id: 0, state: 'CHANGES_REQUESTED' }, ...reviews], 'head', 'author'), true);
});

test('readiness publishes exact-commit success and fails closed without deployed evidence', async () => {
  const input = fixture();
  input.context.payload.pull_request = input.state.pulls[0];
  await readiness(input);
  assert.equal(input.state.statuses[0].sha, 'head');
  assert.equal(input.state.statuses[0].state, 'success');
  assert.equal(input.state.statuses[0].context, 'Test deployment verified');
  input.state.runs = [];
  await assert.rejects(readiness(input));
  assert.equal(input.state.statuses[1].state, 'failure');
});

test('readiness handles workflow completion without executing PR code or trusting forks', async () => {
  const input = fixture();
  input.context.payload.workflow_run = run;
  await readiness(input);
  assert.equal(input.state.statuses.length, 1);
  input.context.payload.workflow_run = { ...run, event: 'pull_request' };
  await readiness(input);
  input.context.payload.workflow_run = { ...run, head_branch: 'main' };
  await readiness(input);
  assert.equal(input.state.statuses.length, 1);
  input.context.payload = { pull_request: { head: { repo: { full_name: 'fork/repo' } } } };
  await assert.rejects(readiness(input), /repository-owned/);
  input.context.payload = {};
  await assert.rejects(readiness(input), /Missing feature commit/);
});
