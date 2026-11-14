import pytest

from app.extractor import Extractor


@pytest.fixture(scope="module")
def extractor() -> Extractor:
    # Loading the model takes about a second; share one instance across the module.
    return Extractor()


def test_extracts_canonical_skill_names(extractor):
    result = extractor.extract("Built REST APIs in c# with asp.net core and mongodb.")
    assert result.skills == ["REST APIs", "C#", "ASP.NET Core", "MongoDB"]


def test_aliases_map_to_one_canonical_skill(extractor):
    result = extractor.extract("Frontend in JS and TypeScript. Also wrote plain JavaScript and used k8s.")
    assert result.skills == ["JavaScript", "TypeScript", "Kubernetes"]


def test_multi_token_skills_beat_their_shorter_prefixes(extractor):
    # "ASP.NET Core" must win over a bare ".NET"/"ASP.NET" and "SQL Server" over "SQL".
    result = extractor.extract("Experience with ASP.NET Core and SQL Server.")
    assert "ASP.NET Core" in result.skills
    assert "SQL Server" in result.skills
    assert "SQL" not in result.skills


def test_case_sensitive_skills_ignore_ordinary_words(extractor):
    result = extractor.extract("I go to meetups. Wrote services in Go and some R scripts.")
    assert result.skills == ["Go", "R"]


def test_no_skills_in_unrelated_text(extractor):
    result = extractor.extract("I enjoy hiking and cooking on weekends.")
    assert result.skills == []


def test_extracts_orgs_locations_and_dates(extractor):
    text = (
        "Software Engineer at Microsoft in Seattle from January 2019 to March 2023. "
        "Previously an intern at Infosys in Bangalore in 2018."
    )
    result = extractor.extract(text)
    assert "Microsoft" in result.orgs
    assert "Infosys" in result.orgs
    assert "Seattle" in result.locations
    assert "Bangalore" in result.locations
    assert any("2019" in d for d in result.dates)
    assert any("2018" in d for d in result.dates)


def test_skills_are_never_also_reported_as_orgs(extractor):
    result = extractor.extract("Worked with MongoDB, Docker and Kafka at Acme Corp.")
    assert {"MongoDB", "Docker", "Kafka"} <= set(result.skills)
    lowered_orgs = {o.casefold() for o in result.orgs}
    assert not lowered_orgs & {"mongodb", "docker", "kafka"}


def test_results_are_deduplicated(extractor):
    result = extractor.extract("C#, C#, csharp and more C#. Python and python.")
    assert result.skills == ["C#", "Python"]
