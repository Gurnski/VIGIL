"""Notes what the AI Detective looked up while answering, so the apps can show its work.

The Detective answers by calling tools: it searches the findings, fetches one, reads the
run summary. Its answer says which findings it used, but not how it got to them. This module
records each tool call as a step, and the service returns the steps with the answer
(``steps`` in the contract's ask response). An app can then list them, and draw on the globe
the place a search covered and the findings it returned.

    client = TracingClient(anthropic.Anthropic())
    answer = ask(question, findings, run, history, finding_id, client=client)
    answer["steps"] = client.steps

It works from outside the Detective. ``ask`` takes the client it should call the model
through, and ``TracingClient`` is that client with a notebook: every call goes to the real
one unchanged, and every reply and error comes back unchanged. It reads two things as they
pass. A reply that asks for a tool gives the tool's name and input. The next request carries
that tool's result, which gives the findings it returned. Nothing the model sees or says is
altered, and a failure to read a step never stops an answer.
"""

from __future__ import annotations

import json
import logging

log = logging.getLogger(__name__)

#: The names a search may filter by, as the Detective's tools define them, and how each reads in a sentence.
KIND_WORDS = {"trend": "trends", "change_point": "change points", "cascade": "cascades", "contradiction": "contradictions"}
SORT_WORDS = {"abs_slope": "largest change first", "p_adjusted": "smallest p-value first"}


def _degrees(value: float, positive: str, negative: str) -> str:
    text = f"{abs(value):.1f}".rstrip("0").rstrip(".")
    return f"{text}°{positive if value >= 0 else negative}"


def place_text(box: dict) -> str:
    """A latitude and longitude box in words, such as "10°S to 0°N, 70°W to 50°W". Open edges are left out."""
    parts = []
    if box.get("lat_min") is not None or box.get("lat_max") is not None:
        south = "the South Pole" if box.get("lat_min") is None else _degrees(box["lat_min"], "N", "S")
        north = "the North Pole" if box.get("lat_max") is None else _degrees(box["lat_max"], "N", "S")
        parts.append(f"{south} to {north}")
    if box.get("lon_min") is not None and box.get("lon_max") is not None:
        parts.append(f"{_degrees(box['lon_min'], 'E', 'W')} to {_degrees(box['lon_max'], 'E', 'W')}")
    return ", ".join(parts)


def describe(tool: str, arguments: dict) -> str:
    """One plain line saying what a tool call asked for. It reports the request, and claims nothing about the answer."""
    if tool == "get_run_summary":
        return "Read what the scan covered"
    if tool == "get_finding":
        return f"Looked up the finding {arguments.get('id', '')}".rstrip()
    if tool != "search_findings":
        return f"Called {tool}"

    what = KIND_WORDS.get(arguments.get("kind"), "findings")
    if arguments.get("direction") in ("increasing", "decreasing"):
        what = f"{arguments['direction']} {what}"
    if arguments.get("variable"):
        what = f"{str(arguments['variable']).replace('_', ' ')} {what}"
    line = f"Searched for {what}"
    if arguments.get("cell_id"):
        line += f" in cell {arguments['cell_id']}"
    place = place_text(arguments)
    if place:
        line += f" in {place}"
    if arguments.get("include_not_significant") is True:
        line += ", including those that did not pass"
    elif arguments.get("significant") is False:
        line += ", only those that did not pass"
    if arguments.get("sort_by") in SORT_WORDS:
        line += f", {SORT_WORDS[arguments['sort_by']]}"
    return line


def _box(arguments: dict) -> dict | None:
    """The box a search named, when it gave all four edges and does not cross the date line. For drawing on the globe."""
    edges = [arguments.get(key) for key in ("lat_min", "lat_max", "lon_min", "lon_max")]
    if any(isinstance(edge, bool) or not isinstance(edge, (int, float)) for edge in edges):
        return None
    lat_min, lat_max, lon_min, lon_max = (float(edge) for edge in edges)
    if not (-90 <= lat_min < lat_max <= 90 and -180 <= lon_min < lon_max <= 180):
        return None
    return {"lat_min": lat_min, "lat_max": lat_max, "lon_min": lon_min, "lon_max": lon_max}


class TracingClient:
    """The client ``ask`` calls the model through, with every tool call noted in ``steps``.

    It has the two things ``ask`` uses, ``messages.create`` and ``beta.messages.create``, and
    passes both straight to the real client.
    """

    def __init__(self, client):
        self.steps: list[dict] = []
        self._waiting: dict[str, dict] = {}          # tool call id -> the step whose result has not come back yet
        self.messages = _Messages(self, lambda: client.messages)
        self.beta = _Beta(self, client)

    def _before(self, request: dict) -> None:
        """Reads the results of the tool calls just made, which ride in the request's last message."""
        try:
            messages = request.get("messages") or []
            content = messages[-1].get("content") if messages and isinstance(messages[-1], dict) else None
            if not isinstance(content, list):
                return
            for block in content:
                if not isinstance(block, dict) or block.get("type") != "tool_result":
                    continue
                step = self._waiting.pop(block.get("tool_use_id"), None)
                if step is None:
                    continue
                result = json.loads(block["content"]) if isinstance(block.get("content"), str) else {}
                if block.get("is_error") or "error" in result:
                    step["failed"] = True
                elif "note" in result and not any(key in result for key in ("findings", "finding", "run", "matching")):
                    # A note and nothing else: the Detective pointed the model back at a result it already
                    # holds, so nothing new was looked up. (A search that returned findings can carry a note too.)
                    self.steps.remove(step)
                    continue
                found = [*result.get("findings", []), result.get("finding")]
                step["finding_ids"] = [finding["id"] for finding in found if isinstance(finding, dict) and "id" in finding]
                if isinstance(result.get("matching"), int):
                    step["matching"] = result["matching"]
        except Exception:                                 # a step that cannot be read is dropped, never the answer
            log.exception("could not read a tool result for the trace")

    def _after(self, response) -> None:
        """Notes the tools a reply asks for."""
        try:
            for block in getattr(response, "content", None) or []:
                if getattr(block, "type", None) != "tool_use":
                    continue
                arguments = block.input if isinstance(block.input, dict) else {}
                step = {"tool": block.name, "input": arguments, "summary": describe(block.name, arguments), "finding_ids": []}
                box = _box(arguments) if block.name == "search_findings" else None
                if box is not None:
                    step["box"] = box
                self.steps.append(step)
                self._waiting[block.id] = step
        except Exception:
            log.exception("could not read a tool call for the trace")


class _Messages:
    def __init__(self, tracer: TracingClient, real):
        self._tracer = tracer
        self._real = real                                  # a callable: the real ``messages`` is looked up when used

    def create(self, **request):
        self._tracer._before(request)
        response = self._real().create(**request)          # any error passes straight through to ask()
        self._tracer._after(response)
        return response


class _Beta:
    def __init__(self, tracer: TracingClient, client):
        self.messages = _Messages(tracer, lambda: client.beta.messages)
