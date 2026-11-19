# VoltaSegura — backend C#/.NET

Entrega da disciplina **C# Software Development** (FIAP · Global Solution 2026.2 · CitizenTech).

## Integrantes

| RM | Nome completo |
|---|---|
| 565065 | Augusto Barcelos Barros |
| 556197 | Caio Felipe de Lima Bezerra |
| 555541 | Juan Francisco Alves Muradas |
| 555931 | Lucas Derenze Simidu |
| 554873 | Sofia Fernandes |

## A ideia

Aplicativos de mobilidade priorizam tempo e distância, mas pouco ajudam a entender as condições urbanas ao longo de um trajeto, principalmente para quem volta para casa à noite, usa transporte público e caminha parte do caminho. O **VoltaSegura** é uma plataforma de apoio à decisão de mobilidade: compara alternativas de trajeto com base em dados urbanos públicos (como as ocorrências da SSP-SP) e mostra a **exposição estimada** e a **confiabilidade dos dados**, para que a pessoa escolha com mais informação.

Este repositório é a **camada de backend em C#/.NET 8** da solução:

- **Domínio orientado a objetos**, com entidades, interfaces e regras de negócio isoladas;
- **Persistência com Entity Framework Core** (SQLite) e migrações;
- **Web API REST** documentada com Swagger;
- **Testes automatizados** (xUnit) do domínio e da API.

> **Demonstração acadêmica.** A carga padrão tem **80 registros sintéticos** e o roteamento é **mock**. As respostas da API identificam os exemplos (`exemplo: true`). Eles não são dados criminais reais nem resultados validados de mobilidade. O produto **não garante segurança, não prevê crimes e não classifica bairros**.

## Como executar

**Requisitos:** .NET SDK 8 (ou posterior compatível com `net8.0`, com o runtime 8 instalado) e acesso à internet para restaurar os pacotes NuGet. O SQLite acompanha o projeto: não é preciso instalar servidor de banco, Python ou OTP.

### Windows (PowerShell)

Na pasta da solution (onde está `VoltaSegura.sln`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/preparar-demo.ps1
powershell -ExecutionPolicy Bypass -File scripts/iniciar-csharp.ps1
```

1. O **primeiro script** restaura e compila a solution, roda os testes, aplica as migrações e cria a carga sintética em `dados/voltasegura-demo.db`. É idempotente e se recusa a misturar a demonstração com outra carga.
2. O **segundo script** sobe a API em `http://localhost:5032`.
3. Abra o **Swagger** em `http://localhost:5032/swagger` e teste os endpoints.
4. Encerre a API com `Ctrl+C`.

### Linux / macOS (ou passo a passo manual)

```bash
dotnet restore VoltaSegura.sln
dotnet build VoltaSegura.sln --no-restore
dotnet test VoltaSegura.sln --no-build

export VOLTASEGURA_DB="$PWD/dados/voltasegura-demo.db"
dotnet run --no-build --no-launch-profile --project VoltaSegura.Api -- --demonstracao   # migra e carrega os 80 registros
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --no-launch-profile --project VoltaSegura.Api --urls http://localhost:5032
```

Depois, abra `http://localhost:5032/swagger`.

### Observações

- O Swagger só é exposto em ambiente `Development` (os scripts já configuram isso).
- A API **não altera o banco ao iniciar**: migrações e carga só rodam com `--migrar` ou `--demonstracao`.
- **Windows com "Controle de Aplicativo" / Smart App Control:** se aparecer `Uma política de Controle de Aplicativo bloqueou este arquivo (0x800711C7)`, o Windows está bloqueando as DLLs geradas pelo build. Execute `Get-ChildItem -Recurse | Unblock-File` na pasta do projeto, ou rode os comandos acima no WSL/Linux.

---

## Documentação dos endpoints

A documentação interativa (OpenAPI) fica em `/swagger`, gerada a partir dos comentários XML dos controllers. O arquivo `VoltaSegura.Api/VoltaSegura.Api.http` traz requisições prontas para testar no Visual Studio ou no VS Code.

| Método e rota | O que faz | Respostas |
|---|---|---|
| `GET /api/ocorrencias` | Resumo agregado por bairro e faixa do dia | 200, 400 |
| `GET /api/ocorrencias/bairros` | Ranking de bairros por boletins distintos | 200, 400 |
| `GET /api/ocorrencias/naturezas` | Boletins por natureza apurada | 200 |
| `GET /api/ocorrencias/faixas` | Boletins por faixa do dia (inclui `SemFaixa`) | 200 |
| `POST /api/trajetos/resumo` | Calcula o resumo de um trajeto, sem gravar | 200, 400 |
| `GET /api/trajetos` | Lista trajetos salvos (opcional) | 200, 403 |
| `POST /api/trajetos` | Salva um trajeto fictício (opcional) | 201, 400, 403 |
| `GET /api/trajetos/{id}` | Detalha um trajeto salvo (opcional) | 200, 403, 404 |
| `DELETE /api/trajetos/{id}` | Remove um trajeto salvo (opcional) | 204, 403, 404 |

Todas as respostas de ocorrências são **agregadas**: nenhum endpoint devolve boletim individual.

### `GET /api/ocorrencias`

Resumo agregado de boletins distintos por bairro e faixa do dia.

| Parâmetro | Tipo | Descrição |
|---|---|---|
| `bairro` | string | Nome exato do bairro (opcional) |
| `natureza` | string | `NATUREZA_APURADA`, por exemplo `FURTO - OUTROS` (opcional) |
| `rubrica` | string | Rubrica, conservada para auditoria (opcional) |
| `faixa` | int | 1 = madrugada, 2 = manhã, 3 = tarde, 4 = noite (opcional) |

**200** com `periodoDados` (início e fim, ou `null` se não houver dados), `fonte`, `exemplo`, `sobreOsDados` e `resumo` (lista com `bairro`, `faixa`, `boletinsDistintos`; no máximo 200 linhas). **400** se `faixa` estiver fora de 1–4.

### `GET /api/ocorrencias/bairros`

Ranking dos bairros com mais boletins distintos. O percentual é calculado sobre o total que atende aos filtros.

| Parâmetro | Tipo | Descrição |
|---|---|---|
| `top` | int | Tamanho do ranking, de 1 a 50 (padrão 10) |
| `natureza` | string | Filtro por natureza apurada (opcional) |
| `faixa` | int | Filtro por faixa do dia, de 1 a 4 (opcional) |

**200** com `fonte`, `exemplo`, `sobreOsDados` e `itens` (`bairro`, `boletinsDistintos`, `percentual`). **400** para `top` ou `faixa` fora do intervalo.

### `GET /api/ocorrencias/naturezas`

Boletins distintos por natureza apurada. Cada item traz `entraNaComparacao`, que indica se a natureza compõe a comparação de trajetos (`FURTO - OUTROS` e `ROUBO - OUTROS`) ou é apenas contexto. **200** com `itens` (`natureza`, `boletinsDistintos`, `entraNaComparacao`).

### `GET /api/ocorrencias/faixas`

Distribuição pelas quatro faixas do dia (`Madrugada`, `Manha`, `Tarde`, `Noite`) e por `SemFaixa`, que reúne os boletins sem horário informado (a ausência é mostrada, nunca inventada). **200** com `itens` (`faixa`, `boletinsDistintos`, `percentual`).

### `POST /api/trajetos/resumo`

Calcula, **sem gravar nada**, o resumo de um trajeto com as regras do domínio. Exemplo de corpo:

```json
{
  "trechos": [
    { "modo": "WALK",   "comprimentoKm": 0.5 },
    { "modo": "BUS",    "comprimentoKm": 4.0 },
    { "modo": "SUBWAY", "comprimentoKm": 3.0 },
    { "modo": "WALK",   "comprimentoKm": 0.3 }
  ]
}
```

**200**: `distanciaTotalKm` (7,8), `kmCaminhados` (0,8), `percentualCaminhado` (10,3), `quantidadeTrechos` (4), `baldeacoes` (1) e `trechosPorModo`. **400** se não houver trechos, se o `modo` não for `WALK`, `BUS`, `SUBWAY`, `RAIL` ou `TRAM`, ou se `comprimentoKm` for negativo.

### `GET/POST /api/trajetos` e `GET/DELETE /api/trajetos/{id}`

Persistência opcional de trajetos fictícios, **desativada por padrão (403)**: a decisão do produto é que o histórico pessoal fique apenas no aparelho. Para exercitar o CRUD com dados fictícios, defina `Demonstracao__PersistirTrajetos=true` antes de subir a API. Com o recurso habilitado:

- `POST /api/trajetos` recebe `nomeOrigem`, `nomeDestino`, `sentido` e `trechos` (`modo`, `comprimentoKm`, `pontos`) e retorna **201** com o cabeçalho `Location`, ou **400** se os trechos forem inválidos;
- `GET /api/trajetos` lista os 50 trajetos mais recentes;
- `GET /api/trajetos/{id}` detalha o trajeto com os trechos em ordem (**404** se não existir);
- `DELETE /api/trajetos/{id}` remove o trajeto e seus trechos (**204**, ou **404**).

## Banco de dados e migrações

- SQLite via EF Core. O caminho do arquivo vem da variável `VOLTASEGURA_DB` (padrão: `dados/voltasegura.db`).
- Migrações versionadas em `VoltaSegura.Infrastructure/Migrations`: `MigracaoInicial` (ocorrências, trajetos, trechos, registros de consulta) e `ProvenienciaAcervo` (tabela `acervo`, com a origem dos dados).
- Índice único por boletim (`NumeroBo` + `AnoBo` + `Delegacia`), garantindo um registro por boletim distinto.

```powershell
dotnet tool restore                      # dotnet-ef 8.0.11, fixado em .config/dotnet-tools.json
dotnet ef migrations list --project VoltaSegura.Infrastructure --startup-project VoltaSegura.Api
$env:VOLTASEGURA_DB = Join-Path (Get-Location) 'dados/voltasegura-demo.db'
dotnet ef database update --project VoltaSegura.Infrastructure --startup-project VoltaSegura.Api
```

## Configuração

| Variável | Efeito | Padrão |
|---|---|---|
| `VOLTASEGURA_DB` | Caminho do arquivo SQLite | `dados/voltasegura.db` |
| `Demonstracao__PersistirTrajetos` | Habilita listar, salvar, detalhar e remover trajetos | `false` |
| `Roteamento__Modo` | `mock` (padrão) ou `otp2` (adaptador OpenTripPlanner) | `mock` |
| `Otp2__BaseUrl` | Endereço do servidor OTP2, usado só com `Roteamento__Modo=otp2` | `http://localhost:8080` |

Dados reais da SSP-SP são opcionais; o passo a passo está em [DADOS_REAIS.md](DADOS_REAIS.md).

## Testes automatizados

```bash
dotnet test VoltaSegura.sln
```

- **Domínio (unitários):** teste binomial exato, avaliação e estabilidade da comparação, múltiplas alternativas, geometria do corredor.
- **Infraestrutura:** comparação ponta a ponta sobre SQLite e contrato do adaptador OTP2 com respostas controladas.
- **API (integração):** `ApiOcorrenciasTests`, com SQLite em memória e as migrações reais, cobrindo banco vazio, carga de demonstração idempotente, filtros, ranking, estatísticas por natureza e faixa, resumo de trajeto, validação 400, CRUD de trajetos desativado (403) e habilitado (201/204/404), e o conteúdo do OpenAPI.

## Scripts para testar

Além dos scripts de execução, estas são as formas de testar o projeto. Os comandos devem ser rodados na pasta da solution (onde está `VoltaSegura.sln`).

### Testes automatizados com filtros

```powershell
dotnet test VoltaSegura.sln                                                        # todos os testes
dotnet test VoltaSegura.sln --filter "FullyQualifiedName~ApiOcorrenciasTests"      # só a API (integração)
dotnet test VoltaSegura.sln --filter "FullyQualifiedName~TesteBinomialExatoTests"  # só uma classe do domínio
dotnet test VoltaSegura.sln --filter "FullyQualifiedName~Trajetos_"                # testes com esse trecho no nome
dotnet test VoltaSegura.sln --logger "console;verbosity=detailed"                  # lista cada teste executado
dotnet test VoltaSegura.sln --collect:"XPlat Code Coverage"                        # mede a cobertura de código
```

Classes de teste: `ApiOcorrenciasTests`, `AvaliadorComparacaoTests`, `ComparacaoPontaAPontaTests`, `CorredorRegressaoTests`, `EstabilidadeCorredorTests`, `MultiplasAlternativasTests`, `Otp2ContratoTests` e `TesteBinomialExatoTests`.

### Chamadas à API (com a API no ar)

Depois de executar `scripts/iniciar-csharp.ps1`, em outro terminal do PowerShell:

```powershell
$api = 'http://localhost:5032'

# Consultas de ocorrências (dados sintéticos)
Invoke-RestMethod "$api/api/ocorrencias" | ConvertTo-Json -Depth 5
Invoke-RestMethod "$api/api/ocorrencias/bairros?top=5" | ConvertTo-Json -Depth 5
Invoke-RestMethod "$api/api/ocorrencias/naturezas" | ConvertTo-Json -Depth 5
Invoke-RestMethod "$api/api/ocorrencias/faixas" | ConvertTo-Json -Depth 5

# Resumo de trajeto (não grava nada). Esperado: distanciaTotalKm 7,8 e baldeacoes 1
$corpo = @{ trechos = @(
    @{ modo = 'WALK';   comprimentoKm = 0.5 },
    @{ modo = 'BUS';    comprimentoKm = 4.0 },
    @{ modo = 'SUBWAY'; comprimentoKm = 3.0 },
    @{ modo = 'WALK';   comprimentoKm = 0.3 }) } | ConvertTo-Json -Depth 4
Invoke-RestMethod "$api/api/trajetos/resumo" -Method Post -ContentType 'application/json' -Body $corpo

# Respostas de erro (curl.exe mostra o status HTTP)
curl.exe -i "$api/api/ocorrencias?faixa=9"   # esperado: 400
curl.exe -i "$api/api/trajetos"              # esperado: 403 (persistência desativada)
```

Para testar o CRUD de trajetos (201, 200, 204, 404), habilite a persistência **antes** de subir a API, no mesmo terminal:

```powershell
$env:Demonstracao__PersistirTrajetos = 'true'
powershell -ExecutionPolicy Bypass -File scripts/iniciar-csharp.ps1
```

### Requisições prontas no `.http`

O arquivo `VoltaSegura.Api/VoltaSegura.Api.http` tem uma requisição para cada endpoint, inclusive os casos de erro. Abra no Visual Studio ou no VS Code (extensão REST Client) e clique em *Send Request*.

### Migrações

```powershell
dotnet tool restore
dotnet ef migrations list --project VoltaSegura.Infrastructure --startup-project VoltaSegura.Api   # esperado: 2 migrações
```

### Verificação de ponta a ponta

```bash
python scripts/verificar_entrega.py
```

Compila, roda os testes, aplica as migrações, sobe a API, confere as respostas (inclusive 400 e 403) e testa a persistência após reiniciar. Os resultados vão para a pasta `evidencias/`. Requer .NET 8 e Python 3.

## Arquitetura da solution

| Projeto | Papel |
|---|---|
| `VoltaSegura.Domain` | Entidades (`Ocorrencia`, `Trajeto`, `Trecho`, `Acervo`, `RegistroConsulta`), interfaces (`IRoteador`, `IOcorrenciaRepositorio`, `IEstatisticasOcorrencias`) e regras de negócio puras (`AvaliadorComparacao`, `ComparadorAlternativas`, `EstabilidadeCorredor`, `TesteBinomialExato`) |
| `VoltaSegura.Infrastructure` | `VoltaSeguraDbContext` (EF Core + SQLite), migrações, `OcorrenciaRepositorio`, `EstatisticasOcorrencias`, roteadores (`RoteadorMock`, `Otp2Roteador`), registro de dependências e carga de demonstração |
| `VoltaSegura.Api` | Web API REST (controllers, Swagger) |
| `VoltaSegura.Tests` | Testes xUnit: unitários do domínio e de integração da API |

**Tecnologias:** C# 12 · .NET 8 · ASP.NET Core Web API · Entity Framework Core 8 (SQLite) · Swagger/OpenAPI (Swashbuckle) · xUnit.

## Critérios da disciplina e onde estão no código

| Critério | Evidência no código |
|---|---|
| POO — 3,0 | Entidades e serviços de domínio em `VoltaSegura.Domain`; abstração por interfaces (`IRoteador`, `IOcorrenciaRepositorio`, `IEstatisticasOcorrencias`) com duas implementações de roteamento |
| Web API — 2,5 | `VoltaSegura.Api`: controllers REST com códigos HTTP (200/201/204/400/403/404), 9 operações em 7 rotas, parâmetros validados e Swagger com comentários XML |
| EF Core + LINQ — 1,5 | `VoltaSeguraDbContext`, migrações, filtros, `GroupBy`, `OrderBy`/`ThenBy`, `Take`, projeções, `Include`, `AsNoTracking` e agregações (`Min`/`Max`/`Count`) executadas no banco, em `EstatisticasOcorrencias` |
| Async/Await — 1,0 | Controllers, repositórios, carga de demonstração e adaptador HTTP assíncronos, com `CancellationToken` |
| Qualidade do código — 2,0 | Separação em camadas, injeção de dependência (`AdicionarBackend`; controllers dependem de interfaces), validação de entrada, testes automatizados e identificação explícita de dados sintéticos |

## Entregáveis

| Item | Onde está |
|---|---|
| Repositório GitHub com o projeto C# | Este repositório (`VoltaSegura.sln`) |
| Web API | `VoltaSegura.Api` |
| Entity Framework Core | `VoltaSegura.Infrastructure` (contexto, repositórios e migrações) |
| README com execução e descrição da solução | Este arquivo |
| Documentação dos endpoints | Swagger em `/swagger` e a seção acima; requisições prontas em `VoltaSegura.Api/VoltaSegura.Api.http` |
| Evidências da aplicação | Pasta `evidencias/` (build, testes, migrações, respostas HTTP e OpenAPI) |

Para (re)gerar as evidências, com .NET 8 e Python 3, na pasta da solution:

```bash
python scripts/verificar_entrega.py
```

## Limitações

- A carga padrão é sintética; a importação de dados reais da SSP-SP é opcional.
- O roteamento é mock; o adaptador OTP2 foi validado apenas por contrato, com respostas controladas.
- Não há autenticação nem integração com o aplicativo móvel nesta entrega.
- A comparação usa apenas trechos a pé, separa exposição estimada de cobertura dos dados e retorna "dados insuficientes" quando necessário. A cobertura por circunscrição é aproximada a partir dos boletins próximos.
