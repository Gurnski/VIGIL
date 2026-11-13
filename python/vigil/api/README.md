# api

The FastAPI service. It serves stored findings to the desktop app and the AI Detective, and runs a fresh analysis when a user asks for one. It hands out results and never changes them.

The calls it offers are drafted in section 07 of `Project_Brief.pdf`. Their JSON follows the format in `contract/`.
