# detective

The AI Detective. It turns a question into calls on the API and answers only from what those calls return, naming the findings it used. When the results cannot settle a question, it says so.

It never computes or alters a statistic itself.

## How it works

`ask(question, findings, run, history, finding_id)` is the one call the service makes for `POST /ask`.

1. The model is shown the top 10 findings, the finding the user has open (with the ones it links to) and a summary of the run.
2. For anything else it calls tools (`tools.py`): `search_findings` (by variable, kind, direction, cell or a latitude and longitude box, sorted by rank, size of change or p-value), `get_finding` and `get_run_summary`. At most 4 rounds of tool calls, then it must answer. The tools only read; they never compute a statistic.
3. Its reply is checked by `grounding.check_answer` against every finding it was shown or looked up. Every number in the answer must be in a cited finding or in the run record. A reply that fails is replaced by a plain message.

`use_tools=False` is the older single call with the top 30 findings pasted in. It is also what runs if the API refuses a request that carries tools.

`python -m vigil.detective.live_check` asks the real model the contract's four examples and four questions that need the tools, and prints each tool call. It needs `ANTHROPIC_API_KEY` in that terminal and spends a few pence. It is the only check of the live API: the tests use a stand-in for the model.
