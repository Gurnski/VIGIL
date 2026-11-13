# python

The Python analysis service. The `vigil` package has four parts, and each has one owner (see `COORDINATION.md`):

| Folder | Holds |
|---|---|
| `vigil/pipeline/` | Downloading and regridding NASA data, the catalogue and the results cache |
| `vigil/api/` | The FastAPI service that the desktop app and the AI Detective call |
| `vigil/analysis/` | Trends, significance, change points, cascades, contradictions and ranking |
| `vigil/detective/` | The AI Detective |

Downloaded data and the results cache never go in the repository. `.gitignore` keeps them out.
