# Dados reais (opcional)

A demonstração **não depende** da planilha da SSP-SP: `scripts/preparar-demo.ps1` cria um banco SQLite com 80 registros **sintéticos**, e a API os identifica como exemplo (`exemplo: true` e o campo `fonte` em `GET /api/ocorrencias`). Os passos abaixo são opcionais e servem para carregar dados públicos reais no lugar da carga fictícia.

## Obter a planilha SSP-SP

1. Acesse o [portal oficial de Consultas SSP-SP](https://www.ssp.sp.gov.br/estatistica/consultas).
2. No bloco **SP Dados — RES 160 — RES 516**, escolha **Dados criminais → 2026**. O arquivo esperado é `SPDadosCriminais_2026.xlsx`.
3. Registre o SHA256, a origem e o período efetivos. Guarde o arquivo **fora do Git** (a pasta `dados/` já está no `.gitignore`).

## Importar para um banco separado do sintético

A partir da pasta da solution (PowerShell):

```powershell
$env:VOLTASEGURA_DB = Join-Path (Get-Location) 'dados/voltasegura-ssp.db'
dotnet run --project VoltaSegura.Api -- --migrar
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r scripts/requirements-etl.txt
.\.venv\Scripts\python.exe scripts/carga_ocorrencias.py --arquivo 'C:\caminho\SPDadosCriminais_2026.xlsx' --banco $env:VOLTASEGURA_DB
dotnet run --project VoltaSegura.Api
```

O importador usa somente a capital, a janela jan–set/2026, as naturezas acordadas (`FURTO - OUTROS`, `ROUBO - OUTROS`, mais contexto) e deduplicação por boletim (`NUM_BO` + `ANO_BO` + delegacia). Grava proveniência e SHA256 na tabela `acervo` e não mistura silenciosamente uma carga sintética com a SSP; `--force` substitui o acervo de forma explícita.

## Roteamento (OpenTripPlanner 2) — adaptador opcional

O domínio define a interface `IRoteador`, com duas implementações em `VoltaSegura.Infrastructure/Roteamento`: `RoteadorMock` (padrão, determinístico) e `Otp2Roteador` (GraphQL em `POST /otp/gtfs/v1`). A troca é feita por configuração (`Roteamento__Modo=mock|otp2`, `Otp2__BaseUrl`), sem alterar o código que consome a interface. O contrato do adaptador é verificado em `Otp2ContratoTests` com respostas controladas; **não foi validado contra um servidor OTP2 real**.
