import io
from email.message import Message
from unittest.mock import Mock
import unittest
from urllib.error import HTTPError

from test_delivery_tools import load

readonly = load("readonly", "read-only-deployment.py")


class ReadOnlyBoundaryTests(unittest.TestCase):
    def test_non_reads_and_action_routes_never_reach_the_network(self):
        opener = Mock()
        client = readonly.ReadOnlyClient("https://service.example.invalid", opener)
        for method, path in [(method, "/health") for method in ["POST", "PUT", "PATCH", "DELETE", "HEAD"]] + [
            ("GET", "/email/test/invoices/1"), ("GET", "/token"), ("GET", "/rsas"), ("GET", "/invoices"),
            ("GET", "https://other.example.invalid/health"), ("GET", "//other.example.invalid/health"),
            ("GET", "/health?token=secret"), ("GET", "/health#fragment"),
            ("GET", "/11111111-1111-1111-1111-111111111111/invoices/1?delete=true"),
        ]:
            with self.assertRaises(ValueError):
                client.request(method, path)
        opener.open.assert_not_called()

    def test_get_uses_the_fixed_origin_and_never_follows_redirects(self):
        opener = Mock()
        response = io.BytesIO(b'{"status":"ready"}')
        response.status = 200
        response.headers = Message()
        response.headers["Content-Type"] = "application/json"
        opener.open.return_value = response
        client = readonly.ReadOnlyClient("https://service.example.invalid/", opener)
        self.assertEqual(client.get("/health")[0], 200)
        request = opener.open.call_args.args[0]
        self.assertEqual(request.get_method(), "GET")
        self.assertEqual(request.full_url, "https://service.example.invalid/health")
        self.assertIsNone(readonly.NoRedirect().redirect_request(None, None, 302, None, None, "https://other.example.invalid"))

    def test_http_failure_does_not_return_sensitive_body(self):
        opener = Mock()
        opener.open.side_effect = HTTPError("https://example.invalid/?token=secret", 403, "forbidden", {}, io.BytesIO(b"private fixture"))
        self.assertEqual(readonly.ReadOnlyClient("https://service.example.invalid", opener).get("/health"), (403, "", b""))

    def test_production_requires_the_complete_fixture_before_any_network_request(self):
        client = Mock()
        with self.assertRaises(ValueError):
            readonly.checks(client, "a" * 40, {}, require_fixture=True)
        client.get.assert_not_called()

    def test_expected_binary_is_required_before_any_invoice_read(self):
        client = Mock()
        client.get.return_value = (200, "application/json", b'{"status":"ready","commit":"old"}')
        result = readonly.checks(client, "a" * 40, {}, wait_seconds=0)
        self.assertFalse(result[0]["passed"])
        client.get.assert_called_once_with("/health")

    def test_real_fixture_reads_are_optional_but_partial_configuration_fails(self):
        client = Mock()
        client.get.side_effect = [(200, "application/json", b'{"status":"ready","commit":"' + b"a" * 40 + b'"}'), (404, "", b""), (404, "", b""), (200, "text/html", b"invoice"), (403, "", b"")]
        result = readonly.checks(client, "a" * 40, {"biller": "11111111-1111-1111-1111-111111111111", "invoice": "1", "token": "private"})
        self.assertEqual(len(result), 5)
        self.assertTrue(all(item["passed"] for item in result))
        client.get.side_effect = [(200, "application/json", b'{"status":"ready","commit":"' + b"a" * 40 + b'"}'), (404, "", b""), (404, "", b"")]
        with self.assertRaises(ValueError):
            readonly.checks(client, "a" * 40, {"biller": "one"})
