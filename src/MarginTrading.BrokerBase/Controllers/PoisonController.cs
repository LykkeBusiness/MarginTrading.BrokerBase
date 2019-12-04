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
        private readonly IRabbitPoisonHandingService _rabbitPoisonHandingService;
        
        public PoisonController(IRabbitPoisonHandingService rabbitPoisonHandingService)
        {
            _rabbitPoisonHandingService = rabbitPoisonHandingService;
        }

        [HttpPost("put-messages-back")]
        public async Task PutMessagesBack()
        {
            await _rabbitPoisonHandingService.PutMessagesBack();
        }
    }
}