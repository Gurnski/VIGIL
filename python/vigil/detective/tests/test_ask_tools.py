"""Checks the Detective's tool loop with a scripted stand-in for the model.

Run from the ``python/`` folder:  python -m pytest vigil/detective/tests/test_ask_tools.py -v

The script says what the "model" does in each round: call tools, or give its
final answer. Nothing here calls the real API. What the real API does with a
request that carries tools can only be checked with ``live_check``.
"""

import copy
import json
from pathlib import Path
from types import SimpleNamespace

import anthropic
import pytest

from vigil.detective import ask as ask_module
from vigil.detective.ask import MAX_TOOL_ROUNDS, SHOWN_WITH_TOOLS, ask
from vigil.detective.tools import TOOLS

FIXTURE = Path(__file__).resolve().parents[4] / "contract" / "fixture"


@pytest.fixture(scope="module")
def snapshot():
    return json.loads((FIXTURE / "snapshot.json").read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def examples():
    return json.loads((FIXTURE / "ask.json").read_text(encoding="utf-8"))["examples"]


def tool(name, **arguments):
    return ("tool", name, arguments)


def final(reply):
    return ("final", reply)


class ScriptedClient:
    """Plays a script, one step per request, and keeps a snapshot of every request it was sent."""

    def __init__(self, steps, error=None):
        self.steps = list(steps)
        self.error = error
        self.calls = []
        self.messages = SimpleNamespace(create=lambda **kw: self._create(kw))
        self.beta = SimpleNamespace(messages=SimpleNamespace(create=lambda **kw: self._create(kw)))

    def _create(self, kwargs):
        self.calls.append(copy.deepcopy(kwargs))
        if self.error is not None and len(self.calls) == 1:
            raise self.error
        step = self.steps.pop(0) if self.steps else final("")
        if step[0] == "tool":
            _, name, arguments = step
            block = SimpleNamespace(type="tool_use", id=f"toolu_{len(self.calls)}", name=name, input=arguments)
            return SimpleNamespace(stop_reason="tool_use", model="claude-sonnet-5-5", content=[block], stop_details=None)
        text = step[1] if isinstance(step[1], str) else json.dumps(step[1])
        return SimpleNamespace(stop_reason="end_turn", model="claude-sonnet-5-5", stop_details=None,
                               content=[SimpleNamespace(type="text", text=text)])


def reply_from(example):
    r = copy.deepcopy(example["response"])
    return {k: r[k] for k in ("status", "answer", "citations", "limitations")}


def question_and_answer(snapshot, example, steps):
    request = example["request"]
    client = ScriptedClient(steps)
    response = ask(request["question"], snapshot["findings"], snapshot["run"], request.get("history"),
                   request.get("finding_id"), client=client)
    return response, client


# ------------------------------------------------------------------ the first request

def test_the_first_request_offers_the_tools_and_shows_only_the_top_findings(snapshot, examples):
    _, client = question_and_answer(snapshot, examples[0], [final(reply_from(examples[0]))])
    request = client.calls[0]
    assert request["tools"] == TOOLS
    assert "tool_choice" not in request
    shown = json.loads(request["messages"][-1]["content"].split("\n", 1)[1].rsplit("\n\nQuestion:", 1)[0])["findings"]
    assert len(shown) == SHOWN_WITH_TOOLS
    assert [f["rank"] for f in shown] == sorted(f["rank"] for f in shown)


def test_the_system_prompt_tells_the_model_about_the_tools():
    from vigil.detective.prompt import SYSTEM_PROMPT

    for name in ("search_findings", "get_finding", "get_run_summary", "abs_slope"):
        assert name in SYSTEM_PROMPT


def test_without_tools_the_old_single_call_is_used(snapshot, examples):
    client = ScriptedClient([final(reply_from(examples[0]))])
    ask("q", snapshot["findings"], snapshot["run"], client=client, use_tools=False)
    assert len(client.calls) == 1 and "tools" not in client.calls[0]
    shown = json.loads(client.calls[0]["messages"][-1]["content"].split("\n", 1)[1].rsplit("\n\nQuestion:", 1)[0])["findings"]
    assert len(shown) == len(snapshot["findings"])        # the fixture has fewer than 30


# ------------------------------------------------------------------ a round of tool calls

def test_a_finding_the_model_looked_up_can_be_cited(snapshot, examples):
    # Example 3 cites a finding that is not significant, so it is not in the top 10 shown first.
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    response, client = question_and_answer(
        snapshot, examples[2],
        [tool("get_finding", id=cited), final(reply_from(examples[2]))],
    )
    assert response["status"] == "inconclusive" and "0.160" in response["answer"]
    assert response["citations"][0]["finding_id"] == cited
    assert len(client.calls) == 2


def test_the_same_answer_without_the_lookup_is_not_shown(snapshot, examples):
    response, _ = question_and_answer(snapshot, examples[2], [final(reply_from(examples[2]))])
    assert "0.160" not in response["answer"] and response["citations"] == []
    assert "could not back" in response["answer"]


def test_tool_results_go_back_to_the_model_in_the_api_format(snapshot, examples):
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    _, client = question_and_answer(snapshot, examples[2], [tool("get_finding", id=cited), final(reply_from(examples[2]))])
    second = client.calls[1]["messages"]
    assert [m["role"] for m in second][-3:] == ["user", "assistant", "user"]
    assistant, results = second[-2]["content"], second[-1]["content"]
    assert assistant == [{"type": "tool_use", "id": "toolu_1", "name": "get_finding", "input": {"id": cited}}]
    assert results[0]["type"] == "tool_result" and results[0]["tool_use_id"] == "toolu_1"
    assert "is_error" not in results[0]
    assert json.loads(results[0]["content"])["finding"]["id"] == cited


def test_a_search_makes_every_returned_finding_citable(snapshot, examples):
    response, _ = question_and_answer(
        snapshot, examples[2],
        [tool("search_findings", cell_id="r51c032", significant=False), final(reply_from(examples[2]))],
    )
    assert response["citations"][0]["finding_id"] == "trend.temperature.r51c032"


def test_a_number_from_a_finding_that_was_only_searched_for_but_not_cited_does_not_count(snapshot, examples):
    reply = reply_from(examples[2])
    reply["citations"] = []
    reply["status"] = "inconclusive"
    response, _ = question_and_answer(snapshot, examples[2], [tool("search_findings", cell_id="r51c032", significant=False), final(reply)])
    assert "could not back" in response["answer"]


def test_a_bad_tool_call_is_an_error_result_and_the_loop_carries_on(snapshot, examples):
    response, client = question_and_answer(
        snapshot, examples[3], [tool("search_findings", variable="methane"), final(reply_from(examples[3]))]
    )
    result = client.calls[1]["messages"][-1]["content"][0]
    assert result["is_error"] is True and "not a variable" in result["content"]
    assert response["status"] == "out_of_scope"


def test_findings_are_not_added_to_what_the_model_may_cite_by_a_failed_lookup(snapshot, examples):
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    response, _ = question_and_answer(snapshot, examples[2], [tool("get_finding", id="trend.nothing.r00c000"), final(reply_from(examples[2]))])
    assert cited not in [c["finding_id"] for c in response["citations"]]


# ------------------------------------------------------------------ limits

def test_the_model_cannot_keep_calling_tools_for_ever(snapshot, examples):
    endless = [tool("get_run_summary")] * (MAX_TOOL_ROUNDS + 5)
    response, client = question_and_answer(snapshot, examples[0], endless)
    assert len(client.calls) == MAX_TOOL_ROUNDS + 1
    assert client.calls[-1]["tool_choice"] == {"type": "none"}
    assert all("tool_choice" not in c for c in client.calls[:-1])
    assert response["status"] == "inconclusive" and response["citations"] == []


def test_the_last_round_is_a_real_answer_when_the_model_stops_in_time(snapshot, examples):
    steps = [tool("get_run_summary")] * MAX_TOOL_ROUNDS + [final(reply_from(examples[0]))]
    response, client = question_and_answer(snapshot, examples[0], steps)
    assert response["status"] == "answered" and len(client.calls) == MAX_TOOL_ROUNDS + 1


def test_a_slow_answer_gives_up_before_the_app_does(snapshot, examples, monkeypatch):
    ticks = iter([0.0])                                  # the start; every later look at the clock says 100 s
    monkeypatch.setattr(ask_module.time, "monotonic", lambda: next(ticks, 100.0))
    response, client = question_and_answer(snapshot, examples[0], [tool("get_run_summary"), final(reply_from(examples[0]))])
    assert "too long" in response["answer"] and len(client.calls) == 1


# ------------------------------------------------------------------ when the API says no

def bad_request():
    request = SimpleNamespace(method="POST", url="https://api.anthropic.com/v1/messages")
    response = SimpleNamespace(status_code=400, headers={}, request=request)
    return anthropic.BadRequestError("tools are not allowed here", response=response, body=None)


def test_if_the_api_refuses_tools_the_question_is_answered_without_them(snapshot, examples):
    client = ScriptedClient([final(reply_from(examples[0]))], error=bad_request())
    response = ask("Where is it warming fastest?", snapshot["findings"], snapshot["run"], client=client)
    assert response["status"] == "answered"
    assert "tools" in client.calls[0] and "tools" not in client.calls[1]


def test_a_refusal_of_a_request_without_tools_is_just_an_error(snapshot, examples):
    client = ScriptedClient([], error=bad_request())
    response = ask("q", snapshot["findings"], snapshot["run"], client=client, use_tools=False)
    assert "could not be reached" in response["answer"] and len(client.calls) == 1


def test_a_connection_error_in_a_later_round_is_a_plain_message(snapshot, examples):
    error = anthropic.APIConnectionError(request=SimpleNamespace(method="POST", url="https://api.anthropic.com"))

    class Flaky(ScriptedClient):
        def _create(self, kwargs):
            if self.calls:
                raise error
            return super()._create(kwargs)

    client = Flaky([tool("get_run_summary")])
    response = ask("q", snapshot["findings"], snapshot["run"], client=client)
    assert "could not be reached" in response["answer"]


def test_the_answer_still_validates_against_the_contract(snapshot, examples):
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads((FIXTURE.parent / "schema.json").read_text(encoding="utf-8"))
    ask_schema = {"$ref": "#/$defs/ask_response", "$defs": schema["$defs"]}
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    response, _ = question_and_answer(snapshot, examples[2], [tool("get_finding", id=cited), final(reply_from(examples[2]))])
    jsonschema.validate(response, ask_schema)


# ------------------------------------------------------------------ not asking twice

def results_of(client, call_index):
    """The tool results the model was sent before request number ``call_index`` (0-based)."""
    return client.calls[call_index]["messages"][-1]["content"]


def test_the_same_search_twice_is_answered_with_a_pointer_not_the_data_again(snapshot, examples):
    search = dict(cell_id="r51c032", significant=False)
    response, client = question_and_answer(
        snapshot, examples[2], [tool("search_findings", **search), tool("search_findings", **search), final(reply_from(examples[2]))],
    )
    first, second = results_of(client, 1)[0], results_of(client, 2)[0]
    assert "findings" in json.loads(first["content"])
    assert json.loads(second["content"]) == {"note": ask_module.REPEATED_CALL}
    assert "is_error" not in second
    # What the first search returned is still citable.
    assert response["citations"][0]["finding_id"] == "trend.temperature.r51c032"


def test_the_same_call_with_the_options_in_another_order_is_still_a_repeat(snapshot, examples):
    response, client = question_and_answer(
        snapshot, examples[2],
        [tool("search_findings", cell_id="r51c032", significant=False),
         tool("search_findings", significant=False, cell_id="r51c032"), final(reply_from(examples[2]))],
    )
    assert json.loads(results_of(client, 2)[0]["content"]) == {"note": ask_module.REPEATED_CALL}


def test_a_narrower_search_after_a_complete_one_is_covered_by_it(snapshot, examples):
    _, client = question_and_answer(
        snapshot, examples[2],
        [tool("search_findings", cell_id="r51c032", significant=False),
         tool("search_findings", cell_id="r51c032", significant=False, limit=1), final(reply_from(examples[2]))],
    )
    assert json.loads(results_of(client, 2)[0]["content"]) == {"note": ask_module.COVERED_SEARCH}


def test_a_smaller_limit_is_covered_but_a_larger_one_runs(snapshot, examples):
    filters = dict(kind="trend")                               # more than 3 matches, so the first search is not complete
    _, client = question_and_answer(
        snapshot, examples[0],
        [tool("search_findings", limit=3, **filters), tool("search_findings", limit=2, **filters),
         tool("search_findings", limit=6, **filters), final(reply_from(examples[0]))],
    )
    assert json.loads(results_of(client, 1)[0]["content"])["complete"] is False
    assert json.loads(results_of(client, 2)[0]["content"]) == {"note": ask_module.COVERED_SEARCH}
    assert len(json.loads(results_of(client, 3)[0]["content"])["findings"]) > 3


def test_different_filters_are_not_covered(snapshot, examples):
    _, client = question_and_answer(
        snapshot, examples[0],
        [tool("search_findings", kind="trend", significant=False), tool("search_findings", kind="cascade", limit=1),
         final(reply_from(examples[0]))],
    )
    assert "findings" in json.loads(results_of(client, 2)[0]["content"])


def test_a_different_search_is_run(snapshot, examples):
    _, client = question_and_answer(
        snapshot, examples[2],
        [tool("search_findings", cell_id="r51c032", significant=False), tool("search_findings", cell_id="r51c032"), final(reply_from(examples[2]))],
    )
    assert "findings" in json.loads(results_of(client, 2)[0]["content"])


def test_asking_for_a_finding_it_already_holds_gets_a_pointer(snapshot, examples):
    top = min(snapshot["findings"], key=lambda f: f["rank"])["id"]
    _, client = question_and_answer(snapshot, examples[0], [tool("get_finding", id=top), final(reply_from(examples[0]))])
    assert json.loads(results_of(client, 1)[0]["content"]) == {"note": ask_module.ALREADY_GIVEN, "id": top}


def test_a_finding_that_a_search_returned_is_not_fetched_again(snapshot, examples):
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    response, client = question_and_answer(
        snapshot, examples[2],
        [tool("search_findings", cell_id="r51c032", significant=False), tool("get_finding", id=cited), final(reply_from(examples[2]))],
    )
    assert json.loads(results_of(client, 2)[0]["content"])["note"] == ask_module.ALREADY_GIVEN
    assert response["citations"][0]["finding_id"] == cited


def test_a_repeated_failed_call_is_a_pointer_too_and_a_malformed_id_does_not_crash(snapshot, examples):
    steps = [tool("search_findings", variable="methane"), tool("search_findings", variable="methane"),
             tool("get_finding", id=["not", "a", "string"]), final(reply_from(examples[3]))]
    response, client = question_and_answer(snapshot, examples[3], steps)
    assert json.loads(results_of(client, 2)[0]["content"]) == {"note": ask_module.REPEATED_CALL}
    assert results_of(client, 3)[0]["is_error"] is True
    assert response["status"] == "out_of_scope"


def test_a_pointer_does_not_use_up_a_lookup_that_has_not_been_made(snapshot, examples):
    cited = examples[2]["response"]["citations"][0]["finding_id"]
    # A pointer for a different finding must not make this one citable.
    top = min(snapshot["findings"], key=lambda f: f["rank"])["id"]
    response, _ = question_and_answer(snapshot, examples[2], [tool("get_finding", id=top), final(reply_from(examples[2]))])
    assert cited not in [c["finding_id"] for c in response["citations"]]


# ------------------------------------------------------------------ wording

def test_the_prompt_asks_for_sizes_as_positive_numbers_and_no_repeated_lookups():
    from vigil.detective.prompt import SYSTEM_PROMPT

    assert "never \"fell by -10.8\"" in SYSTEM_PROMPT
    assert "do not ask for a finding or repeat a search" in SYSTEM_PROMPT
    assert "complete" in SYSTEM_PROMPT


def test_a_size_written_without_its_sign_passes_the_grounding_check(snapshot):
    from vigil.detective.grounding import check_answer

    finding = next(f for f in snapshot["findings"] if f["id"] == "trend.precipitation.r32c051")
    reply = {
        "status": "answered",
        "answer": "Precipitation fell by 10.8 mm per month per decade (interval -15.4 to -6.10, adjusted p = 0.000426).",
        "citations": [{"finding_id": finding["id"], "supports": "the drying trend"}],
        "limitations": [],
    }
    assert check_answer(reply, [finding], snapshot["run"]).ok
