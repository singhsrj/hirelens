"""Skill and entity extraction with spaCy.

The pipeline is ``en_core_web_sm`` with an EntityRuler placed *before* the statistical NER.
The ruler tags known skills with a custom ``SKILL`` label; because it runs first, a skill like
"MongoDB" is claimed as SKILL instead of being mislabelled ORG by the model. Organisations,
locations and dates come from the model's built-in ORG, GPE/LOC and DATE labels.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

import spacy
from spacy.language import Language

SKILLS_FILE = Path(__file__).with_name("skills.json")
MODEL = "en_core_web_sm"

# Only the NER component (and its own embedding layer) is needed. Dropping the rest makes
# loading and inference noticeably faster.
EXCLUDED_COMPONENTS = ["parser", "lemmatizer", "tagger", "attribute_ruler", "senter"]


@dataclass(frozen=True)
class Extraction:
    skills: list[str] = field(default_factory=list)
    orgs: list[str] = field(default_factory=list)
    locations: list[str] = field(default_factory=list)
    dates: list[str] = field(default_factory=list)


def load_skill_patterns(nlp: Language, path: Path = SKILLS_FILE) -> list[dict]:
    """Builds token patterns from skills.json. Each pattern's id is the canonical skill name.

    Aliases are tokenised with the pipeline's own tokenizer, so "C#" or "ASP.NET Core" split into
    exactly the tokens the matcher will see in real text.
    """
    data = json.loads(path.read_text(encoding="utf-8"))
    patterns: list[dict] = []

    def add(canonical: str, alias: str, case_sensitive: bool) -> None:
        tokens = [t.text for t in nlp.make_doc(alias)]
        key = "ORTH" if case_sensitive else "LOWER"
        pattern = [{key: t if case_sensitive else t.lower()} for t in tokens]
        patterns.append({"label": "SKILL", "pattern": pattern, "id": canonical})

    for canonical, aliases in data["skills"].items():
        for alias in {canonical, *aliases}:
            add(canonical, alias, case_sensitive=False)
    for canonical, aliases in data.get("caseSensitive", {}).items():
        for alias in aliases:
            add(canonical, alias, case_sensitive=True)
    return patterns


def build_pipeline() -> Language:
    nlp = spacy.load(MODEL, exclude=EXCLUDED_COMPONENTS)
    # overwrite_ents lets the ruler's SKILL spans win over any overlapping model prediction.
    ruler = nlp.add_pipe("entity_ruler", before="ner", config={"overwrite_ents": True})
    ruler.add_patterns(load_skill_patterns(nlp))
    return nlp


def _unique(values: list[str]) -> list[str]:
    """Order-preserving, case-insensitive de-duplication."""
    seen: set[str] = set()
    out: list[str] = []
    for v in values:
        key = v.casefold()
        if v and key not in seen:
            seen.add(key)
            out.append(v)
    return out


def _clean(text: str) -> str:
    return " ".join(text.split()).strip(" ,.;:-()")


class Extractor:
    def __init__(self, nlp: Language | None = None) -> None:
        self.nlp = nlp or build_pipeline()

    def extract(self, text: str) -> Extraction:
        doc = self.nlp(text)
        skills: list[str] = []
        orgs: list[str] = []
        locations: list[str] = []
        dates: list[str] = []

        for ent in doc.ents:
            if ent.label_ == "SKILL":
                skills.append(ent.ent_id_ or ent.text)
            elif ent.label_ == "ORG":
                orgs.append(_clean(ent.text))
            elif ent.label_ in ("GPE", "LOC"):
                locations.append(_clean(ent.text))
            elif ent.label_ == "DATE":
                dates.append(_clean(ent.text))

        skills = _unique(skills)
        # The model sometimes tags a skill-like word as ORG where the ruler had no pattern for
        # that exact spelling; never report something as both.
        skill_keys = {s.casefold() for s in skills}
        orgs = [o for o in _unique(orgs) if o.casefold() not in skill_keys]

        return Extraction(skills=skills, orgs=orgs, locations=_unique(locations), dates=_unique(dates))
