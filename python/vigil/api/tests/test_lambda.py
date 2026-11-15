"""The service as AWS Lambda runs it: through ``lambda_handler.handler``, one event per request."""

from __future__ import annotations

import importlib
import json
import sys

from vigil.api.app import FIXTURE, Results


def event(path: str) -> dict:
    """A GET as a Lambda function URL hands it over."""
    return {
        "version": "2.0", "routeKey": "$default", "rawPath": path, "rawQueryString": "",
        "headers": {"host": "example.lambda-url.eu-north-1.on.aws", "accept": "application/json"},
        "requestContext": {
            "accountId": "anonymous", "apiId": "example", "domainName": "example.lambda-url.eu-north-1.on.aws",
            "http": {"method": "GET", "path": path, "protocol": "HTTP/1.1", "sourceIp": "203.0.113.9", "userAgent": "test"},
            "requestId": "1", "routeKey": "$default", "stage": "$default",
        },
        "isBase64Encoded": False,
    }


def test_the_scan_is_read_once_however_many_requests_arrive(monkeypatch):
    """Mangum starts the app for every request. Reading the scan each time made every call slow."""
    monkeypatch.setenv("VIGIL_SNAPSHOT", str(FIXTURE))
    monkeypatch.delenv("VIGIL_SCAN_DATA", raising=False)
    reads = []
    read = Results.from_environment.__func__

    def counted(cls):
        reads.append(1)
        return read(cls)

    monkeypatch.setattr(Results, "from_environment", classmethod(counted))
    # Loaded as a starting copy of the function loads it: once, from nothing.
    monkeypatch.delitem(sys.modules, "vigil.api.lambda_handler", raising=False)
    on_lambda = importlib.import_module("vigil.api.lambda_handler")
    for _ in range(3):
        reply = on_lambda.handler(event("/health"), None)
        assert reply["statusCode"] == 200
        assert json.loads(reply["body"])["status"] == "ok"
    assert len(reads) == 1
