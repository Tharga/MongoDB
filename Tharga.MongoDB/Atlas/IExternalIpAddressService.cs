using System.Net;
using System.Threading.Tasks;

namespace Tharga.MongoDB.Atlas;

public interface IExternalIpAddressService
{
    Task<IPAddress> GetExternalIpAddressAsync();
}