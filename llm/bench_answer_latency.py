"""Ad-hoc latency benchmark for the local Qwen path at TARGET evidence load.

Mirrors EvidencePromptBuilder: SST system prompt + QUESTION + 4 evidence chunks
(~1000 estimated tokens, the configured budget). Sends N warm, concurrency=1
requests with a VARIED question each time so llama.cpp's prompt cache can't skip
prefill and fake a low number. Reports prefill (prompt_ms) vs generation
(predicted_ms) from llama.cpp's own `timings`, plus wall-clock TTFT proxy.

Run:  python llm/bench_answer_latency.py
"""

from __future__ import annotations

import json
import sys
import time
import urllib.request

URL = "http://127.0.0.1:8001/v1/chat/completions"
N = 8
MAX_TOKENS = 120
NCHUNKS = int(sys.argv[1]) if len(sys.argv) > 1 else 4  # evidence chunks (budget lever)

SYSTEM = (
    "You are a warm, clear SST documentary assistant. "
    "Answer ONLY from the supplied evidence. Do not invent, assume, or use outside knowledge. "
    "For specific questions about emails, names, dates, deadlines, locations, or phone numbers, "
    "give the exact data first instead of a general summary. "
    "If the evidence is insufficient, say so clearly. Use plain text only, answer in the user's "
    "language, and do not include a Fuentes/Sources section because the UI shows citations separately."
)

# One evidence chunk ~1000 chars (~250 est tokens). Four of them ~= the 1000-token budget.
_CHUNK = (
    "El uso de equipo de proteccion personal es obligatorio en todas las areas operativas de la planta. "
    "El trabajador debe portar casco, gafas de seguridad, guantes resistentes a cortes y calzado con "
    "puntera de acero antes de ingresar a la zona de maquinaria pesada. El supervisor de turno verifica "
    "el cumplimiento al inicio de cada jornada y registra las observaciones en el formato SST-014. En "
    "caso de detectar un equipo defectuoso, el trabajador debe reportarlo de inmediato al coordinador de "
    "seguridad y no operar la maquina hasta recibir autorizacion. Las inspecciones periodicas se realizan "
    "cada quince dias y sus hallazgos se documentan en el sistema de gestion. El incumplimiento reiterado "
    "de estas normas puede derivar en una sancion disciplinaria segun el reglamento interno de trabajo. "
)


def build_messages(i: int) -> list[dict]:
    sources = []
    for s in range(1, NCHUNKS + 1):
        sources.append(f"\n[SOURCE {s}] Document: Manual_Seguridad_Industrial.pdf | Page: {s}\n{_CHUNK}")
    user = (
        f"QUESTION:\nSegun el manual, cual es el procedimiento exacto numero {i} para el uso de EPP "
        f"y quien verifica su cumplimiento?\n\nEVIDENCE:\n" + "".join(sources)
    )
    return [
        {"role": "system", "content": SYSTEM},
        {"role": "user", "content": user},
    ]


def one_request(i: int) -> dict:
    body = {
        "model": "qwen3-1.7b",
        "messages": build_messages(i),
        "max_tokens": MAX_TOKENS,
        "temperature": 0,
        "stream": False,
        # Production sees distinct evidence per question, so prompt-cache reuse never
        # helps prefill. Disable it here or identical evidence fakes a ~0s prefill.
        "cache_prompt": False,
        # Qwen3 thinking off via chat-template kwarg (falls back to /no_think if ignored).
        "chat_template_kwargs": {"enable_thinking": False},
    }
    data = json.dumps(body).encode()
    req = urllib.request.Request(URL, data=data, headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=120) as resp:
        payload = json.loads(resp.read())
    wall_ms = (time.perf_counter() - t0) * 1000.0
    t = payload.get("timings", {}) or {}
    usage = payload.get("usage", {}) or {}
    return {
        "wall_ms": wall_ms,
        "prompt_ms": t.get("prompt_ms"),          # prefill
        "predicted_ms": t.get("predicted_ms"),    # generation
        "prompt_n": t.get("prompt_n") or usage.get("prompt_tokens"),
        "predicted_n": t.get("predicted_n") or usage.get("completion_tokens"),
    }


def pct(vals: list[float], p: float) -> float:
    if not vals:
        return float("nan")
    s = sorted(vals)
    k = min(len(s) - 1, int(round((p / 100.0) * (len(s) - 1))))
    return s[k]


def main() -> None:
    print(f"Warmup (1 request, discarded)...")
    one_request(0)

    rows = []
    for i in range(1, N + 1):
        r = one_request(i)
        rows.append(r)
        print(
            f"  req {i}: prompt={r['prompt_n']}tok prefill={r['prompt_ms']:.0f}ms  "
            f"gen={r['predicted_n']}tok {r['predicted_ms']:.0f}ms  wall={r['wall_ms']:.0f}ms"
        )

    prefill = [r["prompt_ms"] for r in rows if r["prompt_ms"] is not None]
    gen = [r["predicted_ms"] for r in rows if r["predicted_ms"] is not None]
    wall = [r["wall_ms"] for r in rows]

    def line(name: str, v: list[float]) -> None:
        print(f"  {name:10s} P50={pct(v,50)/1000:6.2f}s  P95={pct(v,95)/1000:6.2f}s  max={max(v)/1000:6.2f}s")

    print(f"\n=== N={N} warm, concurrency=1, {MAX_TOKENS} max out, prompt ~{rows[0]['prompt_n']} tok ===")
    line("prefill", prefill)
    line("gen", gen)
    line("wall(LLM)", wall)
    print("\nNote: wall(LLM) is ONLY local generation. E2E answer time also adds "
          "retrieval + webhook hop + SSE/poll. Target: gen P95 <= 17s, E2E < 25s.")


if __name__ == "__main__":
    main()
