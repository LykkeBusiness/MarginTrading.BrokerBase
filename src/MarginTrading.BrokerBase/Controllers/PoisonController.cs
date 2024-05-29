using System.Threading.Tasks;
using Lykke.MarginTrading.BrokerBase.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lykke.MarginTrading.BrokerBase.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class PoisonController : Controller
    {
        private readonly IRabbitMqPoisonQueueHandler _rabbitMqPoisonQueueHandler;
        
        public PoisonController(IRabbitMqPoisonQueueHandler rabbitMqPoisonQueueHandler)
        {
            _rabbitMqPoisonQueueHandler = rabbitMqPoisonQueueHandler;
        }

        [HttpPost("put-messages-back")]
        public async Task<string> PutMessagesBack()
        {
            return await _rabbitMqPoisonQueueHandler.PutMessagesBack();
        }
    }
}