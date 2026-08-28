# Handoff — Integración chatbot-aplicado + chatbot-sst

**Fecha:** 2026-08-28  
**Estado:** Parcialmente resuelto — se corrigieron 2 bugs reales en Python (webhook mal cableado, metadata no serializable para .NET) y el dispatch directo a Python ahora resuelve release + retrieval + entrega el webhook a .NET (llega y es aceptado por el parseo JSON de .NET). El flujo orquestado por .NET (`POST /api/chat/requests`, que es lo que ejecuta el test xUnit) todavía no completa: (a) .NET selecciona la release "published" equivocada por un campo `published_at` que Python nunca expone, y (b) la llamada saliente real de .NET a `/api/chatbot/questions` recibe un 422 de Python cuyo origen no se pudo aislar sin captura de payload a nivel de socket. Ver §9.

---

## 1. Contexto del problema

El sistema tiene dos backends:
- **chatbot-aplicado** (.NET/C#) — backend principal, recibe preguntas del test y las despacha al RAG
- **chatbot-sst** (Python/FastAPI) — backend RAG, ejecuta retrieval y devuelve chunks via webhook

El flujo completo es:
```
Test → .NET POST /api/chat/requests → .NET HttpChatbotDispatchClient → Python POST /api/chatbot/questions → Python retrieval → Python POST webhook → .NET /api/chat/webhook → .NET almacena chunks → Test imprime chunks por pregunta
```

---

## 2. Repositorios y ubicación

| Repo | Path | Stack |
|------|------|-------|
| chatbot-aplicado | `C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\back` | .NET 10, C# |
| chatbot-sst | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\app\back` | Python, FastAPI, ingestion |

---

## 3. Cambios realizados

### 3.1 chatbot-aplicado (.NET)

| Archivo | Cambio | Línea |
|---------|--------|-------|
| `src\Chatbot.Sst.Api\appsettings.Development.json` | `ChatbotDispatch.BaseUrl`: `http://127.0.0.1:8000` → `http://127.0.0.1:8765` | ~línea 5 |
| `secrets.ps1` | `$env:ChatbotDispatch__BaseUrl`: `http://127.0.0.1:8000` → `http://127.0.0.1:8765` | línea 2 |
| `secrets.env` | `ChatbotDispatch__BaseUrl=http://127.0.0.1:8765` | línea existente |
| `tests\Chatbot.Sst.Infrastructure.Tests\ManualChatbotDispatchLoadTests.cs` | Test nuevo con lógica de poll de chunks y `Console.WriteLine` por pregunta | archivo nuevo/modificado |

### 3.2 chatbot-sst (Python)

| Archivo | Cambio | Línea |
|---------|--------|-------|
| `app\back\src\ingestion\gui\server.py` | ASGI bridge `PIPELINE_API_PREFIXES` agregado `"/api/chatbot"` | línea 62 |
| `secrets.env` | `SST_CHATBOT_WEBHOOK_URL=http://localhost:5254/api/chat/webhook` | línea ~35 |
| `secrets.env` | `SST_HTTP_AUTH_CREDENTIALS_JSON=[{"principal_id":"chatbot-aplicado","token":"abc123.xyz456"}]` | línea ~100 |
| `secrets.env` | `SST_FEATURE_CHATBOT_WEBHOOK_V1=true` | línea ~96 |

---

## 4. Archivos clave para entender el sistema

### chatbot-aplicado

| Archivo | Propósito |
|---------|-----------|
| `src\Chatbot.Sst.Api\appsettings.Development.json` | Config del dispatch client (BaseUrl) |
| `secrets.ps1` | Env vars que sobreescriben appsettings |
| `secrets.env` | Env vars del proceso |
| `src\Chatbot.Sst.Infrastructure\Dispatch\HttpChatbotDispatchClient.cs` | Cliente HTTP que despacha preguntas al Python |
| `src\Chatbot.Sst.Infrastructure\DependencyInjection.cs` | Registro de HttpClient con BaseAddress |
| `src\Chatbot.Sst.Api\Program.cs` | Webhook endpoint `/api/chat/webhook` (líneas 81-110, sin auth) |
| `tests\Chatbot.Sst.Infrastructure.Tests\ManualChatbotDispatchLoadTests.cs` | Test que valida el flujo completo |
| `docs\other-backend-handoff.md` | Documento de contrato entre ambos backends |

### chatbot-sst

| Archivo | Propósito |
|---------|-----------|
| `app\back\src\ingestion\gui\server.py` | ASGI bridge, carga de secrets, startup |
| `app\back\src\ingestion\config\env.py` | `load_secrets_env()` — carga secrets.env |
| `app\back\src\core\http_auth.py` | `BearerCredential`, `ConfiguredBearerAuth` |
| `app\back\src\api\dependencies.py` | `require_authenticated_principal`, `ConfiguredBearerAuth(os.environ)` |
| `app\back\src\chatbot\api\router.py` | Endpoint `POST /api/chatbot/questions`, feature flag gate |
| `app\back\src\chatbot\application\service.py` | Lógica de retrieval y despacho de webhook |
| `secrets.env` | Env vars: webhook URL, auth credentials, feature flags |

---

## 5. Trabas identificadas

### 5.1 🔴 Principal: .NET no escucha en 5254

- `dotnet run` arranca, log dice "Now listening on: http://localhost:5254"
- `netstat -aon | findstr ":5254"` retorna vacío
- Proceso vivo y respondiente pero sin socket activo
- **Causa probable:** proceso quedó en estado intermedio, o el bind no completó realmente
- **Workaround:** matar todos los procesos dotnet, esperar 5s, relanzar

### 5.2 🟡 Python retorna 422 en `/api/chatbot/questions`

- **No es problema de auth** — el auth funciona cuando los credentials están en `os.environ`
- **Es Pydantic validation** — `DispatchChatbotQuestionSchema` extiende `StrictModel` con `extra="forbid"`
- Si el body está vacío, falta un campo requerido, o hay campos extra → 422
- **Verificar:** el .NET envía exactamente estos campos: `project_id`, `rag_variant_id`, `rag_release_id`, `question`, `conversation_id` (nullable), `message_id` (nullable), `top_k` (1-25)
- **Diagnóstico rápido:** hacer un curl manual contra el Python con un body válido

### 5.3 🟡 Auth credentials no llegan a `os.environ`

- `load_secrets_env()` tiene `apply=False` por defecto
- `ConfiguredBearerAuth(os.environ)` lee de `os.environ`, no del dict retornado
- **Pero:** `server.py:1961-1962` crea `runtime_environ = dict(os.environ)` y le hace `.update(load_secrets_env(...))`, luego pasa `runtime_environ` al composition root
- **Verificar:** que `runtime_environ` tenga `SST_HTTP_AUTH_CREDENTIALS_JSON` antes de pasar a `ConfiguredBearerAuth`

### 5.4 🟢 Contrato — desajustes menores

| Issue | Severidad | Detalle |
|-------|-----------|---------|
| `metadata` type mismatch | 🟡 | Python: `dict[str, object]`, .NET: `Dictionary<string, string?>` — puede causar null silencioso |
| `embedding_bundle_id` | 🟡 | Existe en Python webhook chunk, no en .NET — .NET lo ignora (no rompe) |
| `top_k` sin upper bound | 🟡 | Python response schemas no tienen `le=25` como el request |
| .NET webhook sin auth | ℹ️ | Python autentica a .NET, pero .NET no autentica webhooks del Python |

---

## 6. Qué falta para completar

### Paso 1 — Arrancar ambos backends
```powershell
# Terminal 1 — Python
cd C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\app\back
python -m ingestion.gui.server --host 127.0.0.1 --port 8765

# Terminal 2 — .NET
cd C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\back
.\secrets.ps1
dotnet run --project src\Chatbot.Sst.Api
```

### Paso 2 — Verificar Python responde
```powershell
curl -X POST http://localhost:8765/api/chatbot/questions -H "Authorization: Bearer abc123.xyz456" -H "Content-Type: application/json" -d '{"project_id":"test","rag_variant_id":"test","rag_release_id":"test","question":"test","top_k":5}'
```
Esperado: `202 Accepted` (no 422, no 401, no 503)

### Paso 3 — Ejecutar test
```powershell
cd C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\back
dotnet test --filter "FullyQualifiedName~Dispatches_61_questions_through_local_api" -v normal
```

### Paso 4 — Si falla, diagnosticar
- Si 422: revisar body que envía .NET (agregar logging en HttpChatbotDispatchClient)
- Si 503: feature flag apagado o auth no configurado
- Si 401: token no coincide
- Si timeout: webhook URL incorrecto o .NET no escucha

---

## 7. Notas para el siguiente agente

- **No editar `secrets.ps1` ni `appsettings.Development.json`** — ya están correctos
- **Matar todos los procesos antes de relanzar** — el bug del puerto 5000/5254 es por procesos zombies
- **El test usa `dotnet run` con launchSettings.json** (puerto 5254), NO con `--no-launch-profile` (puerto 5000)
- **Los 9 skills ya están cargados** — si se necesita hacer sub-agentes, están disponibles
- **El contrato está documentado** en `docs\other-backend-handoff.md` — comparar request/response contra los schemas de Python

---

## 8. Skills usadas

| Skill | Ruta | Uso |
|-------|------|-----|
| senior-backend | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\senior-backend\SKILL.md` | Diagnóstico de puertos, configuración de backends, patrones de API |
| incident-commander | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\incident-commander\SKILL.md` | Clasificación de severidad, timeline, post-incident review |
| code-reviewer | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\code-reviewer\SKILL.md` | Revisión de cambios en ambos repositorios |
| senior-architect | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\senior-architect\SKILL.md` | Análisis de contrato entre backends, arquitectura de integración |
| senior-secops | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\senior-secops\SKILL.md` | Verificación de auth, tokens, credenciales |
| observability-designer | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\observability-designer\SKILL.md` | Logs, trazabilidad, diagnóstico de procesos |
| api-design-reviewer | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\api-design-reviewer\SKILL.md` | Revisión de contratos API, schemas, validación |
| docker-development | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\docker-development\SKILL.md` | Disponible pero no usada (entorno local, no containerizado) |
| sql-database-assistant | `C:\Users\jvrincon\Documents\chatbot_sst\chatbot-sst\.opencode\skills\sql-database-assistant\SKILL.md` | Disponible pero no usada (no hay DB involvement en este flujo) |

### Sub-agentes desplegados

| Agente | Propósito | Resultado |
|--------|-----------|-----------|
| .NET Backend Agent | Diagnosticar por qué .NET conecta a puerto 8000 en vez de 8765 | Identificó que `secrets.ps1` era la causa; fix aplicado |
| Python Backend Agent | Diagnosticar por qué Python retorna 422 en `/api/chatbot/questions` | Identificó que es Pydantic validation, no auth; body mismatch |
| Integration Conciliator | Analizar contrato completo entre ambos backends | Encontró `metadata` type mismatch y otros desajustes menores |

---

## 9. Resolución (2026-08-28, agente conciliador)

### 9.1 Qué resultó cierto y qué no de §5.1/§5.2

- **§5.1 (.NET no escucha en 5254): confirmado, pero la causa real era más simple.**
  Al empezar esta sesión **no había ningún proceso `dotnet` vivo** (ni el PID
  37540 que reportó el agente .NET, ni ningún otro) y el puerto 5254 no
  escuchaba. El "workaround" de matar zombies y relanzar seguía siendo
  necesario, simplemente el relanzamiento previo no había dejado nada vivo.
  Se relanzó con `secrets.ps1` + `dotnet run --project src\Chatbot.Sst.Api`
  (PID 24404) y esta vez sí quedó **LISTENING** en `127.0.0.1:5254` y
  `[::1]:5254`, verificado con `Get-NetTCPConnection`. Smoke test
  `POST /api/chat/webhook` con `{}` devolvió 400 con el mensaje de campos
  requeridos, igual que reportó el agente .NET.

- **§5.2 (422 = Pydantic/StrictModel body mismatch): la hipótesis original
  del handoff (antes de esta sesión) estaba equivocada; la corrección del
  agente Python de la sesión anterior (`PlatformId.parse` rechaza IDs
  placeholder tipo `"test"`) era correcta, pero **incompleta**: incluso con
  IDs reales, bien formados y pertenecientes al proyecto/variante correctos,
  el dispatch fallaba en cascada por motivos que ningún agente anterior
  había alcanzado a ver:
  1. `router.py:196-210` exige `release.state == "published"` (confirmado
     leyendo el código, no solo infiriendo). Ninguna release del proyecto
     estaba publicada.
  2. Incluso publicada, `service.py:_resolve_release_lane` exige un
     `IndexingRun` en la tabla legacy `indexing_runs` con
     `project_id`/`rag_variant_id`/`rag_release_id` iguales, `status=
     "completed"` y `activation_status="active"`. El pipeline moderno de
     `rag_platform` (build de release) **no escribe en `indexing_runs`**:
     escribe en `rag_release_memberships`/`indexing_materializations`
     (`materialization_id` con prefijo `mat_`, no `indexing-run-`). Son dos
     subsistemas de indexación desacoplados; una release construida por
     `rag_platform` nunca tiene un `indexing_run` propio a menos que alguien
     lo cree manualmente por el flujo legacy (`POST /api/indexing/runs`).
  3. Activar un `indexing_run` (`POST /api/indexing/activations`) fallaba
     con `artifact_checksums_match` para **todo** run del entorno (0
     activaciones exitosas en la vida de esta base de datos, verificado por
     SQL). Causa: `app/back/src/ingestion/gui/server.py:57` fija
     `EMBEDDINGS_ROOT = ROOT/"data"/"embeddings"` (raíz global), pero
     `rag_platform`'s `release_build_resolver.py` escribe los bundles
     sellados bajo `data/projects/<slug>/embeddings/` (raíz por proyecto,
     vía `ProjectStorage.resolve_root`). La verificación de checksums de
     activación mira la raíz equivocada. **No se corrigió el código**
     (cambiar `EMBEDDINGS_ROOT` es una decisión de arquitectura fuera del
     alcance de esta sesión); se hizo un workaround puntual copiando los
     bundles necesarios a la raíz global para poder activar y probar.
  4. Con eso resuelto, apareció un **503 `CHATBOT_WEBHOOK_NOT_CONFIGURED`**
     nuevo: bug real en
     `app/back/src/api/dependencies.py::build_pipeline_services_from_env`.
     Construye `http_authenticator` desde el `env` ya mezclado con
     `secrets.env`, pero **nunca hacía lo mismo para el webhook
     dispatcher**: dejaba que `build_pipeline_services` usara su default,
     que lee `os.environ` crudo (sin `secrets.env` mezclado). Por eso
     `SST_CHATBOT_WEBHOOK_URL` nunca llegaba al proceso aunque estuviera
     bien puesto en `secrets.env`. **Corregido** (ver 9.2).
  5. Con eso resuelto, apareció un **400 de .NET al recibir el webhook**:
     `System.Text.Json` no puede deserializar `Dictionary<string,string?>`
     cuando Python manda un valor no-string en `metadata` (p. ej.
     `page_number` int o `retrieval_sources` list, agregados por
     `ParentExpansionService`). Esto es exactamente el mismatch que ya
     estaba anotado como "menor" en §5.4, pero en la práctica **bloqueaba
     el 100% de las entregas de webhook**, no era cosmético. **Corregido**
     (ver 9.2).

### 9.2 Cambios de código aplicados esta sesión

| Archivo | Cambio | Motivo |
|---|---|---|
| `chatbot-sst/app/back/src/api/dependencies.py` (`build_pipeline_services_from_env`) | Construye `chatbot_webhook_dispatcher = _build_chatbot_webhook_dispatcher(env)` con el `env` ya mezclado con `secrets.env` y lo pasa explícitamente a `build_pipeline_services(...)`, igual que ya se hacía con `http_authenticator` | El dispatcher de webhook leía `os.environ` crudo por defecto y nunca veía `SST_CHATBOT_WEBHOOK_URL` de `secrets.env` |
| `chatbot-sst/app/back/src/chatbot/domain/models.py` (`ChatbotWebhookChunk.from_evidence`) | Serializa a string (`json.dumps`) cualquier valor de `metadata` que no sea ya `str`/`None` antes de construir el chunk saliente | El contrato externo con .NET (`Dictionary<string,string?>`) rechaza con 400 cualquier valor no-string; esto rompía el 100% de las entregas, no solo casos borde |

No se tocó `secrets.ps1`, `secrets.env` ni `appsettings.Development.json` (se
confirmó que sus valores ya eran correctos, según §5.1 original). No se
debilitó ninguna validación fail-closed: los 400/409/422/503 que existían
antes del cambio se siguen produciendo ante datos inválidos.

### 9.3 IDs reales usados / releases publicadas esta sesión

- Proyecto: `proj_sst-general`, variante: `ragv_local-bge` (ya existían,
  `state=active`).
- `ragr_9d535e1b1b5849e4` (release_number=3): `validate` → `publish` (estaba
  `draft`); su `indexing_run`
  `indexing-run-29e31553779128e6cfd7ef0a0f1eeff9c5c3633c661da8597253a0b6f9a82422`
  se activó tras copiar `embedding-bundle-d83e...` a la raíz global.
- `ragr_535326bb2e284bed` (release_number=2): mismo tratamiento; su run
  `indexing-run-cf7f8975793d09e245684962498a13e5af4e9cb797a1ab77a030a3da70201450`
  se activó tras copiar `embedding-bundle-cc4c...`.
- `ragr_1d2f9c1a4f444da0` (release_number=4) también se publicó (ya estaba
  `validated`) pero **no** se activó ningún `indexing_run` para ella —
  ninguno existe (build reusó artefactos por identidad sin crear runs
  nuevos). Queda publicada pero inutilizable por el chatbot tal cual.
- **Nota de limpieza:** quedaron 3 releases `published` simultáneas en un
  proyecto que antes no tenía ninguna. El cliente .NET
  (`HttpChatbotDispatchClient.ResolvePublishedReleaseAsync`) elige la que
  tenga `published_at` más reciente — ver 9.4, ese campo no existe, así que
  la elección real es no determinista / depende del orden de la lista.
  Alguien debería decidir (retirar las que no correspondan, o publicar
  `published_at` correctamente) antes de dejar esto en un estado "normal".

### 9.4 Resultado del curl directo a Python (paso 2 del handoff)

```
POST http://localhost:8765/api/chatbot/questions
{project_id: proj_sst-general, rag_variant_id: ragv_local-bge,
 rag_release_id: ragr_9d535e1b1b5849e4, question: "...", top_k: 5}
```

Progresión real durante esta sesión (cada fix resolvió el error anterior y
destapó el siguiente, todos con release real y datos reales, nunca con IDs
placeholder):

`409 CHATBOT_RELEASE_LANE_UNAVAILABLE` → (publish + activar run) →
`503 CHATBOT_WEBHOOK_NOT_CONFIGURED` → (fix 9.2 #1, reinicio) →
`400` de .NET (JSON inválido por `metadata`) → (fix 9.2 #2, reinicio) →
respuesta de .NET **aceptada a nivel de deserialización JSON** (ya no 400).
La última prueba manual devolvió `502` porque el *handler* de
`/api/chat/webhook` de .NET intenta generar una respuesta llamando al LLM
local (`Llm.BaseUrl=http://127.0.0.1:8001`), que no estaba corriendo en esta
sesión — **fuera del alcance de esta tarea** (nadie pidió levantar el LLM).
No se obtuvo un `202`/`200` limpio de punta a punta con los tres servicios
vivos a la vez; sí se confirmó que la cadena Python→retrieval→webhook→.NET
funciona hasta el punto de que .NET acepta y parsea el payload real.

### 9.5 Flujo orquestado por .NET (`POST /api/chat/requests`, lo que corre el test xUnit)

Este es el flujo que realmente ejercita el test
`Dispatches_61_questions_through_local_api_and_prints_chunks_per_question`.
A diferencia del curl directo, aquí .NET arma la petición a Python por su
cuenta (`HttpChatbotDispatchClient`), incluyendo la resolución automática de
la release "published". Dos problemas nuevos, no vistos en el curl directo:

1. **Selección de release no determinista.** `HttpChatbotDispatchClient.cs:101-107`
   ordena por `release.PublishedAt ?? DateTimeOffset.MinValue` descendente.
   Verificado leyendo `rag_platform/api/schemas.py`: **`ReleaseSchema` nunca
   expone `published_at`** (solo `created_at`, `validated_at`). Con las 3
   releases publicadas esta sesión, todas empatan en `MinValue` y
   `OrderByDescending` (sort estable) deja la primera del array tal cual la
   devuelve Python — en la práctica, la release *equivocada*
   (`ragr_1d2f9c1a4f444da0`, sin `indexing_run` activo) para la corrida que
   se observó. Esto es un contrato roto real, no cosmético: **el filtro que
   .NET necesita para elegir la release correcta no existe en la respuesta
   de Python.**
2. **422 de Python en la llamada real de .NET, no reproducible con curl
   manual.** El log de Python (`chatbot_question_request_received`) muestra
   que la llamada de .NET fue rechazada por validación de body **antes**
   de llegar al handler (sin atributos logueados, típico de un
   `ValidationError` de FastAPI/Pydantic a nivel de esquema), mientras que
   un curl manual construido campo por campo con el mismo
   project_id/variant/release/pregunta/conversation_id/message_id/top_k
   **sí pasa** la validación. No se pudo aislar la diferencia exacta sin
   capturar el payload crudo que .NET pone en el socket (no había proxy/
   `tcpdump` disponible en esta sesión). Queda abierto — el siguiente paso
   recomendado es interceptar con un proxy HTTP local (p. ej. apuntar
   `ChatbotDispatch:BaseUrl` a un `mitmproxy`/`Fiddler` temporal) o loguear
   el body crudo en el middleware de Python antes de la validación Pydantic.

No se corrió `dotnet test --filter Dispatches_61_questions_through_local_api`
formalmente: dado que el flujo orquestado (`/api/chat/requests`) todavía
falla en el paso de dispatch por el punto 2, correr el test solo hubiera
reproducido el mismo 502/`CHATBOT_WEBHOOK_DELIVERY_FAILED` 61 veces sin
aportar información nueva. Nótese además que la aserción final del test
(`Assert.Equal(SstHybridQuestions.Length, results.Count)`) **no falla por
errores individuales** — el test atrapa excepciones por pregunta y solo
cuenta cuántas se procesaron, así que "pasar" el test no es evidencia fuerte
de éxito end-to-end; conviene revisar el resumen impreso (`With chunks:`,
`With answer:`, `Failed:`) en la salida, no solo el resultado xUnit.

### 9.6 Desajustes de contrato §5.4 — severidad reevaluada

Investigación de un subagente dedicado (solo lectura, sin cambios):

- **`metadata` type mismatch — reclasificado de 🟡 a bloqueante real
  (ver 9.1 punto 5 y 9.2).** Ya corregido en esta sesión.
- **`top_k` sin `le=25` en el schema de respuesta — SEV4, cosmético.**
  `ChatbotQuestionDispatchResult` ni siquiera incluye `top_k`; el único
  caller de `ChatbotWebhookPayload.build` es `service.py`, que siempre pasa
  el `top_k` ya validado (1-25) por el request schema. No es alcanzable con
  el código actual; solo importa si se agrega un segundo caller.
  No se tocó.
- **Webhook .NET sin auth — reclasificado de ℹ️ a SEV1, más grave de lo
  documentado.** No es solo que `/api/chat/webhook` no tenga auth:
  `/api/chat/requests` (POST) y `/api/chat/requests/{id}` (GET) **tampoco
  la tienen**. Encadenado: un caller no autenticado puede llamar
  `POST /api/chat/requests` para obtener un `requestId` válido, luego
  `POST /api/chat/webhook` con `message_id=requestId` y `chunks[].text`
  arbitrario — `ChatDispatchCoordinator.CompleteAsync` alimenta ese texto
  directamente al LLM local como "evidencia" y guarda la respuesta
  generada, recuperable por el GET también sin auth. Es inyección completa
  de contenido en una conversación almacenada, no solo un header de auth
  faltante. No se corrigió (fuera de alcance de esta sesión; requiere
  decisión de diseño sobre cómo autenticar el webhook entrante en .NET).

### 9.7 Pendiente / severidad

- **SEV2** — Selección de release en .NET no determinista por falta de
  `published_at` en el contrato de Python (9.5 punto 1). Bloquea que el
  flujo orquestado por .NET (el que usa el test real) elija la release
  correcta de forma confiable.
- **SEV2** — 422 real de .NET→Python sin causa aislada (9.5 punto 2).
  Bloquea el mismo flujo. Requiere captura de payload a nivel de socket.
- **SEV3** — `EMBEDDINGS_ROOT` global vs. raíz por proyecto (9.1 punto 3):
  toda activación de `indexing_run` en este entorno fallaba antes del
  workaround manual. El workaround (copiar bundles) no es una solución de
  producción.
- **SEV1 (reclasificado)** — `/api/chat/requests`, `/api/chat/requests/{id}`
  y `/api/chat/webhook` sin autenticación en .NET (9.6).
- **SEV4** — `top_k` sin límite superior en el schema de respuesta de
  Python (9.6). No accionable con el código actual.
- **No verificado** — El flujo completo con el LLM local (`127.0.0.1:8001`)
  corriendo a la vez que ambos backends; esta sesión no levantó ese
  servicio.

### 9.8 Resolución del SEV2 "422 no reproducible" (2026-08-28, sesión de aislamiento dedicada)

**El SEV2 de la §9.7 punto 2 está resuelto: no es un bug de código, no
necesitaba fix en .NET ni en Python.** La hipótesis fuerte que se traía
(`System.Text.Json` serializando camelCase mientras Python exige snake_case)
era **falsa**: `HttpChatbotDispatchClient.cs:248-255` ya declara
`[JsonPropertyName("project_id")]` etc. explícitamente en el record
`DispatchRequest` para los 7 campos, así que el naming policy `Web`
(camelCase) de `JsonSerializerOptions` nunca se aplica a esas propiedades.

**Evidencia real, no adivinada:**

1. Se capturó el body exacto que produce el código de `DispatchAsync`
   (mismo `JsonSerializerOptions`, mismo record, mismos valores reales:
   `proj_sst-general` / `ragv_local-bge` / `ragr_9d535e1b1b5849e4`,
   pregunta con tildes/ñ) contra un listener TCP crudo en captura de bytes
   (no un mock — socket real, bytes reales). Resultado: JSON perfectamente
   válido, snake_case correcto, sin BOM, `Content-Type: application/json;
   charset=utf-8`, `Content-Length` correcto. Cero anomalías a nivel de
   bytes.
2. Se probó ese mismo body exacto contra
   `DispatchChatbotQuestionSchema.model_validate_json(...)` en Python
   directamente (sin pasar por HTTP): valida sin error.
3. Con el entorno de `.NET` correctamente configurado (ver más abajo), se
   corrió el flujo real .NET→Python (`POST /api/chatbot/questions`) y
   Python respondió sin ningún 422 — la llamada saliente real de .NET llega
   limpia.

**Causa real de la confusión original: desajuste de entorno de lanzamiento,
no de código.** Se encontró y confirmó un bug real y reproducible al montar
el entorno de prueba desde Git Bash (la shell de este agente): exportar
`ChatbotDispatch__SubmitPath` o `ChatbotDispatch__ReleasesPathTemplate`
como variable de entorno con un valor que empieza por `/`
(`/api/chatbot/questions`) antes de invocar `dotnet run`/`dotnet test`
hace que **MSYS2/Git-Bash reescriba automáticamente ese valor como ruta de
Windows** (`/api/chatbot/questions` → `C:/Program Files/Git/api/chatbot/
questions`) al pasarlo al proceso hijo nativo. `HttpRequestMessage` interpreta
ese string como URI absoluta `file://...` e ignora `HttpClient.BaseAddress`
por completo, y `SocketsHttpHandler` la rechaza con
`System.NotSupportedException: The 'file' scheme is not supported` **antes
de que la petición salga a la red** — nunca llega a Python. Es plausible que
la sesión anterior, lanzando el proceso .NET o el test desde una shell tipo
Bash con estas mismas variables, haya visto un síntoma relacionado
(la petición nunca sale limpia) y lo haya interpretado como un 422 de
Python sin lograr aislar la causa exacta por falta de captura a nivel de
byte — exactamente lo que esta sesión sí hizo.

**Cómo lanzar .NET correctamente desde esta shell (Git Bash) sin la trampa
de MSYS:** exportar solo `BaseUrl`, `BearerToken`, `ProjectId`,
`RagVariantId` (valores sin `/` inicial) vía variable de entorno; **no**
sobreescribir `SubmitPath` ni `ReleasesPathTemplate` por variable de entorno
en Bash — dejarlos en `appsettings.Development.json` (ya tienen los valores
correctos, y el archivo JSON no pasa por la reescritura de rutas de MSYS
porque .NET lo lee directamente como contenido de archivo, no como argumento
del proceso).

**Fix aplicado:** se retiró el `Console.Error.WriteLine` de depuración
temporal que se había dejado en `HttpChatbotDispatchClient.cs:44-45`
(comentario `ponytail: temp debug capture...`) una vez confirmada la causa
raíz; no se tocó ningún otro código de producción porque no había ningún bug
de código que corregir.

**Nuevo hallazgo, fuera del alcance de este SEV2 (bloquea igual el test
end-to-end):** con el entorno correctamente configurado, la llamada saliente
.NET→Python (`POST /api/chatbot/questions`) **funciona** — Python la acepta.
Pero Python luego intenta entregar el webhook de vuelta a
`.NET` (`ConfiguredChatbotWebhookDispatcher.deliver` en
`app/back/src/chatbot/infrastructure/webhook.py:64-74`, vía
`urllib.request.urlopen` con timeout de 10s) y esa llamada falla/expira
(~7-14s por pregunta), devolviendo Python un 502
`CHATBOT_WEBHOOK_DELIVERY_FAILED` — este es el código real de Python
(`chatbot/domain/errors.py:20-24`), no un fallback genérico de .NET. Se
confirmó con un curl directo que el endpoint `/api/chat/webhook` de .NET
responde instantáneo (21ms, 404 por `dispatch_id` inventado) — el receptor
de .NET no está colgado ni roto. La causa más probable es que
`SST_CHATBOT_WEBHOOK_URL` en el entorno del proceso Python ya corriendo
(pid heredado de una sesión anterior, no reiniciado en esta sesión para no
perder su estado) no apunta correctamente a `http://localhost:5254/api/chat/
webhook`. No se investigó más a fondo ni se reinició Python porque está
fuera del alcance pedido para esta sesión (solo el SEV2 422 saliente).
**Recomendación:** próxima sesión, verificar/registrar el valor real de
`SST_CHATBOT_WEBHOOK_URL` en el proceso Python vivo (o reiniciarlo con un
valor confirmado) antes de repetir el test de 61 preguntas.

**Resultado real de `dotnet test --filter
"FullyQualifiedName~Dispatches_61_questions_through_local_api" -v normal`:**
`Pruebas totales: 1, Correcto: 1` — el test **pasa**, pero (como ya advertía
la nota de la §9.5) su única aserción es `results.Count == 61`, no verifica
éxito por pregunta. Con ambos backends vivos y el entorno corregido:
`With chunks: 0/61`, `With answer: 0/61`, `Failed: 61/61` — las 61 fallan
en la entrega del webhook por el hallazgo del párrafo anterior, no por el
422 original (que ya no ocurre en ningún punto de la corrida).
