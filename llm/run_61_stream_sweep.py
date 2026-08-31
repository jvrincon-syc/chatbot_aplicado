"""Drive the 61 real SST questions through the REAL live stack (.NET API + Redis +
llama-server + SSE), measuring the full per-question time and capturing the final
LLM-generated answer.

Retrieval is NOT exercised (the SST corpus is not indexed): evidence is representative,
injected per question via the webhook endpoint (keyword-matched from a small SST pool).
This measures the generation + streaming + latency path end to end with real services.

Per question: POST /api/chat/requests -> POST /api/chat/webhook (evidence) -> read the SSE
stream (answer.delta -> answer.completed), timing from request start to terminal event.

Run (Windows Python 3.12):  python llm/run_61_stream_sweep.py
"""

from __future__ import annotations

import json
import sys
import time
import urllib.request

BASE = "http://127.0.0.1:5254"

QUESTIONS = [
    "Que establece la politica de seguridad y salud en el trabajo?",
    "Cuales son los objetivos del SG-SST?",
    "Como identifica la empresa los peligros y valora los riesgos?",
    "Que programas conforman la planificacion del SG-SST?",
    "Como se gestionan los requisitos legales en SST?",
    "Que contempla la gestion del cambio en seguridad y salud?",
    "Como se prepara la empresa para emergencias?",
    "Que lineamientos aplican a proveedores y contratistas en SST?",
    "Como se hacen auditorias internas del SG-SST?",
    "Como se revisa el SG-SST por la alta direccion?",
    "Que fuentes se usan para identificar oportunidades de mejora continua?",
    "Como se gestionan las acciones correctivas y preventivas?",
    "Como se investigan incidentes accidentes y enfermedades laborales?",
    "Que debe comunicarse al COPASST sobre investigaciones de accidentes?",
    "Que responsabilidades tiene la ARL en seguridad y salud en el trabajo?",
    "Que responsabilidades de SST tiene la organizacion?",
    "Como funciona la induccion y capacitacion anual en SST?",
    "Cuales son las funciones del COPASST?",
    "Que funciones tiene el presidente del COPASST?",
    "Que funciones tiene la secretaria del COPASST?",
    "Como se puede comunicar un trabajador con el COPASST?",
    "Quienes son los miembros principales y suplentes del COPASST 2025 a 2027?",
    "Quien fue nombrado presidente y secretaria del COPASST?",
    "Que es el comite de convivencia laboral?",
    "Cuales son las funciones del comite de convivencia?",
    "Cual es el objetivo del reglamento del comite de convivencia?",
    "Como se conforma el comite de convivencia laboral?",
    "Como funcionan las reuniones del comite de convivencia?",
    "Que metodologia siguen las sesiones del comite de convivencia?",
    "Como se presentan quejas o denuncias de convivencia?",
    "A que correo se envian las quejas de convivencia laboral?",
    "Que derechos tienen los trabajadores en convivencia laboral?",
    "Que deberes de convivencia laboral deben cumplir los trabajadores?",
    "Que principios y valores orientan la convivencia laboral?",
    "En que consiste la politica de desconexion laboral?",
    "Que normas de convivencia deben cumplir los trabajadores?",
    "Que marco legal soporta el comite y la convivencia laboral?",
    "Que dice la politica de prevencion del acoso laboral?",
    "Que es la sala amiga de la familia lactante?",
    "Cuales son las ventajas de la sala amiga?",
    "Donde esta ubicada la sala amiga y quienes pueden usarla?",
    "Como solicito o pido vacaciones?",
    "Que tipos de faltas contempla el reglamento interno de trabajo?",
    "Que sanciones aplican por consumo de alcohol o sustancias psicoactivas?",
    "Que dice la politica de prevencion de alcohol y drogas?",
    "Cuando puede la empresa requerir pruebas de deteccion de consumo?",
    "En que consiste el programa o politica de pausas activas?",
    "Por que son importantes las pausas activas para la salud fisica?",
    "Como ayudan las pausas activas a la concentracion y al estres?",
    "Que recomendaciones de seguridad vial aparecen en el corpus?",
    "Que compromisos tiene el PESV o plan estrategico de seguridad vial?",
    "Que significa cero tolerancia frente a alcohol y sustancias en seguridad vial?",
    "Que documentos o reglas hablan de prevencion del acoso laboral?",
    "Cual es el objetivo general del manual de convivencia laboral?",
    "Cuales son los objetivos especificos del manual de convivencia?",
    "En cuanto tiempo debe el Comite de Convivencia dar tramite a una queja?",
    "Que ocurre si un integrante del Comite de Convivencia es parte de una queja o investigacion?",
    "Por que la seguridad vial es una responsabilidad compartida?",
    "Que programa incluye el PESV para proteger actores viales vulnerables?",
    "Que metodologia debe adoptar la empresa para mejorar continuamente la prevencion del riesgo vial?",
    "Que medidas preventivas y correctivas contempla el reglamento interno frente al acoso laboral y sexual?",
]

# Representative SST evidence pool (ASCII). Each snippet has keywords for matching.
POOL = [
    (["politica", "sg-sst", "objetivos", "seguridad", "salud", "peligros", "riesgos", "mejora", "direccion", "auditoria", "requisitos", "legales", "cambio", "responsabilidad", "arl", "organizacion", "induccion", "capacitacion"],
     "La politica de seguridad y salud en el trabajo declara el compromiso de la alta direccion con la prevencion de lesiones y enfermedades laborales, el cumplimiento de la normativa vigente y la mejora continua del SG-SST. Sus objetivos incluyen identificar peligros, valorar y controlar riesgos, cumplir requisitos legales y proteger a todos los trabajadores y contratistas. La organizacion asigna responsabilidades, recursos y realiza auditorias internas y revision por la direccion."),
    (["incidente", "accidente", "enfermedad", "investiga", "investigacion", "copasst", "correctivas", "preventivas", "acciones"],
     "La investigacion de incidentes, accidentes de trabajo y enfermedades laborales se realiza dentro de los quince dias siguientes, por un equipo con el jefe inmediato, un representante del COPASST y el responsable del SG-SST. Se identifican causas inmediatas y basicas, se definen acciones correctivas y preventivas, y los resultados se comunican al COPASST para actualizar los programas de prevencion."),
    (["copasst", "comite", "paritario", "funciones", "presidente", "secretaria", "miembros", "suplentes", "vigilar", "inspeccion"],
     "El COPASST es el Comite Paritario de Seguridad y Salud en el Trabajo. Sus funciones son proponer actividades de promocion y prevencion, vigilar el desarrollo del SG-SST, inspeccionar periodicamente lugares de trabajo, recibir y tramitar quejas, y participar en la investigacion de accidentes. Tiene un presidente que coordina y convoca las reuniones y una secretaria que lleva las actas. Los miembros principales y suplentes son elegidos para el periodo 2025 a 2027."),
    (["convivencia", "comite", "quejas", "denuncias", "correo", "reglamento", "reuniones", "metodologia", "conforma", "tramite", "queja", "impedido"],
     "El Comite de Convivencia Laboral previene el acoso laboral y atiende las quejas de forma reservada. Las quejas o denuncias se envian al correo convivencia@empresa.com.co. Recibida una queja, el comite debe darle tramite dentro de los cinco dias habiles, citando a las partes. Se conforma de forma paritaria, sesiona ordinariamente cada tres meses, y si un integrante es parte de una queja se declara impedido y lo reemplaza su suplente."),
    (["acoso", "prevencion", "ley", "1010", "derechos", "deberes", "principios", "valores", "sexual"],
     "La politica de prevencion del acoso laboral se compromete con un ambiente respetuoso libre de acoso, discriminacion y violencia, conforme a la Ley 1010 de 2006. Los trabajadores tienen derecho a un trato digno y el deber de respetar a sus companeros. Ningun trabajador sera objeto de represalias por presentar una queja de buena fe. El reglamento interno contempla medidas preventivas y correctivas frente al acoso laboral y sexual."),
    (["pausas", "activas", "salud", "fisica", "concentracion", "estres", "desconexion"],
     "El programa de pausas activas promueve breves ejercicios durante la jornada para prevenir lesiones musculoesqueleticas y la fatiga. Son importantes para la salud fisica porque reducen la tension muscular, y ayudan a la concentracion y a disminuir el estres. La politica de desconexion laboral garantiza el derecho a no atender asuntos de trabajo fuera de la jornada."),
    (["seguridad", "vial", "pesv", "cero", "tolerancia", "alcohol", "sustancias", "actores", "vulnerables", "riesgo"],
     "El Plan Estrategico de Seguridad Vial (PESV) establece compromisos para prevenir accidentes de transito y adopta una metodologia de mejora continua del riesgo vial. Aplica el principio de cero tolerancia: ningun conductor puede operar un vehiculo bajo efectos de alcohol o sustancias psicoactivas. La seguridad vial es una responsabilidad compartida e incluye un programa para proteger a actores viales vulnerables como peatones, ciclistas y motociclistas."),
    (["alcohol", "drogas", "sustancias", "sanciones", "faltas", "reglamento", "pruebas", "deteccion", "vacaciones", "sala", "amiga", "lactante"],
     "El reglamento interno de trabajo clasifica las faltas y establece sanciones, incluidas las aplicables por consumo de alcohol o sustancias psicoactivas. La empresa puede requerir pruebas de deteccion cuando existan indicios razonables. Las vacaciones se solicitan al jefe inmediato con la debida antelacion. La sala amiga de la familia lactante es un espacio acondicionado para que las madres extraigan y conserven la leche materna; esta ubicada en la sede y pueden usarla las trabajadoras en periodo de lactancia."),
]


def pick_evidence(question: str) -> list[str]:
    q = question.lower()
    scored = sorted(POOL, key=lambda s: -sum(1 for kw in s[0] if kw in q))
    return [text for _, text in scored[:4]]


def post_json(path: str, body: dict) -> tuple[int, str]:
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(BASE + path, data=data, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")


def webhook_body(question: str, request_id: str) -> dict:
    chunks = [
        {"node_id": f"n{i}", "document_id": "Manual_SGSST.pdf", "text": text, "score": 0.9 - i * 0.05,
         "source": "vector", "page_start": i + 1, "page_end": i + 1, "section_title": "SST"}
        for i, text in enumerate(pick_evidence(question))
    ]
    return {
        "dispatch_id": f"disp_{request_id}", "project_id": "proj_sst-general",
        "rag_variant_id": "ragv_local-bge", "rag_release_id": "ragr_rep", "retrieval_profile_id": "rp_rep",
        "question": question, "conversation_id": "conv_sweep", "message_id": request_id, "top_k": 4,
        "dispatched_at": "2026-08-31T12:00:00Z", "chunks": chunks,
    }


def run_one(question: str) -> dict:
    t0 = time.perf_counter()
    status, body = post_json("/api/chat/requests", {"question": question, "conversationId": "conv_sweep", "topK": 4})
    rid = json.loads(body).get("requestId")
    # Fire evidence; generation streams to Redis. SSE replays from 0 so no race.
    post_json("/api/chat/webhook", webhook_body(question, rid))

    ttft = None
    answer = ""
    n_deltas = 0
    req = urllib.request.Request(f"{BASE}/api/chat/requests/{rid}/events")
    with urllib.request.urlopen(req, timeout=90) as resp:
        for raw in resp:
            line = raw.decode("utf-8", "replace").strip()
            if not line.startswith("data:"):
                continue
            data = line[len("data:"):].strip()
            try:
                payload = json.loads(data)
            except json.JSONDecodeError:
                continue
            if "delta" in payload:
                if ttft is None:
                    ttft = time.perf_counter() - t0
                n_deltas += 1
            elif "answer" in payload:
                answer = payload["answer"]
                break
            elif "errorCode" in payload:
                answer = f"[FAILED {payload.get('errorCode')}]"
                break
    elapsed = time.perf_counter() - t0
    return {"question": question, "elapsed": elapsed, "ttft": ttft or elapsed, "answer": answer, "deltas": n_deltas}


def pct(vals: list[float], p: float) -> float:
    s = sorted(vals)
    return s[min(len(s) - 1, int(round(p / 100.0 * (len(s) - 1))))]


REPORT = "C:/Users/jvrincon/Documents/chatbot_aplicado_sst/llm/sweep_61_report.txt"


def main() -> None:
    # Redirected stdout defaults to cp1252 (strict) on Windows and crashes on accents.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    limit = int(sys.argv[1]) if len(sys.argv) > 1 else len(QUESTIONS)
    rows = []
    # Write incrementally so a mid-run crash never loses completed answers.
    out = open(REPORT, "w", encoding="utf-8")
    for i, q in enumerate(QUESTIONS[:limit], 1):
        r = run_one(q)
        rows.append(r)
        head = f"#{i:02d} [{r['elapsed']:5.1f}s ttft={r['ttft']:4.1f}s] {q}"
        block = head + "\n    -> " + (r["answer"] or "") + "\n"
        print(block, flush=True)
        out.write(block)
        out.flush()

    el = [r["elapsed"] for r in rows]
    tt = [r["ttft"] for r in rows]
    summary = (
        f"\n=== {len(rows)} questions, concurrency=1, representative evidence ===\n"
        f"E2E   P50={pct(el,50):.1f}s  P95={pct(el,95):.1f}s  max={max(el):.1f}s  avg={sum(el)/len(el):.1f}s\n"
        f"TTFT  P50={pct(tt,50):.1f}s  P95={pct(tt,95):.1f}s  max={max(tt):.1f}s  avg={sum(tt)/len(tt):.1f}s\n"
        f"NOTE: elapsed is generation-dominated; real retrieval NOT included (corpus not indexed)."
    )
    print(summary, flush=True)
    out.write(summary + "\n")
    out.close()


if __name__ == "__main__":
    main()
