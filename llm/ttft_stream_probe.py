"""Confirm streaming TTFT vs non-streaming total on the local Qwen path.

Proves the perceived-latency premise: with stream=true the user sees the first
token at ~prefill time instead of waiting for the whole answer. Measures
Time-To-First-Token and full completion for a realistic ~1000-tok prompt.

Run:  python llm/ttft_stream_probe.py
"""

from __future__ import annotations

import json
import time
import urllib.request

URL = "http://127.0.0.1:8001/v1/chat/completions"
MAX_TOKENS = 200

SYSTEM = "You are a warm SST assistant. Answer ONLY from the evidence, plain text, user's language."
_CHUNK = (
    "El uso de equipo de proteccion personal es obligatorio en las areas operativas. El trabajador debe "
    "portar casco, gafas, guantes y calzado con puntera de acero. El supervisor verifica el cumplimiento "
    "al inicio de cada jornada y registra observaciones en el formato SST-014. Las inspecciones se hacen "
    "cada quince dias y sus hallazgos se documentan en el sistema de gestion del SG-SST. "
)


def messages() -> list[dict]:
    ev = "".join(f"\n[SOURCE {i}] Document: Manual_SGSST.pdf | Page: {i}\n{_CHUNK}" for i in range(1, 5))
    return [
        {"role": "system", "content": SYSTEM},
        {"role": "user", "content": f"QUESTION:\nComo se controla el uso de EPP y quien lo verifica?\n\nEVIDENCE:\n{ev}"},
    ]


def stream_once() -> tuple[float, float, int]:
    body = {
        "model": "qwen3-1.7b", "messages": messages(), "max_tokens": MAX_TOKENS,
        "temperature": 0, "stream": True, "cache_prompt": False,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    req = urllib.request.Request(URL, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    ttft = None
    ntok = 0
    with urllib.request.urlopen(req, timeout=120) as resp:
        for raw in resp:
            line = raw.decode("utf-8", "replace").strip()
            if not line.startswith("data:"):
                continue
            data = line[len("data:"):].strip()
            if data == "[DONE]":
                break
            try:
                delta = json.loads(data)["choices"][0].get("delta", {}).get("content")
            except (json.JSONDecodeError, KeyError, IndexError):
                continue
            if delta:
                if ttft is None:
                    ttft = time.perf_counter() - t0
                ntok += 1
    total = time.perf_counter() - t0
    return (ttft or total), total, ntok


def main() -> None:
    stream_once()  # warmup
    ttfts, totals = [], []
    for _ in range(4):
        ttft, total, n = stream_once()
        ttfts.append(ttft)
        totals.append(total)
        print(f"  TTFT={ttft:5.2f}s  full={total:5.2f}s  tokens={n}")
    avg = lambda v: sum(v) / len(v)
    print(f"\n=== streaming, {MAX_TOKENS} max out ===")
    print(f"  TTFT avg = {avg(ttfts):.2f}s   <- what the user perceives as 'answer time'")
    print(f"  full avg = {avg(totals):.2f}s   <- chat.answer.completed (SLO)")
    print(f"  perceived latency cut: {avg(totals) - avg(ttfts):.2f}s hidden behind streaming")


if __name__ == "__main__":
    main()
