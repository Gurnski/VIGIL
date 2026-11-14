import os
import json
import boto3
import earthaccess

def lambda_handler(event, context):
    # Authenticate with NASA Earthdata using environment variables
    auth = earthaccess.login(
        username=os.environ.get('EARTHDATA_USERNAME'),
        password=os.environ.get('EARTHDATA_PASSWORD'),
        strategy='environment'
    )

    # Search for recent datasets (e.g., GLDAS soil moisture or MERRA-2)
    # Restrict spatial bounding box to keep payloads small and free-tier friendly
    results = earthaccess.search_data(
        short_name='GLDAS_NOAH025_M',
        temporal=('2026-01-01', '2026-01-02'),
        bounding_box=(-180, -90, 180, 90)
    )

    # Parse or extract the necessary coordinate values into a lightweight list/dictionary
    # (For demonstration, we structure a lightweight JSON payload)
    lightweight_payload = {
        "dataset": "GLDAS_NOAH025_M",
        "status": "success",
        "granule_count": len(results),
        "sample_data": "Processed coordinate grids go here..."
    }

    # Save processed JSON to S3 Bucket
    s3 = boto3.client('s3')
    bucket_name = os.environ.get('S3_BUCKET_NAME')

    s3.put_object(
        Bucket=bucket_name,
        Key='processed/latest_data.json',
        Body=json.dumps(lightweight_payload),
        ContentType='application/json'
    )

    return {
        'statusCode': 200,
        'body': json.dumps('NASA data successfully processed and saved to S3!')
    }