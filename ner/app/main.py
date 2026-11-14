"""HireLens NER service: POST /extract takes {text} and returns {skills, orgs, locations, dates}."""

from __future__ import annotations

import os
import secrets
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, status
from pydantic import BaseModel, Field

from .extractor import Extractor

MAX_TEXT_LENGTH = 60_000  # comfortably above the API's 50k resume / 20k job description limits


class ExtractRequest(BaseModel):
    text: str = Field(min_length=1, max_length=MAX_TEXT_LENGTH)


class ExtractResponse(BaseModel):
    skills: list[str]
    orgs: list[str]
    locations: list[str]
    dates: list[str]


state: dict[str, Extractor] = {}


def require_api_key(x_api_key: str | None = Header(default=None)) -> None:
    """If NER_API_KEY is set, callers must send it as X-Api-Key.

    Needed when the service is publicly reachable (e.g. Render's free tier has no private network).
    Locally the variable is unset and the check is skipped. compare_digest avoids timing leaks.
    """
    expected = os.environ.get("NER_API_KEY", "")
    if expected and not secrets.compare_digest(x_api_key or "", expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing or invalid API key")


@asynccontextmanager
async def lifespan(_: FastAPI):
    # Load the model once at startup, not per request (it takes about a second).
    state["extractor"] = Extractor()
    yield
    state.clear()


app = FastAPI(title="HireLens NER", version="1.0.0", lifespan=lifespan)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok" if "extractor" in state else "loading"}


# A plain (sync) def: FastAPI runs it in a worker thread, so CPU-bound spaCy work
# doesn't block the event loop.
@app.post("/extract", response_model=ExtractResponse, dependencies=[Depends(require_api_key)])
def extract(request: ExtractRequest) -> ExtractResponse:
    result = state["extractor"].extract(request.text)
    return ExtractResponse(skills=result.skills, orgs=result.orgs, locations=result.locations, dates=result.dates)
