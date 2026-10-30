"""Read-only deployed HTTP checks. No storage/queue SDK, credentials, setup, or cleanup writes."""
import argparse
import json
import os
from pathlib import Path
import re
import time
import uuid
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, urlencode, urljoin, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class ReadOnlyClient:
    def __init__(self, base, opener=None):
        parsed = urlsplit(base)
        if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in ("", "/"):
            raise ValueError("An HTTPS service origin is required")
        self.base = base.rstrip("/") + "/"
        self.opener = opener or build_opener(NoRedirect())

    def request(self, method, path):
        # These are the only read-only application routes. GET alone is not a safe-route guarantee.
        parsed = urlsplit(path)
        invoice = re.fullmatch(r"/([0-9a-fA-F-]{36})/invoices/([1-9][0-9]*)", parsed.path)
        if method != "GET" or parsed.scheme or parsed.netloc or parsed.fragment or not path.startswith("/"):
            raise ValueError("Only allowlisted same-origin GET requests are permitted")
        if parsed.path == "/health":
            if parsed.query:
                raise ValueError("Health query parameters are not permitted")
        elif invoice:
            uuid.UUID(invoice.group(1))
            if int(invoice.group(2)) > 9223372036854775807 or set(parse_qs(parsed.query, keep_blank_values=True)) - {"token"}:
                raise ValueError("Invalid read-only invoice query")
        else:
            raise ValueError("This route is not on the read-only allowlist")
        req = Request(urljoin(self.base, path), method="GET")
        try:
            with self.opener.open(req, timeout=20) as response:
                return response.status, response.headers.get_content_type(), response.read(4 * 1024 * 1024)
        except HTTPError as error:
            # Do not expose error bodies or URLs: they may contain fixture data or link tokens.
            error.close()
            return error.code, "", b""

    def get(self, path):
        return self.request("GET", path)


def checks(client, expected, fixture, wait_seconds=180, require_fixture=False):
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise ValueError("An exact deployment commit is required")
    if require_fixture and not all(fixture.get(key) for key in ("biller", "invoice", "token")):
        raise ValueError("The production test invoice fixture must be configured before reading the deployment")
    results = []
    deadline = time.monotonic() + wait_seconds
    healthy = False
    while True:
        try:
            status, _, body = client.get("/health")
            payload = json.loads(body) if status == 200 else {}
            healthy = payload.get("status") == "ready" and payload.get("commit") == expected
        except (URLError, TimeoutError, json.JSONDecodeError, OSError):
            healthy = False
        if healthy or time.monotonic() >= deadline:
            break
        time.sleep(2)
    results.append({"check": "deployed_commit", "passed": healthy})
    if not healthy:
        return results
    # Synthetic negative test data exercises real application storage reads without creating rows.
    absent = f"/{uuid.uuid4()}/invoices/1"
    results.append({"check": "missing_invoice", "passed": client.get(absent + "?token=invalid-test-token")[0] == 404})
    results.append({"check": "missing_link_token", "passed": client.get(absent)[0] == 404})
    supplied = [bool(fixture.get(key)) for key in ("biller", "invoice", "token")]
    if any(supplied):
        if not all(supplied):
            raise ValueError("The designated invoice fixture requires biller, invoice and token together")
        path = f"/{fixture['biller']}/invoices/{fixture['invoice']}"
        status, content_type, body = client.get(path + "?" + urlencode({"token": fixture["token"]}))
        results.append({"check": "existing_test_invoice_html", "passed": status == 200 and content_type == "text/html" and bool(body.strip())})
        results.append({"check": "existing_test_invoice_wrong_token", "passed": client.get(path + "?token=invalid-test-token")[0] == 403})
    return results


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--require-fixture", action="store_true")
    args = parser.parse_args()
    report = {"commit": os.environ.get("READONLY_EXPECTED_COMMIT", ""), "checks": [], "success": False}
    try:
        fixture = {"biller": os.environ.get("READONLY_INVOICE_BILLER_ID"), "invoice": os.environ.get("READONLY_INVOICE_ID"), "token": os.environ.get("READONLY_INVOICE_TOKEN")}
        report["existing_fixture_configured"] = all(fixture.values())
        report["checks"] = checks(ReadOnlyClient(os.environ["READONLY_API_URL"]), report["commit"], fixture, require_fixture=args.require_fixture)
        report["success"] = all(item["passed"] for item in report["checks"])
    except Exception as error:
        report["error_type"] = type(error).__name__  # Never print fixture/token/response values.
    target = Path(args.report)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, indent=2) + "\n")
    for result in report["checks"]:
        print(f"{'PASS' if result['passed'] else 'FAIL'} {result['check']}")
    if not report.get("existing_fixture_configured"):
        print("No existing invoice fixture configured; coverage is health and negative read-only invoice paths.")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
