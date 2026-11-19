using Microsoft.EntityFrameworkCore;
using VoltaSegura.Domain.Entidades;
using VoltaSegura.Domain.Interfaces;
using VoltaSegura.Infrastructure.Dados;

namespace VoltaSegura.Infrastructure.Repositorios;

/// <summary>
/// Implementação das estatísticas com EF Core + LINQ. A agregação (GroupBy/Count/ordenação)
/// roda no banco; só o resultado já agregado é materializado. O índice único por boletim
/// garante um registro por boletim distinto, então contar linhas conta boletins distintos.
/// </summary>
public class EstatisticasOcorrencias : IEstatisticasOcorrencias
{
    private readonly VoltaSeguraDbContext _contexto;

    public EstatisticasOcorrencias(VoltaSeguraDbContext contexto) => _contexto = contexto;

    public Task<Acervo?> ObterAcervoAsync(CancellationToken ct)
        => _contexto.Acervos.AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ContagemBairro>> RankingBairrosAsync(
        int top, string? natureza, FaixaDoDia? faixa, CancellationToken ct)
    {
        var consulta = _contexto.Ocorrencias.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(natureza))
            consulta = consulta.Where(o => o.Natureza == natureza);
        if (faixa.HasValue)
            consulta = consulta.Where(o => o.Faixa == faixa);

        var total = await consulta.CountAsync(ct);
        if (total == 0) return Array.Empty<ContagemBairro>();

        var grupos = await consulta
            .GroupBy(o => o.Bairro)
            .Select(g => new { Bairro = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Bairro)
            .Take(top)
            .ToListAsync(ct);

        return grupos.Select(g => new ContagemBairro(g.Bairro, g.Total, Percentual(g.Total, total))).ToList();
    }

    public async Task<IReadOnlyList<ContagemNatureza>> PorNaturezaAsync(CancellationToken ct)
    {
        var grupos = await _contexto.Ocorrencias.AsNoTracking()
            .GroupBy(o => o.Natureza)
            .Select(g => new { Natureza = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Natureza)
            .ToListAsync(ct);

        return grupos
            .Select(g => new ContagemNatureza(g.Natureza, g.Total, Ocorrencia.NaturezasComparacao.Contains(g.Natureza)))
            .ToList();
    }

    public async Task<IReadOnlyList<ContagemFaixa>> PorFaixaAsync(CancellationToken ct)
    {
        var grupos = await _contexto.Ocorrencias.AsNoTracking()
            .GroupBy(o => o.Faixa)
            .Select(g => new { Faixa = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var total = grupos.Sum(g => g.Total);
        int Contar(FaixaDoDia? faixa) => grupos.Where(g => g.Faixa == faixa).Sum(g => g.Total);

        // Todas as faixas aparecem (mesmo com zero); a ausência de faixa é exibida, nunca escondida.
        var lista = Enum.GetValues<FaixaDoDia>()
            .Select(f => new ContagemFaixa(f.ToString(), Contar(f), Percentual(Contar(f), total)))
            .ToList();
        lista.Add(new ContagemFaixa("SemFaixa", Contar(null), Percentual(Contar(null), total)));
        return lista;
    }

    private static double Percentual(int parte, int total)
        => total == 0 ? 0 : Math.Round(100.0 * parte / total, 1);
}
