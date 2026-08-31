"""Answer-quality + natural-length eval for the local Qwen path.

Six representative questions from the real SST set, each with domain-realistic
4-chunk evidence (~1000 tok, the configured budget — chunks NOT reduced). We
generate at a HIGH token limit so each answer stops naturally, then read:
  - completion_tokens + finish_reason  -> how many output tokens the answer TYPE
    actually needs (finish_reason='length' means it was still truncated)
  - the text                            -> quality judged by a human/LLM reviewer
  - wall / gen ms                       -> latency cost (~62 ms per output token)

Run:  python llm/eval_answer_quality.py [max_tokens]   (default 320)
"""

from __future__ import annotations

import json
import sys
import time
import urllib.request

URL = "http://127.0.0.1:8001/v1/chat/completions"
MAX_TOKENS = int(sys.argv[1]) if len(sys.argv) > 1 else 320

SYSTEM = (
    "You are a warm, clear SST documentary assistant. "
    "Answer ONLY from the supplied evidence. Do not invent, assume, or use outside knowledge. "
    "For specific questions about emails, names, dates, deadlines, locations, or phone numbers, "
    "give the exact data first instead of a general summary. "
    "If the evidence is insufficient, say so clearly. Use plain text only, answer in the user's "
    "language, and do not include a Fuentes/Sources section because the UI shows citations separately."
)

# (question, [4 evidence chunks]) — domain-accurate SG-SST content (Colombia).
CASES = [
    (
        "Cuales son las funciones del COPASST?",
        [
            "El Comite Paritario de Seguridad y Salud en el Trabajo (COPASST) tiene entre sus funciones: proponer y participar en actividades de promocion y prevencion; vigilar el desarrollo de las actividades del SG-SST; visitar periodicamente los lugares de trabajo e inspeccionar ambientes, maquinas y operaciones; y servir como organismo de coordinacion entre empleador y trabajadores.",
            "Son funciones del COPASST recibir y tramitar las quejas y sugerencias que presenten los trabajadores en materia de seguridad y salud; participar en la investigacion de las causas de accidentes de trabajo y enfermedades laborales; y proponer medidas correctivas a la administracion.",
            "El COPASST debe mantener un archivo de las actas de reunion y de las evidencias de sus actividades, y colaborar en el analisis de las causas de los accidentes para proponer al empleador las medidas que procedan para evitar su ocurrencia.",
            "El COPASST se reune ordinariamente una vez al mes en las instalaciones de la empresa durante la jornada laboral y de forma extraordinaria cuando ocurra un accidente grave o un riesgo inminente lo requiera.",
        ],
    ),
    (
        "A que correo se envian las quejas de convivencia laboral?",
        [
            "Las quejas o denuncias relacionadas con convivencia laboral deben remitirse al correo electronico convivencia@empresa.com.co, garantizando la confidencialidad del remitente.",
            "El Comite de Convivencia Laboral recibe las quejas de manera escrita y reservada; una vez recibidas, cita a las partes para escuchar los descargos dentro de los plazos establecidos.",
            "El objetivo del reglamento del Comite de Convivencia es prevenir las conductas de acoso laboral y proteger a los trabajadores frente a situaciones que afecten su dignidad.",
            "Los miembros del Comite de Convivencia estan obligados a guardar reserva sobre la informacion a la que tengan acceso en el ejercicio de sus funciones.",
        ],
    ),
    (
        "En cuanto tiempo debe el Comite de Convivencia dar tramite a una queja?",
        [
            "Recibida la queja, el Comite de Convivencia Laboral debe dar tramite dentro de los cinco (5) dias habiles siguientes, citando a las partes para promover un espacio de dialogo y busqueda de solucion.",
            "El Comite hara seguimiento a los compromisos adquiridos y verificara su cumplimiento en un plazo razonable, dejando constancia en las actas correspondientes.",
            "Si un integrante del Comite de Convivencia es parte de una queja o investigacion, debe declararse impedido y ser reemplazado por su suplente para preservar la imparcialidad.",
            "El Comite de Convivencia sesiona ordinariamente cada tres meses y extraordinariamente cuando se presenten casos que requieran atencion inmediata.",
        ],
    ),
    (
        "Como se investigan incidentes accidentes y enfermedades laborales?",
        [
            "La investigacion de incidentes, accidentes de trabajo y enfermedades laborales se realiza dentro de los quince (15) dias siguientes a su ocurrencia, mediante un equipo investigador que incluye al jefe inmediato, un representante del COPASST y el responsable del SG-SST.",
            "El equipo investigador identifica las causas inmediatas y basicas del evento aplicando una metodologia de analisis causal, y determina las acciones correctivas para evitar su repeticion.",
            "Los resultados de la investigacion se documentan y se comunican al COPASST, y las lecciones aprendidas se incorporan a los programas de prevencion.",
            "La empresa conserva las estadisticas de accidentalidad y las emplea como fuente para identificar oportunidades de mejora continua del SG-SST.",
        ],
    ),
    (
        "Que dice la politica de prevencion del acoso laboral?",
        [
            "La politica de prevencion del acoso laboral declara el compromiso de la empresa con un ambiente de trabajo respetuoso, libre de conductas de acoso, discriminacion y violencia, en cumplimiento de la Ley 1010 de 2006.",
            "La empresa promueve mecanismos de prevencion, deteccion temprana y atencion de casos, y garantiza que ningun trabajador sera objeto de represalias por presentar una queja de buena fe.",
            "El Comite de Convivencia Laboral es el organo encargado de recibir y tramitar las situaciones de presunto acoso laboral, promoviendo el dialogo entre las partes.",
            "La politica se difunde a todos los trabajadores mediante la induccion y las capacitaciones periodicas del SG-SST.",
        ],
    ),
    (
        "Que significa cero tolerancia frente a alcohol y sustancias en seguridad vial?",
        [
            "El principio de cero tolerancia establece que ningun conductor o actor vial de la empresa puede operar un vehiculo bajo los efectos de alcohol o sustancias psicoactivas, sin importar la cantidad consumida.",
            "El Plan Estrategico de Seguridad Vial (PESV) contempla controles preventivos, pruebas de deteccion aleatorias y sanciones disciplinarias frente al incumplimiento de esta regla.",
            "La seguridad vial es una responsabilidad compartida entre la empresa y los trabajadores, quienes deben reportar condiciones inseguras y cumplir las normas de transito.",
            "El PESV incluye un programa para proteger a los actores viales vulnerables, como peatones, ciclistas y motociclistas.",
        ],
    ),
]


def build_messages(question: str, chunks: list[str]) -> list[dict]:
    sources = "".join(
        f"\n[SOURCE {i+1}] Document: Manual_SGSST.pdf | Page: {i+1}\n{c}"
        for i, c in enumerate(chunks)
    )
    user = f"QUESTION:\n{question}\n\nEVIDENCE:\n{sources}"
    return [{"role": "system", "content": SYSTEM}, {"role": "user", "content": user}]


def ask(question: str, chunks: list[str]) -> dict:
    body = {
        "model": "qwen3-1.7b",
        "messages": build_messages(question, chunks),
        "max_tokens": MAX_TOKENS,
        "temperature": 0,
        "stream": False,
        "cache_prompt": False,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    data = json.dumps(body).encode()
    req = urllib.request.Request(URL, data=data, headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=180) as resp:
        payload = json.loads(resp.read())
    wall_ms = (time.perf_counter() - t0) * 1000.0
    choice = payload["choices"][0]
    t = payload.get("timings", {}) or {}
    usage = payload.get("usage", {}) or {}
    return {
        "text": choice["message"]["content"].strip(),
        "finish": choice.get("finish_reason"),
        "out_tok": usage.get("completion_tokens"),
        "prompt_tok": usage.get("prompt_tokens"),
        "prefill_ms": t.get("prompt_ms"),
        "gen_ms": t.get("predicted_ms"),
        "wall_ms": wall_ms,
    }


def main() -> None:
    print(f"max_tokens={MAX_TOKENS}, thinking=off, temp=0, cache off\n")
    for i, (q, chunks) in enumerate(CASES, 1):
        r = ask(q, chunks)
        print("=" * 78)
        print(f"Q{i}: {q}")
        print(f"    out={r['out_tok']}tok finish={r['finish']}  "
              f"prefill={r['prefill_ms']:.0f}ms gen={r['gen_ms']:.0f}ms wall={r['wall_ms']:.0f}ms")
        print(f"--- answer ---\n{r['text']}\n")


if __name__ == "__main__":
    main()
