using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri ürünleri ve ilişkili kayıtları (senkron geçmişi, paylaşım blokları vb.) topluca siler.</summary>
public interface ICimriStoredDataCleaner
{
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
