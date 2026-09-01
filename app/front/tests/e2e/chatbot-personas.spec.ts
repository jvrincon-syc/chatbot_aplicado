import { test, expect, type Page } from "@playwright/test";

/**
 * Persona-driven smoke test for the SST Chatbot.
 *
 * Simulates 6 distinct Colombian workplace personas asking SST questions
 * in Spanish. Measures response time, verifies the assistant replies with
 * non-empty content, and reports results per persona.
 *
 * Policy: reuse one browser session, snapshot only on failure,
 * no tracing, no full DOM dumps.
 */

interface Persona {
  name: string;
  role: string;
  question: string;
  /** Keywords expected somewhere in the answer (case-insensitive). */
  expectKeywords?: string[];
}

const PERSONAS: Persona[] = [
  {
    name: "Carlos Mendoza",
    role: "Ingeniero de Seguridad Industrial",
    question:
      "¿Qué documentos componen el Anexo Técnico de Seguridad (ATS) y cuál es su importancia en un contrato de obra?",
    expectKeywords: ["ATS", "técnico", "seguridad"],
  },
  {
    name: "María Fernanda López",
    role: "Trabajadora de manufactura",
    question:
      "¿Cuáles son los Elementos de Protección Personal obligatorios para trabajar en una planta industrial y cómo se deben cuidar?",
    expectKeywords: ["protección", "personal", "EPP"],
  },
  {
    name: "Andrés Felipe Ramírez",
    role: "Gerente de Recursos Humanos",
    question:
      "¿Cada cuánto se debe realizar la capacitación obligatoria de Seguridad y Salud en el Trabajo para los trabajadores?",
    expectKeywords: ["capacitación", "obligatoria"],
  },
  {
    name: "Diana Carolina Muñoz",
    role: "Supervisora de Planta",
    question:
      "¿Qué protocolo de emergencia se debe seguir ante un incendio en un centro de trabajo?",
    expectKeywords: ["emergencia", "incendio"],
  },
  {
    name: "Juan Esteban Gutiérrez",
    role: "Aprendiz recién contratado",
    question:
      "¿Qué es la inducción de SST y qué temas se deben cubrir cuando un empleado nuevo ingresa a la empresa?",
    expectKeywords: ["inducción", "SST"],
  },
  {
    name: "Patricia Vargas Solano",
    role: "Presidenta del COPASST",
    question:
      "¿Cuáles son las funciones principales del Comité Paritario de Seguridad y Salud en el Trabajo (COPASST)?",
    expectKeywords: ["COPASST", "comité"],
  },
];

/** Seed localStorage to bypass the auth gate. */
async function bypassAuth(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.setItem(
      "sst_chatbot_user",
      JSON.stringify({ name: "Tester", email: "test@test.com" }),
    );
  });
  await page.reload();
  await page.waitForSelector("#chat-input", { timeout: 15_000 });
}

/** Type a question, submit, wait for the response, return timing + text. */
async function askQuestion(
  page: Page,
  question: string,
): Promise<{ ms: number; answer: string; abstained: boolean }> {
  const input = page.locator("#chat-input");
  await input.fill(question);

  const sendBtn = page.locator('button[aria-label="Enviar"]');
  await sendBtn.click();

  // Wait for typing indicator to appear (confirms request started)
  const typing = page.locator(".bubble--typing");
  await typing.waitFor({ state: "visible", timeout: 15_000 });

  const t0 = Date.now();

  // Wait for typing to disappear (response complete)
  await typing.waitFor({ state: "hidden", timeout: 120_000 });

  const ms = Date.now() - t0;

  // Grab the last assistant bubble
  const lastBubble = page.locator(".bubble--assistant").last();
  const answer = (await lastBubble.locator(".bubble__text").textContent()) ?? "";
  const abstained =
    (await lastBubble.getAttribute("class"))?.includes("bubble--abstained") ?? false;

  return { ms, answer: answer.trim(), abstained };
}

test.describe("SST Chatbot — Persona smoke test", () => {
  test("all 6 personas receive valid answers", async ({ page }) => {
    const results: {
      persona: Persona;
      ms: number;
      answerSnippet: string;
      abstained: boolean;
      keywordHit: boolean;
      pass: boolean;
    }[] = [];

    await page.goto("/");
    await bypassAuth(page);

    for (const persona of PERSONAS) {
      const { ms, answer, abstained } = await askQuestion(page, persona.question);

      const keywordHit = persona.expectKeywords
        ? persona.expectKeywords.some((kw) =>
            answer.toLowerCase().includes(kw.toLowerCase()),
          )
        : true;

      const pass = answer.length > 10 && keywordHit && !abstained;

      results.push({
        persona,
        ms,
        answerSnippet: answer.slice(0, 120),
        abstained,
        keywordHit,
        pass,
      });

      // eslint-disable-next-line no-console
      console.log(
        `[${persona.name}] ${persona.role} — ${ms}ms — ${pass ? "PASS" : "FAIL"} — abstained=${abstained} — keywords=${keywordHit}`,
      );
    }

    // Print summary table
    // eslint-disable-next-line no-console
    console.log("\n═══════════════════════════════════════════════════════════");
    // eslint-disable-next-line no-console
    console.log("PERSONA                        | ROLE                         | TIME    | PASS");
    // eslint-disable-next-line no-console
    console.log("───────────────────────────────┼──────────────────────────────┼─────────┼──────");
    for (const r of results) {
      // eslint-disable-next-line no-console
      console.log(
        `${r.persona.name.padEnd(29)}│ ${r.persona.role.padEnd(28)}│ ${(r.ms + "ms").padStart(7)} │ ${r.pass ? "  ✓" : "  ✗"}`,
      );
    }
    // eslint-disable-next-line no-console
    console.log("═══════════════════════════════════════════════════════════\n");

    // At least 5 of 6 must pass
    const passed = results.filter((r) => r.pass).length;
    expect(passed, `Expected at least 5/6 personas to pass, got ${passed}`).toBeGreaterThanOrEqual(5);
  });
});
