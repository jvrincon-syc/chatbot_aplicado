"""Quick test: submit one question and poll until completed/failed."""
import json
import time
import urllib.request

BASE = "http://127.0.0.1:5254"

def submit(question, conv_id):
    data = json.dumps({"Question": question, "ConversationId": conv_id}).encode()
    req = urllib.request.Request(f"{BASE}/api/chat/requests", data=data, headers={"Content-Type": "application/json"}, method="POST")
    resp = urllib.request.urlopen(req, timeout=30)
    return json.loads(resp.read().decode())

def poll(rid, max_polls=30, delay=2):
    for i in range(max_polls):
        time.sleep(delay)
        req = urllib.request.Request(f"{BASE}/api/chat/requests/{rid}")
        resp = urllib.request.urlopen(req, timeout=15)
        status = json.loads(resp.read().decode())
        state = status["state"]
        print(f"  Poll {i+1}: state={state}")
        if state in ("completed", "failed"):
            return status
    return None

if __name__ == "__main__":
    result = submit("¿Qué es el Sistema de Gestión de SST?", "test-real-2")
    print(f"Submitted: {result['requestId']} state={result['state']}")
    final = poll(result["requestId"])
    if final:
        print(json.dumps(final, indent=2, ensure_ascii=False))
    else:
        print("Timed out after 60s")
