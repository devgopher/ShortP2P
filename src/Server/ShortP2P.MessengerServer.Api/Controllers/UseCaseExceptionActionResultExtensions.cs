using Microsoft.AspNetCore.Mvc;
using ShortP2P.MessengerServer.Contracts.Dtos;
using ShortP2P.MessengerServer.UseCases;

namespace ShortP2P.MessengerServer.Api.Controllers;

internal static class UseCaseExceptionActionResultExtensions
{
    public static IActionResult ToApiErrorResult(this ControllerBase controller, UseCaseException ex) =>
        controller.StatusCode(
            ex.Code switch
            {
                "Validation" => StatusCodes.Status400BadRequest,
                "Unauthorized" => StatusCodes.Status401Unauthorized,
                "NotFound" => StatusCodes.Status404NotFound,
                "Conflict" => StatusCodes.Status409Conflict,
                "Unavailable" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            },
            new ApiError { Code = ex.Code, Message = ex.Message });
}

