using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoltaSegura.Domain.Entidades;
using VoltaSegura.Infrastructure.Dados;

namespace VoltaSegura.Api.Controllers;

/// <summary>
/// Trajetos do cidadão (ida e volta). O histórico pessoal fica no aparelho: a persistência
/// no servidor (listar, obter, salvar e remover) é opcional e vem DESATIVADA por padrão (403).
/// O cálculo de resumo é sem estado — não grava nada — e fica sempre disponível.
/// </summary>
[ApiController]
[Route("api/trajetos")]
[Produces("application/json")]
public class TrajetosController : ControllerBase
{
    private static readonly string[] ModosValidos = { "WALK", "BUS", "SUBWAY", "RAIL", "TRAM" };

    private readonly VoltaSeguraDbContext _contexto;
    private readonly IConfiguration _config;

    public TrajetosController(VoltaSeguraDbContext contexto, IConfiguration config)
        => (_contexto, _config) = (contexto, config);

    private bool PersistenciaHabilitada => _config.GetValue<bool>("Demonstracao:PersistirTrajetos");

    private ObjectResult PersistenciaDesativada() => StatusCode(403, new
    {
        erro = "Histórico no servidor desativado; trajetos pessoais ficam apenas no aparelho. " +
               "Apenas dados fictícios em ambiente de demonstração podem usar este recurso."
    });

    /// <summary>Lista os trajetos salvos (os 50 mais recentes).</summary>
    /// <response code="403">Persistência desativada (padrão).</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        if (!PersistenciaHabilitada) return PersistenciaDesativada();
        var trajetos = await _contexto.TrajetosSalvos
            .AsNoTracking()
            .Include(t => t.Trechos)
            .OrderByDescending(t => t.Id)
            .Take(50)
            .Select(t => new
            {
                t.Id,
                t.NomeOrigem,
                t.NomeDestino,
                t.Sentido,
                DistanciaTotalKm = Math.Round(t.DistanciaTotalKm, 2),
                KmCaminhados = Math.Round(t.KmCaminhados, 2),
                QuantidadeTrechos = t.Trechos.Count,
            })
            .ToListAsync(ct);
        return Ok(trajetos);
    }

    /// <summary>Detalha um trajeto salvo, com seus trechos em ordem.</summary>
    /// <param name="id">Identificador do trajeto.</param>
    /// <param name="ct">Cancelamento da consulta.</param>
    [HttpGet("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(long id, CancellationToken ct)
    {
        if (!PersistenciaHabilitada) return PersistenciaDesativada();
        var trajeto = await _contexto.TrajetosSalvos
            .AsNoTracking()
            .Include(t => t.Trechos)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trajeto is null) return NotFound(new { erro = "Trajeto não encontrado." });

        return Ok(new
        {
            trajeto.Id,
            trajeto.NomeOrigem,
            trajeto.NomeDestino,
            trajeto.Sentido,
            DistanciaTotalKm = Math.Round(trajeto.DistanciaTotalKm, 2),
            KmCaminhados = Math.Round(trajeto.KmCaminhados, 2),
            Trechos = trajeto.Trechos.OrderBy(t => t.Ordem).Select(t => new
            {
                t.Ordem,
                t.Modo,
                t.ComprimentoKm,
                Pontos = t.Pontos(),
            }).ToList(),
        });
    }

    /// <summary>Salva um trajeto (ida ou volta) com seus trechos.</summary>
    /// <response code="201">Trajeto criado.</response>
    /// <response code="400">Trechos ausentes ou inválidos.</response>
    /// <response code="403">Persistência desativada (padrão).</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Salvar([FromBody] RequisicaoTrajeto requisicao, CancellationToken ct)
    {
        if (!PersistenciaHabilitada) return PersistenciaDesativada();
        var erro = ValidarTrechos(requisicao?.Trechos);
        if (erro is not null) return BadRequest(new { erro });

        var trajeto = new Trajeto
        {
            NomeOrigem = requisicao!.NomeOrigem,
            NomeDestino = requisicao.NomeDestino,
            Sentido = requisicao.Sentido,
            Trechos = requisicao.Trechos.Select((t, i) => CriarTrecho(t, i)).ToList(),
        };

        _contexto.TrajetosSalvos.Add(trajeto);
        await _contexto.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Obter), new { id = trajeto.Id }, new
        {
            trajeto.Id,
            trajeto.NomeOrigem,
            trajeto.NomeDestino,
            trajeto.Sentido,
        });
    }

    /// <summary>Remove um trajeto salvo e seus trechos.</summary>
    /// <param name="id">Identificador do trajeto.</param>
    /// <param name="ct">Cancelamento da operação.</param>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remover(long id, CancellationToken ct)
    {
        if (!PersistenciaHabilitada) return PersistenciaDesativada();
        var trajeto = await _contexto.TrajetosSalvos
            .Include(t => t.Trechos)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (trajeto is null) return NotFound(new { erro = "Trajeto não encontrado." });

        _contexto.RemoveRange(trajeto.Trechos);
        _contexto.Remove(trajeto);
        await _contexto.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Calcula o resumo de um trajeto sem gravá-lo: distâncias, % caminhado, baldeações e trechos por modo.</summary>
    /// <remarks>
    /// Usa as regras do domínio (<c>Trajeto.DistanciaTotalKm</c>, <c>KmCaminhados</c> e <c>Baldeacoes</c>).
    /// Nada é persistido e as coordenadas informadas não são registradas.
    /// </remarks>
    /// <response code="400">Trechos ausentes, modo desconhecido ou comprimento inválido.</response>
    [HttpPost("resumo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Resumir([FromBody] RequisicaoResumo requisicao)
    {
        var erro = ValidarTrechos(requisicao.Trechos);
        if (erro is not null) return BadRequest(new { erro });

        var trajeto = new Trajeto { Trechos = requisicao.Trechos.Select((t, i) => CriarTrecho(t, i)).ToList() };
        var total = trajeto.DistanciaTotalKm;
        var caminhados = trajeto.KmCaminhados;

        return Ok(new
        {
            distanciaTotalKm = Math.Round(total, 2),
            kmCaminhados = Math.Round(caminhados, 2),
            percentualCaminhado = total > 0 ? Math.Round(100 * caminhados / total, 1) : 0,
            quantidadeTrechos = trajeto.Trechos.Count,
            baldeacoes = trajeto.Baldeacoes,
            trechosPorModo = trajeto.Trechos.GroupBy(t => t.Modo).ToDictionary(g => g.Key, g => g.Count()),
        });
    }

    private static Trecho CriarTrecho(TrechoRequisicao t, int ordem)
        => Trecho.Criar(ordem, t.Modo.ToUpperInvariant(), t.ComprimentoKm, t.Pontos ?? new List<Coordenada>());

    private static string? ValidarTrechos(List<TrechoRequisicao>? trechos)
    {
        if (trechos is null || trechos.Count == 0)
            return "informe ao menos um trecho";
        if (trechos.Any(t => t is null || !ModosValidos.Contains(t.Modo ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
            return "modo inválido: use WALK, BUS, SUBWAY, RAIL ou TRAM";
        if (trechos.Any(t => !double.IsFinite(t.ComprimentoKm) || t.ComprimentoKm < 0))
            return "comprimentoKm deve ser um número maior ou igual a zero";
        return null;
    }

    /// <summary>Corpo de criação de trajeto.</summary>
    public sealed record RequisicaoTrajeto(string NomeOrigem, string NomeDestino, string Sentido, List<TrechoRequisicao> Trechos);

    /// <summary>Corpo do cálculo de resumo (apenas os trechos).</summary>
    public sealed record RequisicaoResumo(List<TrechoRequisicao> Trechos);

    /// <summary>Trecho do corpo da requisição.</summary>
    /// <param name="Modo">WALK, BUS, SUBWAY, RAIL ou TRAM.</param>
    /// <param name="ComprimentoKm">Distância do trecho, em km (≥ 0).</param>
    /// <param name="Pontos">Coordenadas de exemplo (opcional).</param>
    public sealed record TrechoRequisicao(string Modo, double ComprimentoKm, List<Coordenada>? Pontos);
}
