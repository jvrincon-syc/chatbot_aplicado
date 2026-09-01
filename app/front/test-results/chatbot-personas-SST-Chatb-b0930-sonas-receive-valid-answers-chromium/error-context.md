# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: chatbot-personas.spec.ts >> SST Chatbot — Persona smoke test >> all 6 personas receive valid answers
- Location: tests\e2e\chatbot-personas.spec.ts:111:3

# Error details

```
TimeoutError: locator.waitFor: Timeout 120000ms exceeded.
Call log:
  - waiting for locator('.bubble--typing') to be hidden
    236 × locator resolved to visible <div class="bubble bubble--assistant bubble--typing">…</div>

```

# Page snapshot

```yaml
- generic [ref=e5]:
  - complementary [ref=e6]:
    - generic [ref=e7]:
      - img [ref=e9]
      - generic [ref=e12]:
        - paragraph [ref=e13]: SST Chatbot
        - paragraph [ref=e14]: Seguridad y Salud en el Trabajo
    - navigation "Navegacion principal" [ref=e15]:
      - list [ref=e16]:
        - listitem [ref=e17]:
          - button "Nuevo chat" [disabled] [ref=e18]:
            - img [ref=e19]
            - text: Nuevo chat
        - listitem [ref=e21]:
          - generic [ref=e22]:
            - img [ref=e23]
            - text: Historial
        - listitem [ref=e26]:
          - generic [ref=e27]:
            - img [ref=e28]
            - text: Documentos
        - listitem [ref=e31]:
          - generic [ref=e32]:
            - img [ref=e33]
            - text: Favoritos
      - list [ref=e36]:
        - listitem [ref=e37]:
          - generic [ref=e38]:
            - img [ref=e39]
            - text: Guias SST
        - listitem [ref=e41]:
          - generic [ref=e42]:
            - img [ref=e43]
            - text: Configuracion
    - generic [ref=e46]:
      - img [ref=e48]
      - paragraph [ref=e50]: La seguridad es compromiso de todos.
    - generic [ref=e51]:
      - generic [ref=e52]: T
      - generic [ref=e53]:
        - generic [ref=e54]: Tester
        - generic [ref=e55]: test@test.com
      - button "Cerrar sesion" [ref=e56] [cursor=pointer]:
        - img [ref=e57]
  - main [ref=e60]:
    - generic [ref=e61]:
      - generic [ref=e63]: Marco Legal SST Colombia
      - log [ref=e65]:
        - article [ref=e66]:
          - paragraph [ref=e67]: ¿Qué documentos componen el Anexo Técnico de Seguridad (ATS) y cuál es su importancia en un contrato de obra?
        - article [ref=e68]:
          - generic [ref=e69]:
            - img [ref=e71]
            - generic [ref=e74]:
              - paragraph
              - toolbar "Acciones de la respuesta" [ref=e75]:
                - button "Guardar" [ref=e76] [cursor=pointer]:
                  - img [ref=e77]
                - button "Util" [ref=e79] [cursor=pointer]:
                  - img [ref=e80]
                - button "No util" [ref=e82] [cursor=pointer]:
                  - img [ref=e83]
        - generic [ref=e86]:
          - generic "El asistente está escribiendo" [ref=e87]
          - generic [ref=e91]: Consultando información verificada...
    - generic [ref=e92]:
      - generic [ref=e93]:
        - generic [ref=e94]: Tu pregunta
        - textbox "Tu pregunta" [disabled] [ref=e95]:
          - /placeholder: Escribe tu pregunta sobre SST...
        - button "Enviar" [disabled] [ref=e96]:
          - img [ref=e97]
      - paragraph [ref=e100]: Verifica siempre la información con tu área legal o profesional SST.
```

# Test source

```ts
  1   | import { test, expect, type Page } from "@playwright/test";
  2   | 
  3   | /**
  4   |  * Persona-driven smoke test for the SST Chatbot.
  5   |  *
  6   |  * Simulates 6 distinct Colombian workplace personas asking SST questions
  7   |  * in Spanish. Measures response time, verifies the assistant replies with
  8   |  * non-empty content, and reports results per persona.
  9   |  *
  10  |  * Policy: reuse one browser session, snapshot only on failure,
  11  |  * no tracing, no full DOM dumps.
  12  |  */
  13  | 
  14  | interface Persona {
  15  |   name: string;
  16  |   role: string;
  17  |   question: string;
  18  |   /** Keywords expected somewhere in the answer (case-insensitive). */
  19  |   expectKeywords?: string[];
  20  | }
  21  | 
  22  | const PERSONAS: Persona[] = [
  23  |   {
  24  |     name: "Carlos Mendoza",
  25  |     role: "Ingeniero de Seguridad Industrial",
  26  |     question:
  27  |       "¿Qué documentos componen el Anexo Técnico de Seguridad (ATS) y cuál es su importancia en un contrato de obra?",
  28  |     expectKeywords: ["ATS", "técnico", "seguridad"],
  29  |   },
  30  |   {
  31  |     name: "María Fernanda López",
  32  |     role: "Trabajadora de manufactura",
  33  |     question:
  34  |       "¿Cuáles son los Elementos de Protección Personal obligatorios para trabajar en una planta industrial y cómo se deben cuidar?",
  35  |     expectKeywords: ["protección", "personal", "EPP"],
  36  |   },
  37  |   {
  38  |     name: "Andrés Felipe Ramírez",
  39  |     role: "Gerente de Recursos Humanos",
  40  |     question:
  41  |       "¿Cada cuánto se debe realizar la capacitación obligatoria de Seguridad y Salud en el Trabajo para los trabajadores?",
  42  |     expectKeywords: ["capacitación", "obligatoria"],
  43  |   },
  44  |   {
  45  |     name: "Diana Carolina Muñoz",
  46  |     role: "Supervisora de Planta",
  47  |     question:
  48  |       "¿Qué protocolo de emergencia se debe seguir ante un incendio en un centro de trabajo?",
  49  |     expectKeywords: ["emergencia", "incendio"],
  50  |   },
  51  |   {
  52  |     name: "Juan Esteban Gutiérrez",
  53  |     role: "Aprendiz recién contratado",
  54  |     question:
  55  |       "¿Qué es la inducción de SST y qué temas se deben cubrir cuando un empleado nuevo ingresa a la empresa?",
  56  |     expectKeywords: ["inducción", "SST"],
  57  |   },
  58  |   {
  59  |     name: "Patricia Vargas Solano",
  60  |     role: "Presidenta del COPASST",
  61  |     question:
  62  |       "¿Cuáles son las funciones principales del Comité Paritario de Seguridad y Salud en el Trabajo (COPASST)?",
  63  |     expectKeywords: ["COPASST", "comité"],
  64  |   },
  65  | ];
  66  | 
  67  | /** Seed localStorage to bypass the auth gate. */
  68  | async function bypassAuth(page: Page): Promise<void> {
  69  |   await page.evaluate(() => {
  70  |     localStorage.setItem(
  71  |       "sst_chatbot_user",
  72  |       JSON.stringify({ name: "Tester", email: "test@test.com" }),
  73  |     );
  74  |   });
  75  |   await page.reload();
  76  |   await page.waitForSelector("#chat-input", { timeout: 15_000 });
  77  | }
  78  | 
  79  | /** Type a question, submit, wait for the response, return timing + text. */
  80  | async function askQuestion(
  81  |   page: Page,
  82  |   question: string,
  83  | ): Promise<{ ms: number; answer: string; abstained: boolean }> {
  84  |   const input = page.locator("#chat-input");
  85  |   await input.fill(question);
  86  | 
  87  |   const sendBtn = page.locator('button[aria-label="Enviar"]');
  88  |   await sendBtn.click();
  89  | 
  90  |   // Wait for typing indicator to appear (confirms request started)
  91  |   const typing = page.locator(".bubble--typing");
  92  |   await typing.waitFor({ state: "visible", timeout: 15_000 });
  93  | 
  94  |   const t0 = Date.now();
  95  | 
  96  |   // Wait for typing to disappear (response complete)
> 97  |   await typing.waitFor({ state: "hidden", timeout: 120_000 });
      |                ^ TimeoutError: locator.waitFor: Timeout 120000ms exceeded.
  98  | 
  99  |   const ms = Date.now() - t0;
  100 | 
  101 |   // Grab the last assistant bubble
  102 |   const lastBubble = page.locator(".bubble--assistant").last();
  103 |   const answer = (await lastBubble.locator(".bubble__text").textContent()) ?? "";
  104 |   const abstained =
  105 |     (await lastBubble.getAttribute("class"))?.includes("bubble--abstained") ?? false;
  106 | 
  107 |   return { ms, answer: answer.trim(), abstained };
  108 | }
  109 | 
  110 | test.describe("SST Chatbot — Persona smoke test", () => {
  111 |   test("all 6 personas receive valid answers", async ({ page }) => {
  112 |     const results: {
  113 |       persona: Persona;
  114 |       ms: number;
  115 |       answerSnippet: string;
  116 |       abstained: boolean;
  117 |       keywordHit: boolean;
  118 |       pass: boolean;
  119 |     }[] = [];
  120 | 
  121 |     await page.goto("/");
  122 |     await bypassAuth(page);
  123 | 
  124 |     for (const persona of PERSONAS) {
  125 |       const { ms, answer, abstained } = await askQuestion(page, persona.question);
  126 | 
  127 |       const keywordHit = persona.expectKeywords
  128 |         ? persona.expectKeywords.some((kw) =>
  129 |             answer.toLowerCase().includes(kw.toLowerCase()),
  130 |           )
  131 |         : true;
  132 | 
  133 |       const pass = answer.length > 10 && keywordHit && !abstained;
  134 | 
  135 |       results.push({
  136 |         persona,
  137 |         ms,
  138 |         answerSnippet: answer.slice(0, 120),
  139 |         abstained,
  140 |         keywordHit,
  141 |         pass,
  142 |       });
  143 | 
  144 |       // eslint-disable-next-line no-console
  145 |       console.log(
  146 |         `[${persona.name}] ${persona.role} — ${ms}ms — ${pass ? "PASS" : "FAIL"} — abstained=${abstained} — keywords=${keywordHit}`,
  147 |       );
  148 |     }
  149 | 
  150 |     // Print summary table
  151 |     // eslint-disable-next-line no-console
  152 |     console.log("\n═══════════════════════════════════════════════════════════");
  153 |     // eslint-disable-next-line no-console
  154 |     console.log("PERSONA                        | ROLE                         | TIME    | PASS");
  155 |     // eslint-disable-next-line no-console
  156 |     console.log("───────────────────────────────┼──────────────────────────────┼─────────┼──────");
  157 |     for (const r of results) {
  158 |       // eslint-disable-next-line no-console
  159 |       console.log(
  160 |         `${r.persona.name.padEnd(29)}│ ${r.persona.role.padEnd(28)}│ ${(r.ms + "ms").padStart(7)} │ ${r.pass ? "  ✓" : "  ✗"}`,
  161 |       );
  162 |     }
  163 |     // eslint-disable-next-line no-console
  164 |     console.log("═══════════════════════════════════════════════════════════\n");
  165 | 
  166 |     // At least 5 of 6 must pass
  167 |     const passed = results.filter((r) => r.pass).length;
  168 |     expect(passed, `Expected at least 5/6 personas to pass, got ${passed}`).toBeGreaterThanOrEqual(5);
  169 |   });
  170 | });
  171 | 
```