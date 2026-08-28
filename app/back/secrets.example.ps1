$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ChatbotDispatch__BaseUrl='http://127.0.0.1:8000'
# Bearer token opaco. Montarlo siempre como string exacto entre comillas.
# Puede verse como letras, numeros o mixto: 'abc123.xyz456' o '1234567890'
$env:ChatbotDispatch__BearerToken='<token-bearer-opaco>'
$env:ChatbotDispatch__ProjectId='proj_sst-general'
$env:ChatbotDispatch__RagVariantId='ragv_local-bge'
$env:ChatbotDispatch__SubmitPath='/api/chatbot/questions'
$env:ChatbotDispatch__ReleasesPathTemplate='/api/platform/projects/{project_id}/releases?page=1&page_size=100'

$env:Llm__BaseUrl='http://127.0.0.1:8001'
$env:Llm__Model='qwen3-1.7b'

$env:CHATBOT_LOCAL_API_BASE_URL='http://localhost:5254'
