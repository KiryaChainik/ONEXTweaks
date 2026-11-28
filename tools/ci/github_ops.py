"""Merge tested PRs, report Windows CI, and stage an approved Setup as a draft."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time


WORKFLOW = ".github/workflows/ci.yml"
JOB = "Windows build and tests"
MARKER = "<!-- onextweaks-windows-ci -->"
SETUP = "ONEXTweaks-Setup-x64.exe"
QA_CASES = ("cleanInstall", "reboot", "upgrade1109", "profilesPreserved",
            "rollback1109", "uninstall", "oemStartupRestored",
            "combined1110Repair", "uninstallAfterRepair", "sameVersionSetupRetry",
            "interruptedFirstSetup", "rollbackWithoutAppx")


class GateError(Exception):
    pass


def gh(*args):
    result = subprocess.run(["gh", *args], text=True, capture_output=True)
    if result.returncode:
        raise GateError(result.stderr.strip() or "GitHub CLI failed.")
    return result.stdout.strip()


def api(path, *args):
    return json.loads(gh("api", path, *args))


def repository(name):
    if name is None:
        name = json.loads(gh("repo", "view", "--json", "nameWithOwner"))["nameWithOwner"]
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", name):
        raise GateError("Expected GitHub repository OWNER/NAME.")
    return name


def pull(repo, number):
    return api(f"repos/{repo}/pulls/{number}")


def current_head(pr):
    if pr["state"] != "open" or pr["base"]["ref"] != "main":
        raise GateError("Expected an open PR targeting main.")
    return pr["head"]["sha"]


def windows_run(repo, sha, event, branch, head_repo, pr_number=None):
    # GitHub can omit pull_requests on a CI run, including for same-repository PRs.
    # Check the origin branch and repository as well as the full SHA.
    query = f"head_sha={sha}&event={event}&per_page=100"
    runs = api(f"repos/{repo}/actions/workflows/ci.yml/runs?{query}")["workflow_runs"]
    matches = [
        run for run in runs
        if run["path"] == WORKFLOW and run["event"] == event and run["head_sha"] == sha
        and run["head_branch"] == branch
        and (run.get("head_repository") or {}).get("full_name", "").lower() == head_repo.lower()
        and (not run["pull_requests"] or pr_number is None
             or any(pr["number"] == pr_number for pr in run["pull_requests"]))
    ]
    return max(matches, key=lambda run: run["id"], default=None)


def windows_job(repo, run):
    jobs = api(f"repos/{repo}/actions/runs/{run['id']}/attempts/{run['run_attempt']}/jobs?per_page=100")
    matches = [job for job in jobs["jobs"] if job["name"] == JOB and job["head_sha"] == run["head_sha"]]
    if len(matches) != 1:
        raise GateError(f"Expected exactly one {JOB} job for run {run['id']}.")
    return matches[0]


def assert_passed(run, job):
    if run["conclusion"] != "success" or job["conclusion"] != "success":
        raise GateError(f"Windows CI did not pass: {run['html_url']}")


def report_body(run, job, light=None, dark=None):
    state = job["conclusion"] or job["status"]
    lines = [MARKER, f"**Windows CI · {state}** · `{run['head_sha'][:12]}`",
             "", f"[Windows build and tests log]({job['html_url']})"]
    for step in job["steps"]:
        if step["name"] in ("Run model and installer tests", "Build Widget, Bridge, PowerAgent and Setup"):
            lines.append(f"- {step['name']} · **{step['conclusion'] or step['status']}**")
    if light and dark:
        lines += ["", f"Visual review: [light theme]({light}) · [dark theme]({dark})"]
    else:
        lines += ["", "Native Game Bar visuals are outside this CI run."]
    return "\n".join(lines)


def post_report(repo, pr, run, job, light=None, dark=None, author=None):
    number, sha = pr["number"], run["head_sha"]
    viewer = author or api("user")["login"]
    comments = gh("api", f"repos/{repo}/issues/{number}/comments?per_page=100", "--paginate", "--slurp")
    body = report_body(run, job, light, dark)
    existing = next(
        (comment for page in json.loads(comments) for comment in page
         if comment["user"]["login"] == viewer and comment["body"].startswith(MARKER)),
        None,
    )
    if current_head(pull(repo, number)) != sha:
        raise GateError("PR head changed before the report could be posted.")
    if existing is not None:
        # Preserve visual links already posted for this SHA when running the merge command.
        if not light and f"`{sha[:12]}`" in existing["body"] and "\nVisual review:" in existing["body"]:
            body = body.split("\n\nNative Game Bar visuals", 1)[0] + "\n\nVisual review:" + existing["body"].split("\nVisual review:", 1)[1]
        if body == existing["body"]:
            return existing["html_url"]
        return api(f"repos/{repo}/issues/comments/{existing['id']}", "--method", "PATCH", "-f", f"body={body}")["html_url"]
    return api(f"repos/{repo}/issues/{number}/comments", "--method", "POST", "-f", f"body={body}")["html_url"]


def pr_ci(repo, number, timeout, poll_interval=15):
    pr = pull(repo, number)
    sha = current_head(pr)
    deadline = time.monotonic() + timeout
    print(f"PR #{number} · {sha}", flush=True)
    while True:
        current = pull(repo, number)
        if current_head(current) != sha:
            raise GateError("PR head changed while waiting. Run the command again for the new commit.")
        head = current["head"]
        if not head["repo"]:
            raise GateError("The PR source repository is unavailable.")
        run = windows_run(repo, sha, "pull_request", head["ref"], head["repo"]["full_name"], number)
        if run and run["status"] == "completed":
            return current, run, windows_job(repo, run)
        if time.monotonic() >= deadline:
            raise GateError(f"Timed out waiting for Windows CI on {sha}.")
        time.sleep(poll_interval)


def merge(args, repo):
    pr, run, job = pr_ci(repo, args.pr, args.timeout)
    assert_passed(run, job)
    print(f"Passed: {job['html_url']}")
    if args.check_only:
        return
    if pr["draft"]:
        raise GateError("The PR is still a draft.")
    sha = run["head_sha"]
    head = pr["head"]
    latest = windows_run(repo, sha, "pull_request", head["ref"], head["repo"]["full_name"], args.pr)
    if not latest or any(latest[key] != run[key] for key in ("id", "run_attempt", "status", "conclusion")):
        raise GateError("Windows CI changed before the merge. Run the command again.")
    # GitHub checks the expected head again atomically as part of the merge.
    gh("pr", "merge", str(args.pr), "--repo", repo, "--squash", "--match-head-commit", sha)
    print(f"Merged PR #{args.pr} at tested head {sha}.")


def report(args, repo):
    if bool(args.light) != bool(args.dark):
        raise GateError("Supply both theme links or omit both.")
    for link in (args.light, args.dark):
        if link and not re.fullmatch(r"https://[^\s()<>`]+", link):
            raise GateError("Theme screenshots or artifacts must use HTTPS links.")
    pr, run, job = pr_ci(repo, args.pr, args.timeout)
    if args.head and args.head != run["head_sha"]:
        raise GateError("Visual evidence belongs to a different PR head.")
    if args.light and not args.head:
        raise GateError("Supply --head with the exact SHA of the screenshot build.")
    print(post_report(repo, pr, run, job, args.light, args.dark))


def report_run(args, repo):
    run = api(f"repos/{repo}/actions/runs/{args.run}")
    if run["path"] != WORKFLOW or run["event"] != "pull_request" or run["status"] != "completed":
        return
    sha, head_repo = run["head_sha"], run["head_repository"]
    if head_repo is None:
        return
    prs = api(f"repos/{repo}/commits/{sha}/pulls")
    for pr in prs:
        head = pr["head"]
        if (pr["state"] != "open" or pr["base"]["ref"] != "main" or head["sha"] != sha
                or head["ref"] != run["head_branch"] or not head["repo"]
                or head["repo"]["id"] != head_repo["id"]):
            continue
        latest = windows_run(repo, sha, "pull_request", head["ref"], head_repo["full_name"], pr["number"])
        if latest and (latest["id"], latest["run_attempt"]) == (run["id"], run["run_attempt"]):
            print(post_report(repo, pr, run, windows_job(repo, run), author="github-actions[bot]"))


def release_commit(repo, sha):
    if not re.fullmatch(r"[a-f0-9]{40}", sha):
        raise GateError("Use a full Git commit SHA.")
    main = api(f"repos/{repo}/branches/main")["commit"]["sha"]
    relation = api(f"repos/{repo}/compare/{sha}...{main}")["status"]
    if relation not in ("identical", "ahead"):
        raise GateError("Release commit is outside main.")
    run = windows_run(repo, sha, "push", "main", repo)
    if not run or run["status"] != "completed":
        raise GateError("Release commit has no completed main Windows CI run.")
    job = windows_job(repo, run)
    assert_passed(run, job)
    return job


def validate_receipt(sha, setup, receipt):
    if setup.name != SETUP or not setup.is_file():
        raise GateError(f"Expected the built {SETUP}.")
    with setup.open("rb") as data:
        digest = hashlib.file_digest(data, "sha256").hexdigest()
    if receipt.get("schema") != 1 or receipt.get("commit") != sha or receipt.get("sha256", "").lower() != digest:
        raise GateError("VM receipt does not identify this commit and Setup SHA-256.")
    machine = receipt.get("machine", {})
    if (machine.get("os") != "Windows 11" or machine.get("architecture") != "x64"
            or machine.get("virtualMachine") is not True or machine.get("oneXConsole") is not True):
        raise GateError("VM receipt must identify Windows 11 x64 with OneXConsole.")
    missing = [case for case in QA_CASES if receipt.get("checks", {}).get(case) is not True]
    if missing:
        raise GateError("Required Windows VM scenarios are missing or failed: " + ", ".join(missing))
    return digest


def release(args, repo):
    if not re.fullmatch(r"v[0-9]+(?:\.[0-9]+){2,3}(?:-[A-Za-z0-9.-]+)?", args.tag):
        raise GateError("Use a version tag such as v0.29.1110-beta.1.")
    job = release_commit(repo, args.commit)
    digest = validate_receipt(args.commit, args.setup, json.loads(args.receipt.read_text(encoding="utf-8-sig")))
    if args.check_only:
        print(f"Passed: {job['html_url']}\nSHA-256: {digest}")
        return
    # Bind a tag which already exists to the tested commit as well.
    refs = api(f"repos/{repo}/git/matching-refs/tags/{args.tag}")
    for ref in refs:
        if ref["ref"] == f"refs/tags/{args.tag}":
            obj = ref["object"]
            while obj["type"] == "tag":
                obj = api(f"repos/{repo}/git/tags/{obj['sha']}")["object"]
            if obj["type"] != "commit" or obj["sha"] != args.commit:
                raise GateError("The existing tag points at a different commit.")
    notes = (
        f"Setup SHA-256: `{digest}`\n\n"
        f"Source commit: `{args.commit}` · [Windows CI]({job['html_url']})\n\n"
        "Windows 11 x64 VM with OneXConsole: install, reboot, 1109 upgrade with profiles, "
        "rollback, uninstall and OEM autostart restoration passed.\n\n"
        "Before publishing, verify Game Bar and controller input on ONEXPLAYER."
    )
    draft = api(f"repos/{repo}/releases", "--method", "POST", "-f", f"tag_name={args.tag}",
                "-f", f"target_commitish={args.commit}", "-f", f"name={args.tag}",
                "-F", "draft=true", "-F", "prerelease=true", "-F", "generate_release_notes=true",
                "-f", f"body={notes}")
    try:
        asset = api(f"https://uploads.github.com/repos/{repo}/releases/{draft['id']}/assets?name={SETUP}",
                    "--method", "POST", "-H", "Content-Type: application/octet-stream", "--input", str(args.setup))
        if asset.get("digest") != f"sha256:{digest}":
            raise GateError("GitHub did not confirm the VM-tested asset digest.")
    except GateError as error:
        raise GateError(f"Incomplete draft {draft['html_url']}: {error}") from error
    print(f"Draft: {draft['html_url']}\nSHA-256: {digest}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", help="GitHub owner/repo (defaults to the current repository)")
    commands = parser.add_subparsers(dest="command", required=True)
    for name, action in (("merge", merge), ("report", report)):
        command = commands.add_parser(name)
        command.add_argument("pr", type=int)
        command.add_argument("--timeout", type=int, default=2400)
        command.set_defaults(action=action)
        if name == "merge":
            command.add_argument("--check-only", action="store_true")
        else:
            command.add_argument("--head")
            command.add_argument("--light")
            command.add_argument("--dark")
    preflight = commands.add_parser("verify-main")
    preflight.add_argument("commit")
    preflight.set_defaults(action=lambda args, repo: print(release_commit(repo, args.commit)["html_url"]))
    triggered = commands.add_parser("report-run", help="Update the CI bot comment from a finished Actions run")
    triggered.add_argument("run", type=int)
    triggered.set_defaults(action=report_run)
    draft = commands.add_parser("draft")
    draft.add_argument("--commit", required=True)
    draft.add_argument("--tag", required=True)
    draft.add_argument("--setup", type=Path, required=True)
    draft.add_argument("--receipt", type=Path, required=True)
    draft.add_argument("--check-only", action="store_true")
    draft.set_defaults(action=release)
    args = parser.parse_args()
    try:
        args.action(args, repository(args.repo))
    except (GateError, OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
