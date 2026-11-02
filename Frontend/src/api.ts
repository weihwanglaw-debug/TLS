export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), 15000);
  try {
    const response = await fetch(`/api/${path}`, { method, signal: controller.signal, headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined });
    const text = await response.text();
    let data: any;
    try { data = text ? JSON.parse(text) : {}; } catch { throw new Error('The local server returned an unexpected response. Check that it is running.'); }
    if (!response.ok) throw new Error(data.message || `Request failed (${response.status}). Check the JSON structure.`);
    return data as T;
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw new Error('No server acknowledgement received. Refresh to check whether the change was saved before retrying.');
    if (e instanceof TypeError) throw new Error('Cannot reach the local server. Your last confirmed data remains on the server.');
    throw e;
  } finally { clearTimeout(timer); }
}
