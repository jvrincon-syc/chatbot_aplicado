# Handoff Para El Otro Backend

Fecha: 2026-08-28

## Objetivo

Este repo no hace retrieval. Tu backend:

- recibe la pregunta desde este chatbot
- resuelve una `rag_release_id` published
- recupera chunks
- llama el webhook de este chatbot
- responde `202 Accepted` si el webhook acepto la entrega

## Autenticacion

Este chatbot te llamara con:

```http
Authorization: Bearer <token-bearer-opaco>
Content-Type: application/json
```

Regla:

- El token bearer es opaco.
- Tratalo siempre como string exacto.
- Puede contener letras, numeros, puntos, guiones u otros caracteres validos de token.
- No lo conviertas a numero ni intentes interpretarlo.

## Endpoint Que Debes Exponer

```http
POST /api/chatbot/questions
```

Body estricto:

```json
{
  "project_id": "proj_sst-general",
  "rag_variant_id": "ragv_local-bge",
  "rag_release_id": "ragr_...",
  "question": "¿Cual es el procedimiento para reportar un accidente de trabajo?",
  "conversation_id": "conv_123",
  "message_id": "msg_456",
  "top_k": 8
}
```

Reglas:

- `project_id`: string canonico `proj_...`
- `rag_variant_id`: string canonico `ragv_...`
- `rag_release_id`: string canonico `ragr_...`
- `question`: string no vacio
- `conversation_id`: opcional
- `message_id`: opcional
- `top_k`: opcional, entero `1..25`, default `10`
- no aceptar campos extra

## Validaciones Que Debes Hacer

- La `rag_release_id` existe.
- Pertenece al `project_id` enviado.
- Pertenece al `rag_variant_id` enviado.
- Su estado es `published`.
- Resuelve exactamente una lane activa de retrieval.
- La pregunta se embebe con el `embedding_profile_id` de esa misma lane.

## Como Resolver La Release

Endpoint:

```http
GET /api/platform/projects/proj_sst-general/releases?page=1&page_size=100
```

Filtro esperado:

- `project_id == "proj_sst-general"`
- `rag_variant_id == "ragv_local-bge"`
- `state == "published"`

Usa la `rag_release_id` resultante en el dispatch.

## Respuesta Esperada A Este Chatbot

Si todo sale bien:

```http
202 Accepted
```

```json
{
  "dispatch_id": "chatq_...",
  "project_id": "proj_sst-general",
  "rag_variant_id": "ragv_local-bge",
  "rag_release_id": "ragr_...",
  "retrieval_profile_id": "retrieval-profile-...",
  "question": "¿Cual es el procedimiento para reportar un accidente de trabajo?",
  "conversation_id": "conv_123",
  "message_id": "msg_456",
  "chunks_sent": 8,
  "webhook_status_code": 202,
  "dispatched_at": "2026-08-28T..."
}
```

Importante:

- Este response no trae la respuesta final del LLM.
- Solo confirma que el webhook fue aceptado.

## Webhook Que Debes Llamar

Tu backend debe enviar:

```http
POST http://<host-alcanzable-del-chatbot>:5254/api/chat/webhook
Content-Type: application/json
```

Payload:

```json
{
  "dispatch_id": "chatq_...",
  "project_id": "proj_sst-general",
  "rag_variant_id": "ragv_local-bge",
  "rag_release_id": "ragr_...",
  "retrieval_profile_id": "retrieval-profile-...",
  "question": "¿Cual es el procedimiento para reportar un accidente de trabajo?",
  "conversation_id": "conv_123",
  "message_id": "msg_456",
  "top_k": 8,
  "chunks": [
    {
      "node_id": "node_...",
      "document_id": "doc_...",
      "parent_node_id": "parent_...",
      "child_chunk_id": "child_...",
      "text": "Texto del chunk...",
      "score": 0.91,
      "source": "vector",
      "page_start": 3,
      "page_end": 3,
      "section_title": "Reporte e investigacion",
      "section_path": "Manual > Incidentes",
      "metadata": {
        "retrieval_mode": "hybrid",
        "rag_release_id": "ragr_..."
      },
      "embedding_profile_id": "bge-m3",
      "corpus_version": "..."
    }
  ],
  "dispatched_at": "2026-08-28T..."
}
```

## Respuesta Esperada Del Webhook

El webhook de este chatbot responde idealmente `2xx`.

- Si responde `2xx`, tu backend puede devolver `202` al caller original.
- Si responde `4xx` o `5xx`, tratelo como error de entrega.

## Conectividad

Checklist:

- Este chatbot local corre en `http://localhost:5254`.
- Si tu backend corre en Docker, `localhost:5254` dentro del contenedor no sirve.
- En Docker sobre Windows, usa `http://host.docker.internal:5254/api/chat/webhook`.
- Si corre en otra maquina, usa `http://<ip-host-chatbot>:5254/api/chat/webhook`.

## Errores Esperables

- `CHATBOT_RAG_CONTEXT_MISMATCH`
- `CHATBOT_RELEASE_NOT_PUBLISHED`
- `CHATBOT_RELEASE_LANE_UNAVAILABLE`
- `CHATBOT_EVIDENCE_UNAVAILABLE`
- `CHATBOT_WEBHOOK_NOT_CONFIGURED`
- `CHATBOT_WEBHOOK_DELIVERY_FAILED`
- `HTTP_AUTH_REQUIRED`
- `HTTP_AUTH_INVALID_CREDENTIALS`

## Smoke Test Recomendado

1. Verifica que el chatbot local responda `GET /health`.
2. Verifica que tu backend pueda hacer `POST` al webhook configurado.
3. Envia una pregunta corta con `top_k = 2`.
4. Confirma:
   - `POST /api/chatbot/questions` devuelve `202`
   - el webhook devuelve `2xx`
   - el chatbot local luego expone chunks en `GET /api/chat/requests/{requestId}`
