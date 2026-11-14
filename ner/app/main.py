"""HireLens NER service: POST /extract takes {text} and returns {skills, orgs, locations, dates}."""

from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI
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
@app.post("/extract", response_model=ExtractResponse)
def extract(request: ExtractRequest) -> ExtractResponse:
    result = state["extractor"].extract(request.text)
    return ExtractResponse(skills=result.skills, orgs=result.orgs, locations=result.locations, dates=result.dates)
