# detective

The AI Detective. It turns a question into calls on the API and answers only from what those calls return, naming the findings it used. When the results cannot settle a question, it says so.

It never computes or alters a statistic itself.

## How it works

`ask(question, findings, run, history, finding_id)` is the one call the service makes for `POST /ask`.

1. The model is shown the top 10 findings, the finding the user has open (with the ones it links to) and a summary of the run.
2. For anything else it calls tools (`tools.py`): `search_findings` (by variable, kind, direction, cell or a latitude and longitude box, sorted by rank, size of change or p-value; only significant findings unless `significant: false` or `include_not_significant: true` is given, and the Detective is told to use the latter for any question about a place), `get_finding` and `get_run_summary`. At most 4 rounds of tool calls, then it must answer. The tools only read; they never compute a statistic.
3. Its reply is checked by `grounding.check_answer` against every finding it was shown or looked up. Every number in the answer must be in a cited finding or in the run record. A reply that fails is replaced by a plain message.

`use_tools=False` is the older single call with the top 30 findings pasted in. It is also what runs if the API refuses a request that carries tools.

`python -m vigil.detective.live_check` asks the real model the contract's four examples and four questions that need the tools, and prints each tool call. It needs `ANTHROPIC_API_KEY` in that terminal and spends a few pence. It is the only check of the live API: the tests use a stand-in for the model.

## Time allowed

The whole answer may take `VIGIL_DETECTIVE_DEADLINE` seconds (default 75), and each model call is given only the time that is left, so one slow reply cannot run past it. If time runs out the user gets a plain "That took too long" message, never a half answer. Set the deadline lower than every limit in front of the service: the function's own timeout (it must be higher than this: 90 seconds against 75), and a proxy such as Cloudflare if there is one (100 seconds on the free plan). The desktop app waits 90 seconds.
