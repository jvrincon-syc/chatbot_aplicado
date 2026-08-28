// The single network boundary. Every backend call goes through here; components never fetch directly.

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string) {
    super(message);
    this.name = "ApiError";
  }
}

export function postJson<TResponse>(path: string, body: unknown): Promise<TResponse> {
  return requestJson<TResponse>(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function getJson<TResponse>(path: string): Promise<TResponse> {
  return requestJson<TResponse>(path, { method: "GET" });
}

async function requestJson<TResponse>(path: string, init: RequestInit): Promise<TResponse> {
  let res: Response;
  try {
    res = await fetch(path, init);
  } catch {
    throw new ApiError(0, "No se pudo conectar con el servidor.");
  }

  if (!res.ok) {
    const { message, code } = await readErrorBody(res);
    throw new ApiError(res.status, message, code);
  }

  return (await res.json()) as TResponse;
}

async function readErrorBody(res: Response): Promise<{ message: string; code?: string }> {
  try {
    const body = (await res.json()) as {
      error?: string;
      errorCode?: string;
      message?: string;
      detail?: string;
    };
    const text = body.error ?? body.message ?? body.detail;
    return { message: text ?? `El servidor respondió ${res.status}.`, code: body.errorCode };
  } catch {
    return { message: `El servidor respondió ${res.status}.` };
  }
}
