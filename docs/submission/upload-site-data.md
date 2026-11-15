# Putting the real scan on keepvigil.earth

The deploy on every push puts the viewer and the app download on the bucket. It does not carry the scan's data, which is git-ignored, so that goes up by hand, once. Until it does, the site shows the synthetic fixture.

This is written for the laptop, which has the AWS CLI and the keys. The home PC has neither.

## What to bring

`data\handover\vigil-site-data.zip` from the home PC (41 MB, 5,845 files). It holds:

- `snapshot.json.gz`: the scan, run `scan-20261115T005506Z`, compressed
- `series\<variable>\<cell_id>.json`: the chart for every finding
- `years\<variable>.json`: each variable year by year

## Commands

In PowerShell, from the folder that holds the zip:

```powershell
Expand-Archive vigil-site-data.zip site
aws s3 ls
```

`aws s3 ls` lists the buckets. Put the site's bucket in place of `<bucket>` below.

```powershell
aws s3 cp site\snapshot.json.gz s3://<bucket>/snapshot.json --content-encoding gzip --content-type application/json --cache-control no-cache
aws s3 sync site\series s3://<bucket>/series --content-type application/json
aws s3 sync site\years s3://<bucket>/years --content-type application/json
```

The first line matters most: the site shows the real findings as soon as `snapshot.json` is there. The `series` sync is the slow one, at about 5,800 small files.

Then deploy again, so the app download carries the real scan too. Do it by pushing, not by re-running an old deploy:

```powershell
git pull --rebase
git push
```

Push the fix to `VIGIL/VIGIL/Controls/FindingsList.cs` first if it is not on `main` yet. Without it the app freezes for about 30 seconds when the Map page opens on a real scan, and a re-run of an older deploy would put the real scan beside that older app. If there is nothing new to push, re-run the newest deploy only:

```powershell
gh run list --limit 1
gh run rerun <the id it shows>
```

## Checking it worked

```powershell
curl.exe -sI https://keepvigil.earth/snapshot.json
curl.exe -sI https://keepvigil.earth/years/temperature.json
```

Both should answer `200`, and the first should carry `Content-Encoding: gzip`. Then open keepvigil.earth with a hard refresh (Ctrl+F5):

- the "Synthetic fixture" badge is gone
- the list holds 6,122 findings, and the top five are vegetation
- a finding far down the list still shows its chart
- "Years" plays from 2001 to 2025

## If something goes wrong

- The page still shows the fixture: check the first `curl`. A `404` means the snapshot went to the wrong bucket or the wrong name.
- The page shows an error about the snapshot: the copy went up without `--content-encoding gzip`. Run the first `aws s3 cp` line again.
- To go back to the fixture: `aws s3 rm s3://<bucket>/snapshot.json`.
- The snapshot is compressed. Tonight's service code reads it either way. Any older copy of the service that reads the bucket's `snapshot.json` directly cannot, so redeploy that first.

The same commands are in `python/vigil/api/README.md` under "A website with no service", with how the files are made.
