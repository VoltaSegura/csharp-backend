using VoltaSegura.Domain.Entidades;

namespace VoltaSegura.Domain.Interfaces;

/// <summary>
/// Consultas estatísticas AGREGADAS sobre as ocorrências do acervo. Nenhum método devolve
/// boletim individual: só contagens de boletins distintos e percentuais.
/// </summary>
public interface IEstatisticasOcorrencias
{
    /// <summary>Proveniência do acervo carregado (nula quando não há carga).</summary>
    Task<Acervo?> ObterAcervoAsync(CancellationToken ct);

    /// <summary>Bairros com mais boletins distintos, opcionalmente filtrados por natureza e faixa do dia.</summary>
    Task<IReadOnlyList<ContagemBairro>> RankingBairrosAsync(
        int top, string? natureza, FaixaDoDia? faixa, CancellationToken ct);

    /// <summary>Boletins por natureza apurada, indicando se a natureza compõe a comparação de trajetos.</summary>
    Task<IReadOnlyList<ContagemNatureza>> PorNaturezaAsync(CancellationToken ct);

    /// <summary>Boletins por faixa do dia, incluindo os sem faixa informada.</summary>
    Task<IReadOnlyList<ContagemFaixa>> PorFaixaAsync(CancellationToken ct);
}

/// <summary>Bairro com a quantidade de boletins distintos e a participação (%) no total filtrado.</summary>
public sealed record ContagemBairro(string Bairro, int BoletinsDistintos, double Percentual);

/// <summary>Natureza apurada; <paramref name="EntraNaComparacao"/> indica se compõe N na comparação.</summary>
public sealed record ContagemNatureza(string Natureza, int BoletinsDistintos, bool EntraNaComparacao);

/// <summary>Faixa do dia (ou "SemFaixa") com a quantidade de boletins e o percentual do total.</summary>
public sealed record ContagemFaixa(string Faixa, int BoletinsDistintos, double Percentual);
