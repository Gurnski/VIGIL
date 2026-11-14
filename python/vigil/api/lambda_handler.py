"""Runs the service on AWS Lambda, behind a function URL or API Gateway.

Set the function's handler to ``vigil.api.lambda_handler.handler``. Lambda has no file of
results of its own, so point ``VIGIL_SNAPSHOT`` at the stored scan, for example
``s3://keepvigil.earth/snapshot.json``. The settings are described in ``vigil/api/app.py``.
"""

from mangum import Mangum

from vigil.api.app import app

handler = Mangum(app)
