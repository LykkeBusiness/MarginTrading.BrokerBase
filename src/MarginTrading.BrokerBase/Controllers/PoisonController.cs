using System;
using System.Net;

using Lykke.Common.Api.Contract.Responses;
using Lykke.MarginTrading.BrokerBase.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lykke.MarginTrading.BrokerBase.Controllers;

[Authorize]
[Route("api/[controller]")]
public class PoisonController : Controller
{
    private readonly IRabbitMqPoisonQueueHandler _rabbitMqPoisonQueueHandler;
    private readonly ILogger<PoisonController> _logger;

    public PoisonController(IRabbitMqPoisonQueueHandler rabbitMqPoisonQueueHandler, ILogger<PoisonController> logger)
    {
        _rabbitMqPoisonQueueHandler = rabbitMqPoisonQueueHandler;
        _logger = logger;
    }

    [HttpPost("put-messages-back")]
    public IActionResult PutMessagesBack()
    {
        try
        {
            var result = _rabbitMqPoisonQueueHandler.TryPutMessagesBack();

            if (string.IsNullOrEmpty(result))
            {
                _logger.LogWarning("No messages found in poison queue");
                return NotFound(ErrorResponse.Create("No messages found in poison queue"));
            }

            _logger.LogInformation(result);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to put messages back: {error}", ex.Message);
            return StatusCode((int)HttpStatusCode.InternalServerError, ErrorResponse.Create("Failed to put messages back"));
        }
    }
}