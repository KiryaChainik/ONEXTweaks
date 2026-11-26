import hashlib
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

import github_ops as ops


REPO = "owner/project"
HEAD = "a" * 40
NEW_HEAD = "b" * 40


def pr(sha=HEAD):
    return {"number": 42, "state": "open", "draft": False, "base": {"ref": "main"},
            "head": {"sha": sha, "ref": "feature", "repo": {"full_name": REPO}}}


def run(number=10, **overrides):
    return {"id": number, "run_attempt": 1, "event": "pull_request", "path": ops.WORKFLOW,
            "head_sha": HEAD, "head_branch": "feature", "head_repository": {"full_name": REPO},
            "status": "completed", "conclusion": "success", "pull_requests": [], "html_url": "run", **overrides}


JOB = {"name": ops.JOB, "status": "completed", "conclusion": "success", "head_sha": HEAD,
       "html_url": "job", "steps": []}


class MergeGateTest(unittest.TestCase):
    def test_moving_head_aborts_the_wait(self):
        with patch.object(ops, "pull", side_effect=[pr(), pr(), pr(NEW_HEAD)]), \
             patch.object(ops, "windows_run", return_value=None), patch.object(ops.time, "sleep"):
            with self.assertRaisesRegex(ops.GateError, "head changed"):
                ops.pr_ci(REPO, 42, 10)

    def test_only_latest_matching_run_counts(self):
        old = run()
        queued = run(11) | {"status": "queued", "conclusion": None}
        unrelated = run(99) | {"head_branch": "other"}
        different_pr = run(100) | {"pull_requests": [{"number": 43}]}
        different_sha = run(101) | {"head_sha": NEW_HEAD}
        with patch.object(ops, "api", return_value={"workflow_runs": [old, queued, unrelated, different_pr, different_sha]}):
            self.assertEqual(ops.windows_run(REPO, HEAD, "pull_request", "feature", REPO, 42), queued)

    @patch.object(ops, "gh")
    @patch.object(ops, "windows_run", return_value=run())
    def test_merge_uses_the_tested_sha(self, _latest, gh):
        with patch.object(ops, "pr_ci", return_value=(pr(), run(), JOB)):
            ops.merge(SimpleNamespace(pr=42, timeout=0, check_only=False), REPO)
        gh.assert_called_once_with("pr", "merge", "42", "--repo", REPO, "--squash", "--match-head-commit", HEAD)

    @patch.object(ops, "gh")
    def test_failed_job_never_merges(self, gh):
        with patch.object(ops, "pr_ci", return_value=(pr(), run(), JOB | {"conclusion": "failure"})):
            with self.assertRaisesRegex(ops.GateError, "did not pass"):
                ops.merge(SimpleNamespace(pr=42, timeout=0, check_only=False), REPO)
        gh.assert_not_called()

    @patch.object(ops, "gh")
    def test_rerun_invalidates_prior_success(self, gh):
        with patch.object(ops, "pr_ci", return_value=(pr(), run(), JOB)), \
             patch.object(ops, "windows_run", return_value=run() | {"run_attempt": 2, "status": "queued"}):
            with self.assertRaisesRegex(ops.GateError, "CI changed"):
                ops.merge(SimpleNamespace(pr=42, timeout=0, check_only=False), REPO)
        gh.assert_not_called()


class DraftGateTest(unittest.TestCase):
    def setUp(self):
        directory = self.enterContext(tempfile.TemporaryDirectory())
        self.setup = Path(directory) / ops.SETUP
        self.setup.write_bytes(b"tested setup")
        self.digest = hashlib.sha256(self.setup.read_bytes()).hexdigest()
        self.receipt = {"schema": 1, "commit": HEAD, "sha256": self.digest,
                        "machine": {"os": "Windows 11", "architecture": "x64", "virtualMachine": True, "oneXConsole": True},
                        "checks": dict.fromkeys(ops.QA_CASES, True)}

    def test_accepts_only_byte_identical_setup(self):
        self.assertEqual(ops.validate_receipt(HEAD, self.setup, self.receipt), self.digest)
        self.setup.write_bytes(b"modified setup")
        with self.assertRaisesRegex(ops.GateError, "SHA-256"):
            ops.validate_receipt(HEAD, self.setup, self.receipt)

    def test_rejects_missing_rollback_or_wrong_source(self):
        with self.assertRaisesRegex(ops.GateError, "commit"):
            ops.validate_receipt(NEW_HEAD, self.setup, self.receipt)
        del self.receipt["checks"]["rollback1109"]
        with self.assertRaisesRegex(ops.GateError, "missing or failed"):
            ops.validate_receipt(HEAD, self.setup, self.receipt)

    def test_rejects_untested_commit_outside_main(self):
        with patch.object(ops, "api", side_effect=[{"commit": {"sha": NEW_HEAD}}, {"status": "diverged"}]), \
             patch.object(ops, "windows_run") as windows_run:
            with self.assertRaisesRegex(ops.GateError, "outside main"):
                ops.release_commit(REPO, HEAD)
        windows_run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
