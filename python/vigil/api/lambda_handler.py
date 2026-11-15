"""Runs the service on AWS Lambda, behind a function URL or API Gateway.

Set the function's handler to ``vigil.api.lambda_handler.handler``. Lambda has no file of
results of its own, so point ``VIGIL_SNAPSHOT`` at the stored scan, for example
``s3://keepvigil.earth/snapshot.json``, and ``VIGIL_SCAN_DATA`` at the gridded data the same
way. The settings are described in ``vigil/api/app.py``.

The results are read here, once, when a copy of the function starts. Mangum starts the app
afresh for every request, and an app left to read its own results would fetch and unpack the
whole scan each time. Handed results that are already loaded, that start costs nothing.
"""

from mangum import Mangum

from vigil.api.app import Results, create_app

handler = Mangum(create_app(Results.from_environment()))
