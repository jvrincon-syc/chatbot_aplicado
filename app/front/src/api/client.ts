// The single network boundary. Every backend call goes through here; components never fetch directly.

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function postJson<TResponse>(path: string, body: unknown): Promise<TResponse> {
  let res: Response;
  try {
    res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, "No se pudo conectar con el servidor.");
  }
  if (!res.ok) {
    throw new ApiError(res.status, `El servidor respondió ${res.status}.`);
  }
  return (await res.json()) as TResponse;
}
