using Microsoft.AspNetCore.Mvc;
using VoltaSegura.Domain.Entidades;
using VoltaSegura.Domain.Interfaces;

namespace VoltaSegura.Api.Controllers;

/// <summary>
/// Estatísticas agregadas das ocorrências do acervo. O controller depende apenas da interface
/// <see cref="IEstatisticasOcorrencias"/> (injeção de dependência); a consulta ao banco fica na
/// infraestrutura. Respostas sempre agregadas e com a fonte/indicador de exemplo explícitos.
/// </summary>
[ApiController]
[Route("api/ocorrencias")]
[Produces("application/json")]
public class EstatisticasController : ControllerBase
{
    private readonly IEstatisticasOcorrencias _estatisticas;

    public EstatisticasController(IEstatisticasOcorrencias estatisticas) => _estatisticas = estatisticas;

    /// <summary>Ranking dos bairros com mais boletins distintos.</summary>
    /// <param name="top">Quantidade de bairros no ranking (1 a 50; padrão 10).</param>
    /// <param name="natureza">Filtro opcional por NATUREZA_APURADA (ex.: "ROUBO - OUTROS").</param>
    /// <param name="faixa">Filtro opcional por faixa do dia (1=madrugada, 2=manhã, 3=tarde, 4=noite).</param>
    /// <param name="ct">Cancelamento da consulta.</param>
    /// <remarks>O percentual é calculado sobre o total de boletins que atendem aos filtros, não só sobre o ranking.</remarks>
    [HttpGet("bairros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RankingBairros(
        [FromQuery] int top = 10, [FromQuery] string? natureza = null, [FromQuery] int? faixa = null,
        CancellationToken ct = default)
    {
        if (top < 1 || top > 50)
            return BadRequest(new { erro = "Top deve estar entre 1 e 50." });
        if (faixa.HasValue && (faixa < 1 || faixa > 4))
            return BadRequest(new { erro = "Faixa deve estar entre 1 e 4." });

        var filtroFaixa = faixa.HasValue ? (FaixaDoDia)faixa.Value : (FaixaDoDia?)null;
        var ranking = await _estatisticas.RankingBairrosAsync(top, natureza, filtroFaixa, ct);
        return Ok(await Envelope(ranking, ct));
    }

    /// <summary>Boletins distintos por natureza apurada, indicando quais compõem a comparação de trajetos.</summary>
    [HttpGet("naturezas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PorNatureza(CancellationToken ct)
        => Ok(await Envelope(await _estatisticas.PorNaturezaAsync(ct), ct));

    /// <summary>Distribuição dos boletins pelas faixas do dia, incluindo os registros sem faixa informada.</summary>
    [HttpGet("faixas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PorFaixa(CancellationToken ct)
        => Ok(await Envelope(await _estatisticas.PorFaixaAsync(ct), ct));

    private async Task<object> Envelope<T>(IReadOnlyList<T> itens, CancellationToken ct)
    {
        var acervo = await _estatisticas.ObterAcervoAsync(ct);
        return new
        {
            fonte = acervo?.Fonte ?? "Acervo sem proveniência registrada",
            exemplo = acervo?.Tipo == "demonstracao",
            sobreOsDados = SobreOsDados.FrasesFixas,
            itens,
        };
    }
}
