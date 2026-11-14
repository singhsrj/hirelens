import pytest
from fastapi.testclient import TestClient

from app.main import MAX_TEXT_LENGTH, app


@pytest.fixture(scope="module")
def client():
    with TestClient(app) as c:  # the context manager runs the lifespan (model load)
        yield c


def test_health(client):
    assert client.get("/health").json() == {"status": "ok"}


def test_extract_returns_the_contract_shape(client):
    response = client.post("/extract", json={"text": "C# developer at Google in London since 2020."})
    assert response.status_code == 200
    body = response.json()
    assert set(body) == {"skills", "orgs", "locations", "dates"}
    assert body["skills"] == ["C#"]


@pytest.mark.parametrize("payload", [{}, {"text": ""}, {"text": "x" * (MAX_TEXT_LENGTH + 1)}])
def test_extract_rejects_missing_empty_or_huge_text(client, payload):
    assert client.post("/extract", json=payload).status_code == 422
