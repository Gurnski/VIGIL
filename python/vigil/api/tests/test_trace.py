"""The record of what the Detective looked up, made from outside it by the client it calls the model through."""

from __future__ import annotations

import copy
import json
from types import SimpleNamespace

import anthropic
import pytest
from fastapi.testclient import TestClient

from vigil.api.app import FIXTURE, Results, create_app
from vigil.api.trace import TracingClient, describe, place_text
from vigil.detective.ask import ask


class Scripted:
    """A stand-in for the model: it plays a script, one step per request, as the Detective's own tests do."""

    def __init__(self, script, error=None):
        self.script = list(script)
        self.error = error
        self.requests = []
        self.messages = SimpleNamespace(create=lambda **kw: self._create(kw))
        self.beta = SimpleNamespace(messages=SimpleNamespace(create=lambda **kw: self._create(kw)))

    def _create(self, request):
        self.requests.append(copy.deepcopy(request))
        if self.error is not None:
            raise self.error
        kind, *rest = self.script.pop(0)
        if kind == "tools":
            blocks = [SimpleNamespace(type="tool_use", id=f"toolu_{len(self.requests)}_{i}", name=name, input=arguments)
                      for i, (name, arguments) in enumerate(rest)]
            return SimpleNamespace(stop_reason="tool_use", model="stand-in", content=blocks, stop_details=None)
        return SimpleNamespace(stop_reason="end_turn", model="stand-in", stop_details=None,
                               content=[SimpleNamespace(type="text", text=json.dumps(rest[0]))])


@pytest.fixture(scope="module")
def snapshot() -> dict:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


def answer_citing(finding: dict) -> dict:
    return {"status": "answered", "answer": "It is listed in the scan.", "limitations": [],
            "citations": [{"finding_id": finding["id"], "supports": "That it is listed"}]}


def test_each_tool_call_becomes_a_step_with_what_it_returned(snapshot):
    wanted = snapshot["findings"][12]
    cell = wanted["cell"]
    box = {"lat_min": cell["lat_min"], "lat_max": cell["lat_max"], "lon_min": cell["lon_min"], "lon_max": cell["lon_max"]}
    script = [
        ("tools", ("search_findings", {**box, "include_not_significant": True, "limit": 10})),
        ("tools", ("get_run_summary", {}), ("get_finding", {"id": snapshot["findings"][13]["id"]})),
        ("final", answer_citing(wanted)),
    ]
    tracer = TracingClient(Scripted(script))
    response = ask("What is at this place?", snapshot["findings"], snapshot["run"], client=tracer)
    assert response["status"] == "answered"

    search, summary, lookup = tracer.steps
    assert search["tool"] == "search_findings" and search["box"] == box
    assert wanted["id"] in search["finding_ids"] and search["matching"] >= len(search["finding_ids"]) >= 1
    assert search["summary"].startswith("Searched for findings in ") and "including those that did not pass" in search["summary"]
    assert summary == {"tool": "get_run_summary", "input": {}, "summary": "Read what the scan covered", "finding_ids": []}
    assert lookup["finding_ids"] == [snapshot["findings"][13]["id"]] and lookup["summary"].endswith(snapshot["findings"][13]["id"])
    json.dumps(tracer.steps)


def test_the_model_is_sent_and_returns_exactly_what_it_would_without_the_trace(snapshot):
    wanted = snapshot["findings"][12]
    script = [("tools", ("search_findings", {"variable": "precipitation"})), ("final", answer_citing(wanted))]
    plain, traced = Scripted(copy.deepcopy(script)), Scripted(copy.deepcopy(script))
    without = ask("Where is rain changing?", snapshot["findings"], snapshot["run"], client=plain)
    with_trace = ask("Where is rain changing?", snapshot["findings"], snapshot["run"], client=TracingClient(traced))
    assert with_trace == without

    def sent(requests):
        return [{key: value for key, value in request.items() if key != "timeout"} for request in requests]

    assert sent(traced.requests) == sent(plain.requests)


def test_a_call_the_detective_did_not_run_again_is_not_a_step(snapshot):
    top = snapshot["findings"][0]
    script = [
        ("tools", ("search_findings", {"variable": "precipitation"})),
        ("tools", ("search_findings", {"variable": "precipitation"}), ("get_finding", {"id": top["id"]})),
        ("final", answer_citing(top)),
    ]
    tracer = TracingClient(Scripted(script))
    ask("Where is rain changing?", snapshot["findings"], snapshot["run"], client=tracer)
    # The repeat, and the finding it was shown at the start, were answered with a pointer, not looked up.
    assert [step["tool"] for step in tracer.steps] == ["search_findings"]


def test_a_search_with_more_matches_than_it_returned_is_still_a_step(snapshot):
    # The Detective adds a note to such a result, telling the model not to quote the count. It is not a repeat.
    script = [("tools", ("search_findings", {"include_not_significant": True, "limit": 3})), ("final", answer_citing(snapshot["findings"][0]))]
    tracer = TracingClient(Scripted(script))
    ask("What is there?", snapshot["findings"], snapshot["run"], client=tracer)
    (step,) = tracer.steps
    assert len(step["finding_ids"]) == 3 and step["matching"] == len(snapshot["findings"]) > 3


def test_a_tool_call_that_failed_is_marked(snapshot):
    script = [("tools", ("get_finding", {"id": "no.such.finding"})), ("final", answer_citing(snapshot["findings"][0]))]
    tracer = TracingClient(Scripted(script))
    ask("What is that?", snapshot["findings"], snapshot["run"], client=tracer)
    assert tracer.steps[0]["failed"] is True and tracer.steps[0]["finding_ids"] == []


def test_an_error_from_the_model_passes_straight_through(snapshot):
    error = anthropic.APITimeoutError(request=SimpleNamespace())
    tracer = TracingClient(Scripted([], error=error))
    with pytest.raises(anthropic.APITimeoutError):
        tracer.beta.messages.create(messages=[{"role": "user", "content": "x"}])
    with pytest.raises(anthropic.APITimeoutError):
        tracer.messages.create(messages=[{"role": "user", "content": "x"}])
    # And the Detective turns it into its own plain message, as it does without the trace.
    response = ask("Anything?", snapshot["findings"], snapshot["run"], client=TracingClient(Scripted([], error=error)))
    assert response["status"] == "inconclusive" and "too long" in response["answer"]


def test_the_lines_say_what_was_asked_for():
    asked = {"variable": "soil_moisture", "kind": "trend", "direction": "decreasing", "sort_by": "abs_slope"}
    assert describe("search_findings", asked) == "Searched for soil moisture decreasing trends, largest change first"
    assert describe("search_findings", {"kind": "cascade", "cell_id": "r60c112"}) == "Searched for cascades in cell r60c112"
    box = {"lat_min": -10, "lat_max": 0, "lon_min": -70, "lon_max": -50}
    assert describe("search_findings", box) == "Searched for findings in 10°S to 0°N, 70°W to 50°W"
    assert place_text({"lat_min": 60}) == "60°N to the North Pole"
    assert describe("something_new", {}) == "Called something_new"


def test_the_service_returns_the_steps_with_the_answer(snapshot, monkeypatch):
    wanted = snapshot["findings"][12]
    search = {"variable": wanted["variables"][0]["id"], "include_not_significant": True, "limit": 10, "cell_id": wanted["cell"]["id"]}
    script = [("tools", ("search_findings", search)), ("final", answer_citing(wanted))]
    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr(anthropic, "Anthropic", lambda: Scripted(script))
    with TestClient(create_app(Results(snapshot))) as client:
        body = client.post("/ask", json={"question": "What is in that cell?"}).json()
    assert body["status"] == "answered" and body["citations"][0]["finding_id"] == wanted["id"]
    assert [step["tool"] for step in body["steps"]] == ["search_findings"]
    assert wanted["id"] in body["steps"][0]["finding_ids"]
