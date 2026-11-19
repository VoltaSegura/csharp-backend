"""Gera evidências reais da execução local da VoltaSegura.Api (somente dados sintéticos).

Uso (precisa de .NET 8 SDK e Python 3; a partir de qualquer diretório):
    python scripts/verificar_entrega.py

Saída: pasta evidencias/ na raiz da solution, com build, testes, migrações, respostas HTTP e OpenAPI.
"""
from __future__ import annotations
import json
import os
import socket
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

BACKEND = Path(__file__).resolve().parents[1]
EVIDENCIAS = BACKEND / "evidencias"
FLAGS = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


def salvar(nome, dado):
    (EVIDENCIAS / nome).write_text(json.dumps(dado, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def comando(nome, args, env):
    p = subprocess.run(args, cwd=BACKEND, env=env, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", creationflags=FLAGS)
    (EVIDENCIAS / nome).write_text("$ " + " ".join(args) + "\n" + p.stdout + p.stderr, encoding="utf-8")
    if p.returncode:
        raise RuntimeError(f"Falha em {nome}; consulte a evidência.")


def chamar(base, caminho, corpo=None):
    req = Request(base + caminho, data=json.dumps(corpo).encode() if corpo is not None else None,
                  headers={"Content-Type": "application/json"})
    try:
        with urlopen(req, timeout=30) as res:
            return res.status, json.load(res)
    except HTTPError as erro:
        return erro.code, json.load(erro)


def iniciar(env):
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        porta = sock.getsockname()[1]
    base = f"http://127.0.0.1:{porta}"
    projeto = "VoltaSegura.Api"
    processo = subprocess.Popen(
        ["dotnet", str(BACKEND / projeto / "bin/Debug/net8.0" / (projeto + ".dll")), "--urls", base],
        cwd=BACKEND / projeto, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=FLAGS)
    for _ in range(100):
        if processo.poll() is not None:
            raise RuntimeError("A API encerrou antes de iniciar.")
        try:
            status, _ = chamar(base, "/swagger/v1/swagger.json")
            if status == 200:
                return processo, base
        except (URLError, ConnectionError):
            pass
        time.sleep(.2)
    processo.terminate()
    processo.wait(timeout=10)
    raise RuntimeError("A API não iniciou no tempo limite.")


def parar(processo):
    processo.terminate()
    processo.wait(timeout=10)


def main():
    EVIDENCIAS.mkdir(parents=True, exist_ok=True)
    banco = BACKEND / "dados" / f"verificacao-{time.time_ns()}.db"
    env = dict(os.environ, VOLTASEGURA_DB=str(banco), ASPNETCORE_ENVIRONMENT="Development")
    comando("01-build.txt", ["dotnet", "build", "VoltaSegura.sln", "--nologo"], env)
    comando("02-testes.txt", ["dotnet", "test", "VoltaSegura.sln", "--no-build", "--nologo"], env)
    executar = ["dotnet", "run", "--no-build", "--no-launch-profile", "--project", "VoltaSegura.Api", "--"]
    comando("03-migracao.txt", executar + ["--migrar"], env)
    comando("04-migracao-idempotente.txt", executar + ["--migrar"], env)

    processo, base = iniciar(env)
    try:
        status, vazio = chamar(base, "/api/ocorrencias")
        assert status == 200 and vazio["resumo"] == [] and vazio["exemplo"] is False
        salvar("05-banco-vazio.json", vazio)
    finally:
        parar(processo)

    comando("06-carga-sintetica.txt", executar + ["--demonstracao"], env)
    processo, base = iniciar(env)
    try:
        status, resumo = chamar(base, "/api/ocorrencias")
        assert status == 200 and resumo["exemplo"] is True
        assert sum(r["boletinsDistintos"] for r in resumo["resumo"]) == 80
        salvar("07-ocorrencias-demonstracao.json", resumo)
        status, filtrado = chamar(base, "/api/ocorrencias?natureza=ROUBO%20-%20OUTROS&faixa=4")
        assert status == 200
        salvar("08-ocorrencias-filtradas.json", filtrado)
        for nome, caminho in [("08a-ranking-bairros.json", "/api/ocorrencias/bairros?top=5"),
                              ("08b-naturezas.json", "/api/ocorrencias/naturezas"),
                              ("08c-faixas.json", "/api/ocorrencias/faixas")]:
            status, dado = chamar(base, caminho)
            assert status == 200 and dado["exemplo"] is True
            salvar(nome, dado)
        pedido = {"trechos": [{"modo": "WALK", "comprimentoKm": 0.5}, {"modo": "BUS", "comprimentoKm": 4.0},
                              {"modo": "SUBWAY", "comprimentoKm": 3.0}, {"modo": "WALK", "comprimentoKm": 0.3}]}
        status, resumo_trajeto = chamar(base, "/api/trajetos/resumo", pedido)
        assert status == 200 and resumo_trajeto["baldeacoes"] == 1
        salvar("08d-resumo-trajeto.json", {"requisicao": pedido, "resposta": resumo_trajeto})
        status, erro = chamar(base, "/api/ocorrencias?faixa=9")
        assert status == 400
        salvar("09-validacao-400.json", {"httpStatus": status, "resposta": erro})
        status, bloqueio = chamar(base, "/api/trajetos")
        assert status == 403
        salvar("10-trajetos-desativados-403.json", {"httpStatus": status, "resposta": bloqueio})
        _, swagger = chamar(base, "/swagger/v1/swagger.json")
        salvar("11-openapi.json", swagger)
    finally:
        parar(processo)

    processo, base = iniciar(env)  # reinício: os dados persistem no SQLite
    try:
        _, persistido = chamar(base, "/api/ocorrencias")
        assert persistido == resumo
        salvar("12-ocorrencias-apos-reiniciar.json", persistido)
    finally:
        parar(processo)

    salvar("resultado.json", {"executadoEmUtc": datetime.now(timezone.utc).isoformat(),
                              "resultado": "aprovado", "acervo": "80 registros sintéticos; nenhum BO real",
                              "reinicioComPersistencia": True, "sspRealImportada": False})
    print(f"Evidências aprovadas: {EVIDENCIAS}")


if __name__ == "__main__":
    main()
