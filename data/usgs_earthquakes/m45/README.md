# USGS earthquakes, magnitude 4.5 and above

`earthquakes.csv` holds 312,431 earthquakes from 1 January 1900 to 31 October 2026, one per row, oldest first.

- Source: the USGS earthquake catalogue (ComCat), https://earthquake.usgs.gov/fdsnws/event/1/
- Query: `eventtype=earthquake`, `minmagnitude=4.5`, `starttime=1900-01-01`, `endtime=2026-11-01`
- Downloaded: 14 November 2026. The row count matched the service's own count for the same query.
- Licence: public domain (US Government work).

Columns are as the service returns them: `time` (UTC), `latitude`, `longitude`, `depth` (km), `mag`, `magType`, `place`, `id` and 14 others covering errors, station counts and the reporting network. The USGS describes each one at https://earthquake.usgs.gov/data/comcat/data-eventterms.php

The early decades are sparse. Before the global seismograph networks of the 1960s and 1970s, only the larger events were recorded, so a rising count over the full period reflects more sensors as well as more earthquakes.

The rest of `data/` is ignored by git. This folder was added by hand with `git add -f`.
