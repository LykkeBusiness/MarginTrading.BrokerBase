using System.Threading.Tasks;
using MarginTrading.BrokerBase.Entities;

namespace MarginTrading.BrokerBase.Repositories
{
    public interface ILogRepository
    {
        Task Insert(ILogEntity log);
    }
}