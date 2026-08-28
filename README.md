# chatbot-sst

Frontend + backend de chatbot para SST. Este repo no hace retrieval ni administra el RAG: envia la
pregunta a un backend externo, recibe `question + chunks` por webhook y usa un LLM local para
redactar la respuesta final.

```text
React -> ASP.NET Core API -> POST /api/chatbot/questions
      -> backend externo resuelve release + chunks
      -> POST /api/chat/webhook
      -> LLM local
      -> frontend consulta estado / respuesta
```

## Boundary

Otro backend es dueno del ciclo RAG y del retrieval. Este repo solo:

- recibe preguntas del usuario
- despacha la pregunta con `project_id`, `rag_variant_id` y una `rag_release_id` published
- recibe el webhook con chunks
- responde con el LLM local usando solo esos chunks

No agregues aqui ingestion, chunking, embeddings, indices, releases ni paginas operativas del RAG.

## Stack

| Layer | Tech |
|-------|------|
| Backend | C# / .NET 10 / ASP.NET Core (`app/back`) |
| Frontend | React + TypeScript SPA (`app/front`) |
| External context backend | HTTP API con bearer auth |
| Local LLM | endpoint OpenAI-compatible en `127.0.0.1:8001` |
| Dev tooling only | Python 3.12 en `.venv_tools/` |

## Required Config

Configura `app/back/src/Chatbot.Sst.Api/appsettings.Development.json` o variables equivalentes para:

- `ChatbotDispatch:BaseUrl`
- `ChatbotDispatch:BearerToken`
- `ChatbotDispatch:ProjectId`
- `ChatbotDispatch:RagVariantId`
- `ChatbotDispatch:SubmitPath`
- `ChatbotDispatch:ReleasesPathTemplate`
- `Llm:BaseUrl`
- `Llm:Model`
- `CHATBOT_LOCAL_API_BASE_URL` para el test manual

Regla del token:

- `ChatbotDispatch:BearerToken` es un string opaco.
- Montalo siempre como texto exacto.
- Si visualmente parece solo numerico, igual va entre comillas en PowerShell y como valor string en env.
- Nunca lo partas, parsees ni lo conviertas a numero.

En desarrollo:

- `project_id = proj_sst-general`
- `rag_variant_id = ragv_local-bge`
- `rag_release_id` se resuelve en runtime consultando `GET /api/platform/projects/proj_sst-general/releases`

Archivos de referencia:

- [secrets.example.env](/C:/Users/jvrincon/Documents/chatbot_aplicado_sst/secrets.example.env)
- [app/back/secrets.example.ps1](/C:/Users/jvrincon/Documents/chatbot_aplicado_sst/app/back/secrets.example.ps1)
- [appsettings.Development.json](/C:/Users/jvrincon/Documents/chatbot_aplicado_sst/app/back/src/Chatbot.Sst.Api/appsettings.Development.json)

## Backend Flow

1. `POST /api/chat/requests` recibe `question`, `conversationId`, `messageId` opcional y `topK`.
2. El backend consulta la release published y despacha `POST /api/chatbot/questions`.
3. El backend externo recupera chunks y llama `POST /api/chat/webhook`.
4. Este repo convierte los chunks en evidencia y llama al LLM local.
5. El frontend consulta `GET /api/chat/requests/{requestId}` hasta ver `completed` o `failed`.

## Comandos

### Back

```powershell
cd C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\back
Copy-Item .\secrets.example.ps1 .\secrets.ps1
# reemplaza solo el bearer real en .\secrets.ps1 como string exacto
. .\secrets.ps1
dotnet run --project src\Chatbot.Sst.Api --launch-profile http
```

Notas:

- `app/back/secrets.ps1` queda ignorado por Git.
- El backend local escucha en `http://localhost:5254` por el perfil `http`.
- El webhook que debe conocer el backend externo es `http://<host-alcanzable>:5254/api/chat/webhook`.

### Front

```powershell
cd C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\front
npm run dev
```

Notas:

- Vite sirve el front en `http://localhost:5173`.
- El proxy de desarrollo apunta a `http://localhost:5254`.

### Test Manual

```powershell
cd C:\Users\jvrincon\Documents\chatbot_aplicado_sst\app\back
. .\secrets.ps1
$env:CHATBOT_LOCAL_API_BASE_URL='http://localhost:5254'
dotnet test tests/Chatbot.Sst.Infrastructure.Tests/Chatbot.Sst.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Dispatches_61_questions_through_local_api"
```

Notas:

- El test ahora hace un preflight a `GET /health`.
- Si el backend local no esta arriba, falla al inicio con un mensaje claro.
- El output imprime los chunks que llegaron por cada pregunta cuando el webhook completa el request.

## Verify

Se permite compilar sin descargas de red:

```powershell
cd app/back
dotnet build Chatbot.Sst.slnx

cd ../front
npm run build
```

No ejecutes tests desde el terminal de este agente; pasale esos comandos al usuario.
