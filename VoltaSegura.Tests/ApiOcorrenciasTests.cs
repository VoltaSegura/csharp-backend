using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoltaSegura.Infrastructure.Dados;

namespace VoltaSegura.Tests;

/// <summary>Testes de integração da VoltaSegura.Api sobre SQLite em memória, com as migrações reais.</summary>
public class ApiOcorrenciasTests
{
    private static int SomaBoletins(JsonElement json) =>
        json.GetProperty("resumo").EnumerateArray().Sum(r => r.GetProperty("boletinsDistintos").GetInt32());

    [Fact]
    public async Task Migracoes_CriamSchemaEReaplicacaoEIdempotente()
    {
        using var host = new HostApi();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
        var antes = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(2, antes.Count);
        await db.Database.MigrateAsync();
        Assert.Equal(antes, (await db.Database.GetAppliedMigrationsAsync()).ToList());
    }

    [Fact]
    public async Task BancoVazio_RetornaResumoVazioSemPeriodoENaoMarcaComoExemplo()
    {
        using var host = new HostApi();
        var resposta = await host.Client.GetAsync("/api/ocorrencias");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("resumo").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("periodoDados").ValueKind);
        Assert.False(json.GetProperty("exemplo").GetBoolean());
        Assert.Equal(3, json.GetProperty("sobreOsDados").GetArrayLength());
    }

    [Fact]
    public async Task CargaDemonstracao_PersisteIdentificaComoExemploEEIdempotente()
    {
        using var host = new HostApi(demonstracao: true);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
            await DadosDemonstracao.CarregarAsync(db); // segunda carga: não duplica
            Assert.Equal(DadosDemonstracao.TotalRegistros, await db.Ocorrencias.CountAsync());
            Assert.Equal(1, await db.Acervos.CountAsync());
        }
        var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias");
        Assert.True(json.GetProperty("exemplo").GetBoolean());
        Assert.Equal(DadosDemonstracao.TotalRegistros, SomaBoletins(json));
        Assert.Contains("SINTÉTICA", json.GetProperty("fonte").GetString());
    }

    [Fact]
    public async Task Filtros_ReduzemOResumoConformeOBanco()
    {
        using var host = new HostApi(demonstracao: true);
        int esperadoNatureza, esperadoBairro;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
            esperadoNatureza = await db.Ocorrencias.CountAsync(o => o.Natureza == "ROUBO - OUTROS");
            esperadoBairro = await db.Ocorrencias.CountAsync(o => o.Bairro == "República" && o.Faixa == VoltaSegura.Domain.Entidades.FaixaDoDia.Noite);
        }
        var porNatureza = await host.Client.GetFromJsonAsync<JsonElement>(
            "/api/ocorrencias?natureza=" + Uri.EscapeDataString("ROUBO - OUTROS"));
        Assert.Equal(esperadoNatureza, SomaBoletins(porNatureza));

        var porBairroEFaixa = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias?bairro=Rep%C3%BAblica&faixa=4");
        Assert.Equal(esperadoBairro, SomaBoletins(porBairroEFaixa));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task FaixaForaDoIntervalo_Retorna400(int faixa)
    {
        using var host = new HostApi();
        var resposta = await host.Client.GetAsync($"/api/ocorrencias?faixa={faixa}");
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Trajetos_PorPadraoEstaoDesativadosComStatus403()
    {
        using var host = new HostApi();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/trajetos")).StatusCode);
        var corpo = new { nomeOrigem = "A", nomeDestino = "B", sentido = "volta", trechos = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync("/api/trajetos", corpo)).StatusCode);
    }

    [Fact]
    public async Task Trajetos_HabilitadosSalvamEListam_EValidamTrechos()
    {
        using var host = new HostApi(persistirTrajetos: true);
        var vazio = new { nomeOrigem = "A", nomeDestino = "B", sentido = "volta", trechos = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/trajetos", vazio)).StatusCode);

        var valido = new
        {
            nomeOrigem = "Origem fictícia", nomeDestino = "Destino fictício", sentido = "volta",
            trechos = new[]
            {
                new
                {
                    modo = "WALK", comprimentoKm = 0.5,
                    pontos = new[] { new { latitude = -23.56, longitude = -46.65 }, new { latitude = -23.561, longitude = -46.651 } }
                }
            }
        };
        var criado = await host.Client.PostAsJsonAsync("/api/trajetos", valido);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);

        var lista = await host.Client.GetFromJsonAsync<JsonElement>("/api/trajetos");
        Assert.Equal(1, lista.GetArrayLength());
        Assert.Equal(1, lista[0].GetProperty("quantidadeTrechos").GetInt32());
        Assert.Equal(0.5, lista[0].GetProperty("kmCaminhados").GetDouble());
    }

    [Fact]
    public async Task RankingBairros_OrdenaPorQuantidadeELimitaPeloTop()
    {
        using var host = new HostApi(demonstracao: true);
        var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias/bairros?top=3");
        Assert.True(json.GetProperty("exemplo").GetBoolean());
        var itens = json.GetProperty("itens").EnumerateArray().ToList();
        Assert.Equal(3, itens.Count);
        var contagens = itens.Select(i => i.GetProperty("boletinsDistintos").GetInt32()).ToList();
        Assert.Equal(contagens.OrderByDescending(c => c).ToList(), contagens);
        Assert.All(itens, i => Assert.InRange(i.GetProperty("percentual").GetDouble(), 0, 100));
    }

    [Fact]
    public async Task RankingBairros_BancoVazio_RetornaListaVazia()
    {
        using var host = new HostApi();
        var vazio = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias/bairros");
        Assert.Equal(0, vazio.GetProperty("itens").GetArrayLength());
    }

    [Theory]
    [InlineData("top=0")]
    [InlineData("top=51")]
    [InlineData("faixa=7")]
    public async Task RankingBairros_ParametrosInvalidos_Retornam400(string consulta)
    {
        using var host = new HostApi();
        var resposta = await host.Client.GetAsync("/api/ocorrencias/bairros?" + consulta);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Naturezas_SomamOTotalEIndicamQuaisEntramNaComparacao()
    {
        using var host = new HostApi(demonstracao: true);
        var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias/naturezas");
        var itens = json.GetProperty("itens").EnumerateArray().ToList();
        Assert.Equal(DadosDemonstracao.TotalRegistros, itens.Sum(i => i.GetProperty("boletinsDistintos").GetInt32()));
        bool Entra(string natureza) => itens.Single(i => i.GetProperty("natureza").GetString() == natureza)
            .GetProperty("entraNaComparacao").GetBoolean();
        Assert.True(Entra("ROUBO - OUTROS"));
        Assert.False(Entra("FURTO DE VEÍCULO"));
    }

    [Fact]
    public async Task Faixas_ListamTodasAsFaixasEPreservamOsRegistrosSemFaixa()
    {
        using var host = new HostApi(demonstracao: true);
        int semFaixaNoBanco;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
            semFaixaNoBanco = await db.Ocorrencias.CountAsync(o => o.Faixa == null);
        }
        var json = await host.Client.GetFromJsonAsync<JsonElement>("/api/ocorrencias/faixas");
        var itens = json.GetProperty("itens").EnumerateArray().ToList();
        Assert.Equal(5, itens.Count);
        Assert.Equal(DadosDemonstracao.TotalRegistros, itens.Sum(i => i.GetProperty("boletinsDistintos").GetInt32()));
        var semFaixa = itens.Single(i => i.GetProperty("faixa").GetString() == "SemFaixa");
        Assert.True(semFaixaNoBanco > 0);
        Assert.Equal(semFaixaNoBanco, semFaixa.GetProperty("boletinsDistintos").GetInt32());
    }

    [Fact]
    public async Task ResumoDeTrajeto_CalculaComAsRegrasDoDominioSemPersistir()
    {
        using var host = new HostApi(); // persistência desativada: o resumo continua disponível
        var corpo = new
        {
            trechos = new object[]
            {
                new { modo = "walk", comprimentoKm = 0.5 },
                new { modo = "BUS", comprimentoKm = 4.0 },
                new { modo = "SUBWAY", comprimentoKm = 3.0 },
                new { modo = "WALK", comprimentoKm = 0.3 },
            }
        };
        var resposta = await host.Client.PostAsJsonAsync("/api/trajetos/resumo", corpo);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7.8, json.GetProperty("distanciaTotalKm").GetDouble(), 2);
        Assert.Equal(0.8, json.GetProperty("kmCaminhados").GetDouble(), 2);
        Assert.Equal(10.3, json.GetProperty("percentualCaminhado").GetDouble(), 1);
        Assert.Equal(4, json.GetProperty("quantidadeTrechos").GetInt32());
        Assert.Equal(1, json.GetProperty("baldeacoes").GetInt32());
        Assert.Equal(2, json.GetProperty("trechosPorModo").GetProperty("WALK").GetInt32());

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
        Assert.Equal(0, await db.TrajetosSalvos.CountAsync());
    }

    [Theory]
    [InlineData("{\"trechos\":[]}")]
    [InlineData("{\"trechos\":[{\"modo\":\"FOGUETE\",\"comprimentoKm\":1}]}")]
    [InlineData("{\"trechos\":[{\"modo\":\"WALK\",\"comprimentoKm\":-2}]}")]
    public async Task ResumoDeTrajeto_EntradaInvalida_Retorna400(string corpo)
    {
        using var host = new HostApi();
        var resposta = await host.Client.PostAsync("/api/trajetos/resumo",
            new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Trajetos_DetalheERemocaoTambemFicamDesativadosPorPadrao()
    {
        using var host = new HostApi();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/trajetos/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.DeleteAsync("/api/trajetos/1")).StatusCode);
    }

    [Fact]
    public async Task Trajetos_ObterERemover_FuncionamQuandoHabilitados()
    {
        using var host = new HostApi(persistirTrajetos: true);
        var corpo = new
        {
            nomeOrigem = "Origem fictícia", nomeDestino = "Destino fictício", sentido = "ida",
            trechos = new[] { new { modo = "BUS", comprimentoKm = 2.5 } }
        };
        var criado = await host.Client.PostAsJsonAsync("/api/trajetos", corpo);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var id = (await criado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var detalhe = await host.Client.GetFromJsonAsync<JsonElement>($"/api/trajetos/{id}");
        Assert.Equal(1, detalhe.GetProperty("trechos").GetArrayLength());
        Assert.Equal("BUS", detalhe.GetProperty("trechos")[0].GetProperty("modo").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/trajetos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/trajetos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync($"/api/trajetos/{id}")).StatusCode);
    }

    [Fact]
    public async Task Trajetos_RejeitaModoDesconhecidoAoSalvar()
    {
        using var host = new HostApi(persistirTrajetos: true);
        var corpo = new
        {
            nomeOrigem = "A", nomeDestino = "B", sentido = "ida",
            trechos = new[] { new { modo = "FOGUETE", comprimentoKm = 1.0 } }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/trajetos", corpo)).StatusCode);
    }

    [Fact]
    public async Task Swagger_DocumentaOsEndpointsDaApi()
    {
        using var host = new HostApi();
        var doc = await host.Client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var caminhos = doc.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToHashSet();
        var esperados = new[]
        {
            "/api/ocorrencias", "/api/ocorrencias/bairros", "/api/ocorrencias/naturezas", "/api/ocorrencias/faixas",
            "/api/trajetos", "/api/trajetos/{id}", "/api/trajetos/resumo",
        };
        Assert.Equal(esperados.Length, caminhos.Count);
        Assert.All(esperados, c => Assert.Contains(c, caminhos));
    }
}

internal sealed class HostApi : WebApplicationFactory<ApiEntryPoint>
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly bool _persistirTrajetos;
    public HttpClient Client { get; }

    public HostApi(bool demonstracao = false, bool persistirTrajetos = false)
    {
        _persistirTrajetos = persistirTrajetos;
        _conexao.Open();
        Client = CreateClient();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoltaSeguraDbContext>();
        db.Database.Migrate();
        if (demonstracao) DadosDemonstracao.CarregarAsync(db).GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Demonstracao:PersistirTrajetos"] = _persistirTrajetos ? "true" : "false" }));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<DbContextOptions<VoltaSeguraDbContext>>();
            s.AddDbContext<VoltaSeguraDbContext>(o => o.UseSqlite(_conexao));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { Client.Dispose(); _conexao.Dispose(); }
    }
}
