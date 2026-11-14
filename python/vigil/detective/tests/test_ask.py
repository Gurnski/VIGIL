"""Checks the AI Detective without calling the language model.

Run from the ``python/`` folder:  python -m pytest vigil/detective/tests -v

A fake client stands in for the Anthropic API, so these tests need no key and
cost nothing. They use the contract's own fixture, so they also show that the
Detective works against the real finding format.
"""

import copy
import json
from pathlib import Path
from types import SimpleNamespace

import anthropic
import pytest

from vigil.detective.ask import FALLBACK_BETA, ask, select_findings
from vigil.detective.grounding import check_answer
from vigil.detective.prompt import RESPONSE_SCHEMA, SYSTEM_PROMPT

FIXTURE = Path(__file__).resolve().parents[4] / "contract" / "fixture"


@pytest.fixture(scope="module")
def snapshot():
    return json.loads((FIXTURE / "snapshot.json").read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def examples():
    return json.loads((FIXTURE / "ask.json").read_text(encoding="utf-8"))["examples"]


class FakeClient:
    """Returns a canned reply and records how it was called."""

    def __init__(self, reply=None, stop_reason="end_turn", error=None):
        self.reply, self.stop_reason, self.error = reply, stop_reason, error
        self.calls = []
        self.messages = SimpleNamespace(create=lambda **kw: self._create("messages", kw))
        self.beta = SimpleNamespace(messages=SimpleNamespace(create=lambda **kw: self._create("beta", kw)))

    def _create(self, endpoint, kwargs):
        self.calls.append((endpoint, kwargs))
        if self.error:
            raise self.error
        text = self.reply if isinstance(self.reply, str) else json.dumps(self.reply)
        return SimpleNamespace(
            stop_reason=self.stop_reason,
            stop_details=None,
            model="claude-sonnet-5-5",
            content=[SimpleNamespace(type="text", text=text)],
        )


def reply_from(example):
    # Copy, so a test that edits its reply cannot change the shared fixture.
    r = copy.deepcopy(example["response"])
    return {k: r[k] for k in ("status", "answer", "citations", "limitations")}


# ---------------------------------------------------------------- the grounding check


def test_dans_four_example_answers_all_pass_the_check(snapshot, examples):
    """The contract's own example answers are the model of a good answer: they must pass."""
    for example in examples:
        result = check_answer(reply_from(example), snapshot["findings"], snapshot["run"])
        assert result.ok, (example["request"]["question"], result.problems)


def test_an_invented_number_is_caught(snapshot, examples):
    reply = reply_from(examples[0])
    reply["answer"] = reply["answer"].replace("0.72 K per decade", "0.81 K per decade")
    result = check_answer(reply, snapshot["findings"], snapshot["run"])
    assert not result.ok
    assert any("0.81" in p for p in result.problems)


def test_a_calculated_number_is_caught(snapshot, examples):
    reply = reply_from(examples[0])
    reply["answer"] += " That is 7.2 K over a century."
    assert not check_answer(reply, snapshot["findings"], snapshot["run"]).ok


def test_a_finding_that_was_not_provided_is_caught(snapshot, examples):
    reply = reply_from(examples[0])
    reply["citations"].append({"finding_id": "trend.temperature.r00c000", "supports": "made up"})
    result = check_answer(reply, snapshot["findings"], snapshot["run"])
    assert not result.ok
    assert any("r00c000" in p for p in result.problems)


def test_an_answered_reply_must_cite_something(snapshot, examples):
    reply = reply_from(examples[0])
    reply["citations"] = []
    result = check_answer(reply, snapshot["findings"], snapshot["run"])
    assert not result.ok
    assert any("no finding is cited" in p for p in result.problems)


def test_numbers_from_an_uncited_finding_do_not_count(snapshot, examples):
    """A number must come from a finding the answer cites, not just any finding."""
    reply = reply_from(examples[0])
    other = next(f for f in snapshot["findings"] if f["id"] != reply["citations"][0]["finding_id"] and f["kind"] == "trend")
    slope = other["result"]["slope_per_decade"]
    reply["answer"] += f" Elsewhere the slope is {slope:.2f}."
    assert not check_answer(reply, snapshot["findings"], snapshot["run"]).ok


def test_rounding_is_accepted_but_whole_numbers_must_match_exactly(snapshot):
    finding = {"id": "trend.x.r01c01", "result": {"slope_per_decade": 0.7213, "lag": 2.4}}
    ok = {"status": "answered", "answer": "The slope is 0.72, or 0.7213.", "citations": [{"finding_id": finding["id"]}]}
    assert check_answer(ok, [finding]).ok
    bad = dict(ok, answer="The lag is 2 months.")
    assert not check_answer(bad, [finding]).ok


def test_signs_are_ignored_and_percentages_match_fractions():
    finding = {"id": "trend.x.r01c01", "slope": -0.22, "ci_level": 0.95}
    reply = {
        "status": "answered",
        "answer": "A cooling of 0.22 with a 95% interval.",
        "citations": [{"finding_id": finding["id"]}],
    }
    assert check_answer(reply, [finding]).ok


# ---------------------------------------------------------------- choosing findings


def test_the_open_finding_and_its_links_are_always_shown(snapshot):
    findings = snapshot["findings"]
    chosen = select_findings(findings, finding_id=findings[-1]["id"], limit=2)
    ids = {f["id"] for f in chosen}
    assert findings[-1]["id"] in ids
    assert {link["finding_id"] for link in findings[-1]["links"]} <= ids
    assert [f["rank"] for f in chosen] == sorted(f["rank"] for f in chosen)


def test_only_the_top_findings_are_shown_by_default(snapshot):
    assert len(select_findings(snapshot["findings"], limit=3)) == 3


# ---------------------------------------------------------------- the whole call


def test_a_good_answer_passes_through_in_the_contract_shape(snapshot, examples):
    client = FakeClient(reply_from(examples[0]))
    response = ask(
        examples[0]["request"]["question"], snapshot["findings"], snapshot["run"], client=client
    )
    assert response["status"] == "answered"
    assert response["contract_version"] == "0.1"
    assert response["run_id"] == snapshot["run"]["id"]
    assert response["citations"][0]["finding_id"] == "trend.temperature.r60c112"
    assert response["model"] == "claude-sonnet-5-5"


def test_the_request_carries_the_rules_the_schema_and_the_findings(snapshot, examples):
    client = FakeClient(reply_from(examples[0]))
    ask("Where is it warming fastest?", snapshot["findings"], snapshot["run"], client=client)

    endpoint, kwargs = client.calls[0]
    assert endpoint == "beta"
    assert kwargs["model"] == "claude-sonnet-5-5"
    assert kwargs["betas"] == [FALLBACK_BETA] and kwargs["fallbacks"] == "default"
    assert kwargs["system"] == SYSTEM_PROMPT
    assert kwargs["output_config"]["format"]["schema"] == RESPONSE_SCHEMA
    assert "tool_choice" not in kwargs and "temperature" not in kwargs
    assert kwargs["messages"][-1]["role"] == "user"
    assert "trend.temperature.r60c112" in kwargs["messages"][-1]["content"]
    assert "Where is it warming fastest?" in kwargs["messages"][-1]["content"]


def test_the_four_rules_are_in_the_system_prompt():
    for phrase in ("only from the findings", "Quote numbers exactly", "which finding each claim", "inconclusive"):
        assert phrase in SYSTEM_PROMPT


def test_fallbacks_can_be_switched_off(snapshot, examples):
    client = FakeClient(reply_from(examples[0]))
    ask("q", snapshot["findings"], snapshot["run"], client=client, use_fallbacks=False)
    endpoint, kwargs = client.calls[0]
    assert endpoint == "messages" and "fallbacks" not in kwargs and "betas" not in kwargs


def test_history_is_sent_as_earlier_turns(snapshot, examples):
    request = examples[1]["request"]
    client = FakeClient(reply_from(examples[1]))
    ask(request["question"], snapshot["findings"], snapshot["run"], request["history"], request["finding_id"], client=client)
    messages = client.calls[0][1]["messages"]
    assert [m["role"] for m in messages] == ["user", "assistant", "user"]
    assert messages[0]["content"] == "Where is it warming fastest?"


def test_an_ungrounded_answer_is_replaced_not_shown(snapshot, examples):
    bad = reply_from(examples[0])
    bad["answer"] = "Temperature in cell r60c112 rose 0.99 K per decade."
    response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient(bad))
    assert response["status"] == "inconclusive"
    assert "0.99" not in response["answer"]
    assert response["citations"] == []


def test_an_inconclusive_answer_is_passed_on_when_it_is_grounded(snapshot, examples):
    request = examples[2]["request"]
    response = ask(request["question"], snapshot["findings"], snapshot["run"], client=FakeClient(reply_from(examples[2])))
    assert response["status"] == "inconclusive"
    assert "0.160" in response["answer"]


def test_out_of_scope_needs_no_citations(snapshot, examples):
    response = ask("Weather in Plymouth?", snapshot["findings"], snapshot["run"], client=FakeClient(reply_from(examples[3])))
    assert response["status"] == "out_of_scope" and response["citations"] == []


@pytest.mark.parametrize("reply", ["not json at all", "{}"])
def test_a_malformed_reply_is_replaced(snapshot, reply):
    response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient(reply))
    assert response["status"] == "inconclusive" and response["citations"] == []


def test_a_refusal_gives_a_plain_message(snapshot):
    response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient({}, stop_reason="refusal"))
    assert response["status"] == "inconclusive"
    assert "declined" in response["answer"]


def test_a_cut_off_answer_is_not_shown(snapshot):
    response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient({}, stop_reason="max_tokens"))
    assert "cut off" in response["answer"]


def test_an_api_error_gives_a_plain_message(snapshot):
    error = anthropic.APIConnectionError(request=SimpleNamespace(method="POST", url="https://api.anthropic.com"))
    response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient(error=error))
    assert "could not be reached" in response["answer"]


def test_the_response_validates_against_the_contract_schema(snapshot, examples):
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads((FIXTURE.parent / "schema.json").read_text(encoding="utf-8"))
    ask_schema = {"$ref": "#/$defs/ask_response", "$defs": schema["$defs"]}
    for example in examples:
        response = ask("q", snapshot["findings"], snapshot["run"], client=FakeClient(reply_from(example)))
        jsonschema.validate(response, ask_schema)
    # A replaced answer must validate too, including when there is no run id.
    jsonschema.validate(ask("q", snapshot["findings"], None, client=FakeClient("not json")), ask_schema)
