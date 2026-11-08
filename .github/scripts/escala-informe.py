#!/usr/bin/env python3
"""Resume la prueba de escala real: métricas del servidor agrupadas por cantidad de jugadores, más lo que vio cada
máquina de bots. Escribe una tabla en markdown en informe-escala.md (para el resumen de GitHub) y avisos (::notice / ::warning).
Uso: escala-informe.py servidor.log resumen-bots-1.txt resumen-bots-2.txt …"""
import re
import sys

PRESUPUESTO_MS = 1000 / 60  # la física corre a 60 pasos por segundo

server_log, bot_files = sys.argv[1], sys.argv[2:]
text = open(server_log, encoding="utf-8", errors="replace").read()

machine = re.search(r"ESCALA_MAQUINA (.*)", text)
rows = []
for line in text.splitlines():
    if "METRICS" not in line:
        continue
    kv = dict(re.findall(r"(\w+)=([-0-9.]+)", line.split("METRICS", 1)[1]))
    try:
        rows.append({k: float(v) for k, v in kv.items()})
    except ValueError:
        continue

out = ["## Prueba de escala real (servidor en internet)", ""]
if machine:
    out += [f"**Servidor:** {machine.group(1)}", ""]
if not rows:
    out.append("No llegaron métricas del servidor.")
    print("::error title=Escala::no llegaron métricas del servidor")
else:
    # Solo la subida y las mesetas: desde que el grupo empieza a irse (cae más de un 10 % desde el pico), las métricas
    # mezclan la salida en masa y ya no dicen cuánto aguanta el servidor con esa cantidad.
    peak_seen, measured = 0, []
    for r in rows:
        n = int(r.get("jugadores", 0))
        if peak_seen and n < peak_seen * 0.9:
            break
        peak_seen = max(peak_seen, n)
        measured.append(r)
    buckets = {}
    for r in measured:
        n = int(r.get("jugadores", 0))
        if n == 0:
            continue
        key = (n - 1) // 10 * 10 + 10  # 1–10 → 10, 11–20 → 20, …
        buckets.setdefault(key, []).append(r)
    out += ["| Jugadores | Física máx (ms/paso) | % del presupuesto | CPU máx (100 = 1 núcleo) | RAM (MB) | Envía (kbps) | Por jugador (kbps) | Pares visibles | FPS mín | Armar paquetes máx (ms) | Limpiezas de memoria (rápidas / grandes) |",
            "|---|---|---|---|---|---|---|---|---|---|---|"]
    first_half = first_full = None
    for key in sorted(buckets):
        b = buckets[key]
        phys = max(r.get("fisica_ms_max", 0) for r in b)
        pct = phys / PRESUPUESTO_MS * 100
        sent = max(r.get("enviado_kbps", 0) for r in b)
        per = max(r.get("enviado_kbps", 0) / max(1, r.get("jugadores", 1)) for r in b)
        out.append(f"| hasta {key} | {phys:.1f} | {pct:.0f} % | {max(r.get('cpu_pct', 0) for r in b):.0f} | "
                   f"{max(r.get('ram_mb', 0) for r in b):.0f} | {sent:.0f} | {per:.1f} | "
                   f"{max(r.get('pares_visibles', 0) for r in b):.0f} | {min(r.get('fps', 0) for r in b):.0f} | "
                   f"{max(r.get('envio_ms_max', 0) for r in b):.1f} | {sum(r.get('gc0', 0) for r in b):.0f} / {sum(r.get('gc2', 0) for r in b):.0f} |")
        if pct >= 50 and first_half is None:
            first_half = key
        if pct >= 100 and first_full is None:
            first_full = key
    peak = max(int(r.get("jugadores", 0)) for r in rows)
    disc = max(r.get("desconexiones", 0) for r in measured)
    errs = max(r.get("errores", 0) for r in rows)
    out += ["", f"Máximo de jugadores a la vez: **{peak}** · desconexiones antes de la salida planeada: {disc:.0f} · errores: {errs:.0f}",
            f"Presupuesto de física: {PRESUPUESTO_MS:.1f} ms por paso (60 por segundo)."]
    if first_full:
        print(f"::warning title=Escala::la física pasó el presupuesto con hasta {first_full} jugadores")
    elif first_half:
        print(f"::notice title=Escala::la física pasó la mitad del presupuesto con hasta {first_half} jugadores (todavía llega)")
    print(f"::notice title=Escala::pico de {peak} jugadores; física máx {max(r.get('fisica_ms_max', 0) for r in rows):.1f} ms")

out += ["", "### Máquinas de bots", ""]
for f in bot_files:
    out.append("- " + open(f, encoding="utf-8").read().strip().replace("ESCALA_BOTS ", ""))
open("informe-escala.md", "w", encoding="utf-8").write("\n".join(out) + "\n")
print("\n".join(out))  # también al log del trabajo
